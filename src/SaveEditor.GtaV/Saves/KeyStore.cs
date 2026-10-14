
namespace SaveEditor.GtaV;

/// <summary>
/// Holds the user-supplied 32-byte PC save key. Rockstar's key is not distributed with
/// this project; the user imports it once and it is kept under %AppData%\SaveEditor\keys.
/// A candidate key is validated by decrypting a real save and checking its CHKS checksum.
/// </summary>
public sealed class KeyStore(string keyPath)
{
    public static KeyStore Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveEditor", "keys", "gtav-pc.key"));

    public string KeyPath { get; } = keyPath;

    public bool HasKey => File.Exists(KeyPath);

    public byte[] Load()
    {
        if (!HasKey)
            throw new SaveFormatException("No GTA V PC save key imported yet. Use 'Import key…' on the save list.");
        var key = File.ReadAllBytes(KeyPath);
        if (key.Length != SaveCrypto.KeyLength)
            throw new SaveFormatException($"Key file '{KeyPath}' must be exactly {SaveCrypto.KeyLength} bytes.");
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
            var sgta = SgtaFile.Read(File.ReadAllBytes(sampleSavePath));
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
