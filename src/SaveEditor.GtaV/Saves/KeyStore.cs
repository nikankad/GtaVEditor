using System.Reflection;

namespace SaveEditor.GtaV;

/// <summary>
/// Holds the 32-byte PC save key. Maintainer releases may embed it from the GTAV_SAVE_KEY
/// repository secret (written to the gitignored Data/gtav-pc.key just for the build).
/// Builds without it ask the user to import a key once; it is kept under %AppData%\SaveEditor\keys and takes precedence.
/// A candidate key is validated by decrypting a real save and checking its CHKS checksum.
/// </summary>
public sealed class KeyStore(string keyPath, byte[]? bundledKey = null)
{
    public static KeyStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveEditor", "keys", "gtav-pc.key"), BundledKey());

    public string KeyPath { get; } = keyPath;

    public bool HasKey => File.Exists(KeyPath) || bundledKey != null;

    public byte[] Load()
    {
        if (File.Exists(KeyPath))
        {
            var key = SgtaFile.ReadAllBytesLimited(KeyPath, SaveCrypto.KeyLength);
            if (key.Length != SaveCrypto.KeyLength)
                throw new SaveFormatException($"Key file '{KeyPath}' must be exactly {SaveCrypto.KeyLength} bytes.");
            return key;
        }
        return bundledKey ?? throw new SaveFormatException("No GTA V PC save key yet. Use 'Import key…' on the start page.");
    }

    /// <summary>The key embedded at build time, or null for builds made without one.</summary>
    private static byte[]? BundledKey()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.gtav-pc.key");
        if (stream == null || stream.Length != SaveCrypto.KeyLength) return null;
        var key = new byte[SaveCrypto.KeyLength];
        stream.ReadExactly(key);
        return key;
    }

    /// <summary>Validates <paramref name="key"/> against <paramref name="sampleSavePath"/>, then stores it.</summary>
    public IntegrityReport Import(byte[] key, string sampleSavePath)
    {
        if (key.Length != SaveCrypto.KeyLength)
            return new(false, $"Key must be exactly {SaveCrypto.KeyLength} bytes (got {key.Length}).");

        IntegrityReport report;
        try
        {
            var sgta = SgtaFile.Read(SgtaFile.ReadAllBytesLimited(sampleSavePath));
            report = BlockContainer.Parse(SaveCrypto.Decrypt(sgta.EncryptedBody, key)).Verify();
        }
        catch (SaveFormatException ex)
        {
            return new(false, "Key did not decrypt the sample save: " + ex.Message);
        }
        if (!report.IsValid) return new(false, "Key decrypted the save but the checksum failed: " + report.Summary);

        Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
        File.WriteAllBytes(KeyPath, key);
        return new(true, "Key verified against the sample save and saved.");
    }
}
