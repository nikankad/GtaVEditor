using System.Security.Cryptography;

namespace SaveEditor.GtaV;

/// <summary>
/// Body encryption of GTA V PC saves: AES-256, ECB, zero padding
/// (GTAV_Class::Decrypt/Encrypt, tokens 0x0600000F / 0x06000011: KeySize=256, Mode=ECB(2), Padding=Zeros(3)).
/// Source builds import the key; maintainer releases may embed it (see <see cref="KeyStore"/>).
/// </summary>
public static class SaveCrypto
{
    public const int KeyLength = 32;

    public static byte[] Decrypt(ReadOnlySpan<byte> cipher, byte[] key)
    {
        using var aes = Create(key);
        return aes.DecryptEcb(cipher, PaddingMode.None);
    }

    /// <summary>Encrypts, zero-padding the plaintext up to the AES block size.</summary>
    public static byte[] Encrypt(ReadOnlySpan<byte> plain, byte[] key)
    {
        using var aes = Create(key);
        return aes.EncryptEcb(plain, PaddingMode.Zeros);
    }

    private static Aes Create(byte[] key)
    {
        if (key.Length != KeyLength)
            throw new ArgumentException($"GTA V save key must be {KeyLength} bytes, got {key.Length}.", nameof(key));
        var aes = Aes.Create();
        aes.Key = key;
        return aes;
    }
}
