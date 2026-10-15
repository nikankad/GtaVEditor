using System.Buffers.Binary;
using System.Text;

namespace SaveEditor.GtaV;

/// <summary>
/// Raw layout of a GTA V PC save file (SGTA5xxxx), observed in a real PC save:
/// <code>
/// 0x000  uint32 BE   version (1)
/// 0x004  UTF-16LE    title, zero-padded (0x100 bytes)
/// 0x104  4 bytes     unknown - preserved as-is
/// 0x108  ...         AES-256-ECB encrypted body (length is a multiple of 16)
/// </code>
/// </summary>
public sealed class SgtaFile
{
    public const int TitleOffset = 0x004;
    public const int TitleBytes = 0x100;
    public const int UnknownOffset = 0x104;
    public const int BodyOffset = 0x108;
    public const int MaxTitleChars = TitleBytes / 2 - 1;

    public uint Version { get; set; }
    public string Title { get; set; } = "";
    public byte[] Unknown104 { get; set; } = new byte[4];
    public byte[] EncryptedBody { get; set; } = [];

    public static SgtaFile Read(byte[] file)
    {
        if (file.Length < BodyOffset + 16)
            throw new SaveFormatException("File is too small to be a GTA V PC save.");
        if ((file.Length - BodyOffset) % 16 != 0)
            throw new SaveFormatException("Encrypted body is not a multiple of 16 bytes; not a GTA V PC save.");

        var titleSpan = file.AsSpan(TitleOffset, TitleBytes);
        var title = Encoding.Unicode.GetString(titleSpan);
        int nul = title.IndexOf('\0');
        return new SgtaFile
        {
            Version = BinaryPrimitives.ReadUInt32BigEndian(file),
            Title = nul >= 0 ? title[..nul] : title,
            Unknown104 = file[UnknownOffset..BodyOffset],
            EncryptedBody = file[BodyOffset..],
        };
    }

    public byte[] Write()
    {
        if (Title.Length > MaxTitleChars)
            throw new SaveFormatException($"Title is longer than {MaxTitleChars} characters.");
        var file = new byte[BodyOffset + EncryptedBody.Length];
        BinaryPrimitives.WriteUInt32BigEndian(file, Version);
        Encoding.Unicode.GetBytes(Title, file.AsSpan(TitleOffset, TitleBytes));
        Unknown104.CopyTo(file, UnknownOffset);
        EncryptedBody.CopyTo(file, BodyOffset);
        return file;
    }

    /// <summary>Reads only the plaintext title (no key needed); null if the file is not readable as a save.</summary>
    public static string? ReadTitle(string path)
    {
        if (!LooksLikeSgta(path)) return null;
        try
        {
            var head = new byte[UnknownOffset];
            using (var fs = File.OpenRead(path)) fs.ReadExactly(head);
            var title = Encoding.Unicode.GetString(head, TitleOffset, TitleBytes);
            int nul = title.IndexOf('\0');
            return nul >= 0 ? title[..nul] : title;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    /// <summary>Quick header sniff used for auto-detection (no key needed).</summary>
    public static bool LooksLikeSgta(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length < BodyOffset + 16 || (info.Length - BodyOffset) % 16 != 0) return false;
            Span<byte> head = stackalloc byte[4];
            using var fs = info.OpenRead();
            fs.ReadExactly(head);
            return BinaryPrimitives.ReadUInt32BigEndian(head) == 1;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
