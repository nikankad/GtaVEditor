using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Resources;
using System.Security.Cryptography;
using SaveEditor.Core;
using SaveEditor.Games.GtaV;

// Parse file bytes only: never load or execute the reference editor or resource objects.
try
{
    if (args.Length == 3 && args[0] == "verify")
    {
        Verify(File.ReadAllBytes(args[1]), args[2]);
        return 0;
    }
    bool install = args.Length == 3 && args[0] == "install";
    if (!install && !(args.Length == 4 && args[0] == "recover"))
    {
        Console.Error.WriteLine("Usage:\n  KeyRecovery recover <reference-editor.exe> <sample-save> <output.key>\n  KeyRecovery install <reference-editor.exe> <sample-save>\n  KeyRecovery verify <key-file> <sample-save>");
        return 2;
    }
    var key = ExtractReferenceKey(args[1]);
    Verify(key, args[2]);
    string keyPath = Path.GetFullPath(install ? KeyStore.Default.KeyPath : args[3]);
    foreach (string input in new[] { args[1], args[2] })
        if (string.Equals(keyPath, Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The output key path must differ from the input files.");
    if (File.Exists(keyPath) && !File.ReadAllBytes(keyPath).SequenceEqual(key))
        throw new InvalidDataException("A different key already exists at the output path. Choose another path.");
    var result = new KeyStore(keyPath).Import(key, args[2]);
    if (!result.IsValid) throw new InvalidDataException(result.Summary);
    Console.WriteLine($"Verified 32-byte key {(install ? "installed" : "saved")}: {keyPath}");
    return 0;
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or
    BadImageFormatException or CryptographicException or SaveFormatException or ArgumentException)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void Verify(byte[] key, string samplePath)
{
    var original = File.ReadAllBytes(samplePath);
    var doc = GtaVSaveDocument.Load(samplePath, key);
    if (!doc.Integrity.IsValid) throw new InvalidDataException(doc.Integrity.Summary);
    if (!original.SequenceEqual(doc.ToBytes()))
        throw new InvalidDataException("The save did not round-trip byte-for-byte; no key was stored.");
    Console.WriteLine($"Title: {doc.Title}");
    Console.WriteLine(doc.Integrity.Summary);
    Console.WriteLine($"Blocks: {string.Join(", ", doc.Body.Blocks)}");
    Console.WriteLine("Unedited decrypt/encrypt round trip: byte-for-byte identical.");
    Console.WriteLine($"Key SHA-256: {Convert.ToHexString(SHA256.HashData(key)).ToLowerInvariant()}");
}

static byte[] ExtractReferenceKey(string editorPath)
{
    // Coordinates are build-specific. Refuse other builds rather than guess.
    const string expectedSha256 = "76e67e346e0ac968b6c42e94bf65ee9ab78620d31fa548c51c22f2a98637f1d1";
    var editorBytes = File.ReadAllBytes(editorPath);
    if (!string.Equals(Convert.ToHexString(SHA256.HashData(editorBytes)), expectedSha256,
        StringComparison.OrdinalIgnoreCase))
        throw new InvalidDataException("Unsupported reference editor build. This extractor supports the inspected v5.9.0.0 binary only.");
    using var editor = new PEReader(new MemoryStream(editorBytes));
    var resourceAssembly = Unpack(ReadManifestResource(editor, "GTA V Save Editor&"));
    using var resources = new PEReader(new MemoryStream(resourceAssembly));
    var constants = Unpack(ReadManifestResource(resources, "GTA V Save Editor%"));
    using var resourceReader = new ResourceReader(new MemoryStream(
        ReadManifestResource(resources, "GTAV_Save_Editor.Resources.resources")));
    resourceReader.GetResourceData("X", out string resourceType, out byte[] encodedX);
    if (resourceType != "ResourceTypeCode.ByteArray" || encodedX.Length < 4 ||
        BitConverter.ToInt32(encodedX, 0) != encodedX.Length - 4)
        throw new InvalidDataException("Unexpected X resource format.");
    var x = encodedX.AsSpan(4).ToArray();

    // Config::ReduceDockingPane (0x06000FCF) initializes seven ints from FieldRVA
    // 0x04000A01 and constructs the key from two separate 16-byte slices of X.
    int Constant(int offset) => BitConverter.ToInt32(constants, offset);
    int count = Constant(144524);
    if (count != 7) throw new InvalidDataException("Unexpected key-offset array size.");
    var field = editor.GetMetadataReader().GetFieldDefinition(MetadataTokens.FieldDefinitionHandle(0xA01));
    int rva = field.GetRelativeVirtualAddress();
    if (rva == 0) throw new InvalidDataException("Key-offset field has no file-backed RVA.");
    var offsetBytes = editor.GetSectionData(rva).GetContent(0, count * sizeof(int)).ToArray();
    var offsets = Enumerable.Range(0, count).Select(i => BitConverter.ToInt32(offsetBytes, i * 4)).ToArray();
    byte[] first = [], second = [];
    for (int i = Constant(144532); i <= count - Constant(144528); i += Constant(144592))
    {
        if (i == 0)
        {
            offsets[i + Constant(144536)] = checked(offsets[i] * Constant(144540));
            first = Slice(offsets[i + Constant(144544)], Constant(144548), Constant(144552));
        }
        else if (i == Constant(144556))
        {
            offsets[i + Constant(144560)] = checked(offsets[i] * Constant(144564) - offsets[i] + i);
        }
        else if (i == Constant(144568))
        {
            offsets[i + Constant(144572)] = checked(offsets[i] * offsets[i + Constant(144576)]);
            second = Slice(offsets[i + Constant(144580)], Constant(144584), Constant(144588));
        }
    }
    var key = second.Concat(first).ToArray();
    if (key.Length != SaveCrypto.KeyLength) throw new InvalidDataException("The derived key is not 32 bytes.");
    return key;

    byte[] Slice(int offset, int length, int preserveOrder)
    {
        var bytes = x.AsSpan(offset, length).ToArray();
        if (preserveOrder == 0) Array.Reverse(bytes);
        return bytes;
    }
}

static byte[] ReadManifestResource(PEReader pe, string name)
{
    var md = pe.GetMetadataReader();
    var handle = md.ManifestResources.Single(h => md.GetString(md.GetManifestResource(h).Name) == name);
    var resource = md.GetManifestResource(handle);
    if (!resource.Implementation.IsNil) throw new InvalidDataException("Expected an embedded resource.");
    var section = pe.GetSectionData(pe.PEHeaders.CorHeader!.ResourcesDirectory.RelativeVirtualAddress);
    int offset = checked((int)resource.Offset);
    int size = BitConverter.ToInt32(section.GetContent(offset, 4).ToArray());
    return section.GetContent(offset + 4, size).ToArray();
}

static byte[] Unpack(byte[] data)
{
    int flags = (~data[3]) & 255;
    int pos = 4;
    if ((flags & 2) != 0)
    {
        using var des = DES.Create();
        des.IV = data.AsSpan(pos, 8).ToArray(); pos += 8;
        des.Key = data.AsSpan(pos, 8).ToArray(); pos += 8;
        using var transform = des.CreateDecryptor();
        data = transform.TransformFinalBlock(data, pos, data.Length - pos);
    }
    else data = data[pos..];
    if ((flags & 8) != 0)
    {
        using var inflate = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        inflate.CopyTo(output);
        data = output.ToArray();
    }
    return data;
}
