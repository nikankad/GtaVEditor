using System.Security.Cryptography;
using SaveEditor.GtaV;

namespace SaveEditor.Tests;

public sealed class EditorWorkflowTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory().FullName;
    private readonly byte[] _key = RandomNumberGenerator.GetBytes(32);

    private string Save(string fileName, string title = "Original")
    {
        var path = Path.GetFullPath(Path.Combine(_folder, fileName));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, GtaVSaveTests.BuildSaveFile(_key, title, GtaVSaveTests.BuildBody(("SAVE", new byte[32]))));
        return path;
    }

    [Fact]
    public void SaveAs_MovesActivePath_AndFollowingSaveLeavesOriginalAlone()
    {
        var original = Save("SGTA50000");
        var originalBytes = File.ReadAllBytes(original);
        var copy = Path.Combine(_folder, "SGTA50001");
        var doc = GtaVSaveDocument.Load(original, _key);
        doc.Title = "Copy";
        doc.Save(copy);
        Assert.Equal(copy, doc.FilePath);
        Assert.False(doc.HasChanges);
        doc.Title = "Second edit";
        doc.Save(doc.FilePath);
        Assert.Equal(originalBytes, File.ReadAllBytes(original));
        Assert.Equal("Second edit", GtaVSaveDocument.Load(copy, _key).Title);
    }

    [Fact]
    public void FailedSave_KeepsActivePathAndUnsavedEdits()
    {
        var original = Save("SGTA50000");
        var doc = GtaVSaveDocument.Load(original, _key);
        doc.Title = "Edit";
        Assert.Throws<DirectoryNotFoundException>(() => doc.Save(Path.Combine(_folder, "missing", "SGTA50001")));
        Assert.Equal(original, doc.FilePath);
        Assert.True(doc.HasChanges);
    }

    [Fact]
    public void RevertingDataAndTitle_ClearsUnsavedState_WithoutMissingOtherFields()
    {
        var doc = GtaVSaveDocument.Load(Save("SGTA50000"), _key);
        Assert.False(doc.HasChanges);
        doc.Body.Blocks[0].Data[0] = 42;
        Assert.True(doc.HasChanges);
        doc.Body.Blocks[0].Data[0] = 0;
        Assert.False(doc.HasChanges);
        doc.Title = "Edited";
        Assert.True(doc.HasChanges);
        doc.Title = "Original";
        Assert.False(doc.HasChanges);
        doc.Body.Blocks[0].Data[1] = 12;
        _ = doc.ToBytes(); // recalculates checksum, without saving
        doc.Body.Blocks[0].Data[1] = 0;
        Assert.False(doc.HasChanges);
    }

    [Fact]
    public void SuccessfulSave_RebasesTheDirtyComparison()
    {
        var doc = GtaVSaveDocument.Load(Save("SGTA50000"), _key);
        doc.Body.Blocks[0].Data[0] = 42;
        doc.Save(doc.FilePath);
        Assert.False(doc.HasChanges);
        doc.Body.Blocks[0].Data[0] = 0;
        Assert.True(doc.HasChanges);
    }

    [Fact]
    public void Discovery_FindsManualAndAutosavesAcrossProfiles_NewestFirst()
    {
        var older = Save("profile-a/SGTA50000", "Older");
        var newer = Save("profile-b/SGTA50015", "Autosave");
        File.SetLastWriteTime(older, new DateTime(2020, 1, 1));
        File.SetLastWriteTime(newer, new DateTime(2021, 1, 1));
        Save("profile-a/SGTA50000.bak");
        Save("profile-a/SGTA50099");
        File.WriteAllText(Path.Combine(_folder, "profile-a", "SGTA50001"), "invalid");
        var saves = DetectedSave.Find([Path.Combine(_folder, "profile-a"), Path.Combine(_folder, "missing"),
            Path.Combine(_folder, "profile-b"), Path.Combine(_folder, "profile-a")]);
        Assert.Equal([newer, older], saves.Select(s => s.FilePath));
        Assert.Equal("Autosave", saves[0].Slot);
        Assert.Equal("Slot 1", saves[1].Slot);
        Assert.Equal("Older", saves[1].Title);
        Assert.Equal("profile-a", saves[1].Profile);
    }

    [Fact]
    public void Backups_RememberSource_AndRepeatedBackupsHaveUniqueNames()
    {
        var source = Save("SGTA50000");
        var service = new BackupService(Path.Combine(_folder, "backups"));
        var first = service.Backup(source);
        var second = service.Backup(source);
        Assert.NotEqual(first, second);
        Assert.Equal(2, service.Entries().Count);
        Assert.All(service.Entries(), b => { Assert.Equal(source, b.OriginalPath); Assert.Equal("Original", b.Title); });
    }

    [Fact]
    public void LegacyBackup_UsesTimestampInName_AndWorksWithoutMetadata()
    {
        var service = new BackupService(Path.Combine(_folder, "backups"));
        Directory.CreateDirectory(service.BackupRoot);
        var legacy = Path.Combine(service.BackupRoot, "SGTA50000.20200102-030405-006.bak");
        File.Copy(Save("SGTA50000"), legacy);
        var entry = Assert.Single(service.Entries());
        Assert.Null(entry.OriginalPath);
        Assert.Equal(new DateTime(2020, 1, 2, 3, 4, 5, 6), entry.Created);
        Assert.Equal("Slot 1", entry.Slot);
        File.WriteAllText(legacy + ".json", "invalid json");
        Assert.Null(Assert.Single(service.Entries()).OriginalPath);
    }

    [Fact]
    public void Restore_PreservesSourceBackup_AndBacksUpReplacedSave()
    {
        var source = Save("SGTA50000");
        var service = new BackupService(Path.Combine(_folder, "backups"));
        var backup = service.Backup(source)!;
        var before = File.ReadAllBytes(backup);
        var doc = GtaVSaveDocument.Load(source, _key);
        doc.Title = "Newer";
        doc.Save(source);
        var previous = service.Restore(backup, source);
        Assert.NotNull(previous);
        Assert.Equal("Newer", GtaVSaveDocument.Load(previous!, _key).Title);
        Assert.Equal(before, File.ReadAllBytes(source));
        Assert.Equal(before, File.ReadAllBytes(backup));
    }

    [Fact]
    public void Restore_ToEmptySlot_DoesNotCreateAnUnnecessaryBackup()
    {
        var service = new BackupService(Path.Combine(_folder, "backups"));
        var backup = service.Backup(Save("SGTA50000"))!;
        var destination = Path.Combine(_folder, "SGTA50001");
        Assert.Null(service.Restore(backup, destination));
        Assert.Equal(File.ReadAllBytes(backup), File.ReadAllBytes(destination));
        Assert.Single(service.Entries());
    }

    [Fact]
    public void InvalidRestore_LeavesDestinationUntouched()
    {
        var source = Save("SGTA50000");
        var service = new BackupService(Path.Combine(_folder, "backups"));
        var backup = service.Backup(source)!;
        var original = File.ReadAllBytes(source);
        Assert.Throws<SaveFormatException>(() => service.Restore(backup, backup));
        Assert.Throws<SaveFormatException>(() => service.Restore(source, source));
        File.WriteAllText(backup, "broken");
        Assert.Throws<SaveFormatException>(() => service.Restore(backup, source));
        Assert.Equal(original, File.ReadAllBytes(source));
        Assert.Single(service.Entries());
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);
}
