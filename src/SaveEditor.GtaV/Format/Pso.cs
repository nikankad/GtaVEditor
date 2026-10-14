using System.Buffers.Binary;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SaveEditor.GtaV;

/// <summary>Field types of Rockstar's PSO format, numbered as they appear in the PSCH schema.</summary>
public enum PsoType : byte
{
    Bool = 0x00, SByte = 0x01, UByte = 0x02, SShort = 0x03, UShort = 0x04, SInt = 0x05, UInt = 0x06, Float = 0x07,
    Float2 = 0x08, String = 0x09, Float3 = 0x0A, Float4 = 0x0B, Structure = 0x0C, Array = 0x0D, Enum = 0x0E,
    Flags = 0x0F, Map = 0x10,
}

/// <summary>
/// One schema field: 12 bytes in PSCH = name hash, type, subtype, offset (16 bits), reference.
/// Bits 6–7 of the subtype byte are bits 16–17 of the offset (needed for the 309 KB script-globals struct).
/// </summary>
public sealed record PsoField(uint NameHash, PsoType Type, byte Subtype, int Offset, uint Reference)
{
    /// <summary>Hash 0x100 marks an array element descriptor, not a real field.</summary>
    public bool IsElementDescriptor => NameHash == 0x100;
}

public sealed record PsoStruct(uint NameHash, int Length, IReadOnlyList<PsoField> Fields);

/// <summary>A PMAP entry: a typed region of the PSIN data block.</summary>
public sealed record PsoBlock(uint TypeHash, int Offset, int Length);

/// <summary>
/// The decrypted GTA V PC save body is a PSO document (same format as .ymt files): PSIN holds the data,
/// PMAP maps typed regions of it, PSCH describes every struct (field name hashes, types, offsets).
/// The reference editor navigates it by the JOAAT hash (exact case) of the game's script save variable names.
/// See docs/FINDINGS.md, "PSO".
/// </summary>
public sealed class PsoDocument
{
    private PsoDocument(byte[] data, Dictionary<uint, PsoStruct> structs, Dictionary<uint, Dictionary<int, uint>> enums,
        List<PsoBlock> blocks, int rootBlock)
    {
        Data = data;
        Structs = structs;
        Enums = enums;
        Blocks = blocks;
        RootBlock = rootBlock;
    }

    /// <summary>The PSIN block's data; edits write straight into the save and are checksummed on save.</summary>
    public byte[] Data { get; }
    public IReadOnlyDictionary<uint, PsoStruct> Structs { get; }
    public IReadOnlyDictionary<uint, Dictionary<int, uint>> Enums { get; }
    public IReadOnlyList<PsoBlock> Blocks { get; }
    public int RootBlock { get; }

    private PsoNode? _root;

    public PsoNode Root => _root ??= PsoNode.ForStruct(this, null, PsoNames.Root, Blocks[RootBlock - 1].TypeHash,
        Blocks[RootBlock - 1].Offset, "");

    // Section offsets in PMAP and PSCH count from the start of the section, including its 8-byte header.
    private const int SectionHeader = SaveBlock.HeaderSize;

    public static PsoDocument Load(BlockContainer body)
    {
        byte[] Block(string id) => (body.Blocks.FirstOrDefault(b => b.Id == id)
            ?? throw new SaveFormatException($"Save has no {id} block.")).Data;
        var psin = Block("PSIN");
        var pmap = Block("PMAP");
        var psch = Block("PSCH");
        try
        {
            // PMAP: int32 root block id, uint16 count, uint16 0x7070, then 16-byte entries (type hash, offset, 0, length).
            int root = BinaryPrimitives.ReadInt32BigEndian(pmap);
            int count = BinaryPrimitives.ReadUInt16BigEndian(pmap.AsSpan(4));
            var blocks = new List<PsoBlock>(count);
            for (int i = 0; i < count; i++)
            {
                var e = pmap.AsSpan(8 + i * 16);
                var b = new PsoBlock(BinaryPrimitives.ReadUInt32BigEndian(e),
                    BinaryPrimitives.ReadInt32BigEndian(e[4..]) - SectionHeader, BinaryPrimitives.ReadInt32BigEndian(e[12..]));
                if (b.Offset < 0 || b.Length < 0 || b.Offset + b.Length > psin.Length)
                    throw new SaveFormatException($"PMAP entry {i + 1} points outside PSIN.");
                blocks.Add(b);
            }
            if (root < 1 || root > blocks.Count) throw new SaveFormatException("PMAP root block id is out of range.");

            // PSCH: int32 count, (name hash, offset) index, then each struct or enum description.
            int schemaCount = BinaryPrimitives.ReadInt32BigEndian(psch);
            var structs = new Dictionary<uint, PsoStruct>();
            var enums = new Dictionary<uint, Dictionary<int, uint>>();
            for (int i = 0; i < schemaCount; i++)
            {
                uint hash = BinaryPrimitives.ReadUInt32BigEndian(psch.AsSpan(4 + i * 8));
                int at = BinaryPrimitives.ReadInt32BigEndian(psch.AsSpan(8 + i * 8)) - SectionHeader;
                uint head = BinaryPrimitives.ReadUInt32BigEndian(psch.AsSpan(at));
                if (head >> 24 == 0)
                {
                    // Struct: [kind 0][unused][field count:16], length, unused, then 12-byte fields.
                    int fieldCount = (int)(head & 0xFFFF), length = BinaryPrimitives.ReadInt32BigEndian(psch.AsSpan(at + 4));
                    var fields = new List<PsoField>(fieldCount);
                    for (int f = 0; f < fieldCount; f++)
                    {
                        var e = psch.AsSpan(at + 12 + f * 12);
                        byte sub = e[5];
                        fields.Add(new PsoField(BinaryPrimitives.ReadUInt32BigEndian(e), (PsoType)e[4], (byte)(sub & 0x3F),
                            BinaryPrimitives.ReadUInt16BigEndian(e[6..]) | (sub >> 6) << 16, BinaryPrimitives.ReadUInt32BigEndian(e[8..])));
                    }
                    structs[hash] = new PsoStruct(hash, length, fields);
                }
                else
                {
                    // Enum: [kind 1][value count:24], then (name hash, value) pairs.
                    int valueCount = (int)(head & 0xFFFFFF);
                    var values = new Dictionary<int, uint>(valueCount);
                    for (int v = 0; v < valueCount; v++)
                        values[BinaryPrimitives.ReadInt32BigEndian(psch.AsSpan(at + 8 + v * 8))] =
                            BinaryPrimitives.ReadUInt32BigEndian(psch.AsSpan(at + 4 + v * 8));
                    enums[hash] = values;
                }
            }
            return new PsoDocument(psin, structs, enums, blocks, root);
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw new SaveFormatException("The save's PSO schema could not be read: " + ex.Message);
        }
    }

    /// <summary>
    /// Resolves an 8-byte PSO pointer: block id (1-based) in the low 12 bits, offset within the block in the next 20.
    /// Returns the absolute PSIN offset, or null for a null/invalid pointer.
    /// </summary>
    public int? ResolvePointer(int at)
    {
        uint p = BinaryPrimitives.ReadUInt32BigEndian(Data.AsSpan(at));
        int block = (int)(p & 0xFFF), offset = (int)(p >> 12);
        if (block < 1 || block > Blocks.Count) return null;
        var b = Blocks[block - 1];
        return offset <= b.Length ? b.Offset + offset : null;
    }

    /// <summary>Depth-first walk of every node (structs, arrays and values).</summary>
    public IEnumerable<PsoNode> Walk()
    {
        var stack = new Stack<PsoNode>();
        stack.Push(Root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            var children = node.Children;
            for (int i = children.Count - 1; i >= 0; i--) stack.Push(children[i]);
        }
    }

    /// <summary>First struct node whose field name is <paramref name="name"/> (exact case), or null.</summary>
    public PsoNode? FindStruct(string name)
    {
        uint hash = PsoNames.Hash(name);
        return Walk().FirstOrDefault(n => n.NameHash == hash && n.Field?.Type == PsoType.Structure);
    }
}

/// <summary>A struct, array or value inside the PSO tree, with the absolute PSIN offset of its data.</summary>
public sealed class PsoNode
{
    private const int MaxDepth = 64;

    private readonly PsoDocument _doc;
    private readonly PsoField? _element; // array element descriptor, for array nodes
    private IReadOnlyList<PsoNode>? _children;

    private PsoNode(PsoDocument doc, PsoField? field, uint nameHash, string name, int offset, string path, int depth,
        uint structHash = 0, PsoField? element = null, int count = 0, int start = 0)
    {
        _doc = doc;
        Field = field;
        NameHash = nameHash;
        Name = name;
        Offset = offset;
        Path = path;
        Depth = depth;
        StructHash = structHash;
        _element = element;
        Count = count;
        Start = start;
    }

    /// <summary>The schema field this node comes from; null for the root.</summary>
    public PsoField? Field { get; }
    public uint NameHash { get; }

    /// <summary>The variable name if known, otherwise the hash ("0x1234ABCD"); "[i]" for array elements.</summary>
    public string Name { get; }

    /// <summary>True when the name (or for array elements, the array's name) is known.</summary>
    public bool IsNamed => PsoNames.IsKnown(NameHash) || (Name.StartsWith('[') && Parent?.IsNamed == true);

    public int Offset { get; }
    public string Path { get; }
    public int Depth { get; }
    public PsoNode? Parent { get; private init; }

    /// <summary>Struct type hash for struct nodes (inline or pointed-to).</summary>
    public uint StructHash { get; }

    /// <summary>Element count for array nodes.</summary>
    public int Count { get; }

    private int Start { get; } // absolute offset of the first array element

    public PsoType? Type => Field?.Type;
    public bool IsStruct => Field == null || Field.Type == PsoType.Structure;
    public bool IsArray => Field?.Type == PsoType.Array;

    public bool IsValue => Field?.Type is PsoType.Bool or PsoType.SByte or PsoType.UByte or PsoType.SShort or PsoType.UShort
        or PsoType.SInt or PsoType.UInt or PsoType.Float or PsoType.Flags || IsPlainEnum;

    private bool IsPlainEnum => Field is { Type: PsoType.Enum, Subtype: 0 };

    public string TypeName => Field?.Type switch
    {
        null => "struct",
        PsoType.Bool => "bool",
        PsoType.SByte => "int8",
        PsoType.UByte => "uint8",
        PsoType.SShort => "int16",
        PsoType.UShort => "uint16",
        PsoType.SInt => "int32",
        PsoType.UInt => "uint32",
        PsoType.Float => "float",
        PsoType.Flags => "flags",
        PsoType.Enum => "enum",
        PsoType.Structure => "struct",
        PsoType.Array => $"array[{Count}]",
        { } t => t.ToString().ToLowerInvariant(),
    };

    internal static PsoNode ForStruct(PsoDocument doc, PsoField? field, string name, uint structHash, int offset, string path) =>
        new(doc, field, field?.NameHash ?? 0, name, offset, path, 0, structHash);

    public IReadOnlyList<PsoNode> Children => _children ??= BuildChildren();

    private IReadOnlyList<PsoNode> BuildChildren()
    {
        if (Depth >= MaxDepth) return [];
        if (IsStruct)
        {
            if (!_doc.Structs.TryGetValue(StructHash, out var s)) return [];
            var list = new List<PsoNode>();
            foreach (var f in s.Fields.Where(f => !f.IsElementDescriptor).OrderBy(f => f.Offset))
                if (Make(f, s.Fields, Offset + f.Offset, PsoNames.NameOf(f.NameHash), f.NameHash) is { } n) list.Add(n);
            return list;
        }
        if (IsArray && _element != null)
        {
            int size = ElementSize(_element);
            if (size <= 0) return [];
            var siblings = _doc.Structs.TryGetValue(Parent?.StructHash ?? 0, out var ps) ? ps.Fields : [];
            var list = new List<PsoNode>(Count);
            for (int i = 0; i < Count; i++)
                if (Make(_element, siblings, Start + i * size, $"[{i}]", NameHash) is { } n) list.Add(n);
            return list;
        }
        return [];
    }

    private PsoNode? Make(PsoField f, IReadOnlyList<PsoField> siblings, int at, string name, uint nameHash)
    {
        if (at < 0 || at >= _doc.Data.Length) return null;
        string path = Path.Length == 0 ? name : name.StartsWith('[') ? Path + name : Path + "/" + name;
        switch (f.Type)
        {
            case PsoType.Structure when f.Subtype == 0:
                return new PsoNode(_doc, f, nameHash, name, at, path, Depth + 1, f.Reference) { Parent = this };
            case PsoType.Structure:
            {
                // Pointer to a struct in another PMAP block; the block's type says which struct.
                var target = _doc.ResolvePointer(at);
                uint type = 0;
                if (target != null)
                {
                    uint p = BinaryPrimitives.ReadUInt32BigEndian(_doc.Data.AsSpan(at));
                    type = _doc.Blocks[(int)(p & 0xFFF) - 1].TypeHash;
                }
                return new PsoNode(_doc, f, nameHash, name, target ?? at, path, Depth + 1, target == null ? 0 : type) { Parent = this };
            }
            case PsoType.Array:
            {
                int index = (int)(f.Reference & 0xFFFF);
                if (index >= siblings.Count) return null;
                var element = siblings[index];
                int count, start;
                if (f.Subtype == 0)
                {
                    // atArray: 8-byte pointer, uint16 count, uint16 capacity.
                    count = BinaryPrimitives.ReadUInt16BigEndian(_doc.Data.AsSpan(at + 8));
                    start = _doc.ResolvePointer(at) ?? -1;
                    if (start < 0) count = 0;
                }
                else if (f.Subtype == 1)
                {
                    // Fixed-size array stored inline; the count is in the high 16 bits of the reference.
                    count = (int)(f.Reference >> 16);
                    start = at;
                }
                else { count = 0; start = at; } // other array kinds (bitsets) are shown but not expanded
                return new PsoNode(_doc, f, nameHash, name, at, path, Depth + 1, 0, element, count, start) { Parent = this };
            }
            default:
                return new PsoNode(_doc, f, nameHash, name, at, path, Depth + 1) { Parent = this };
        }
    }

    private int ElementSize(PsoField e) => e.Type switch
    {
        PsoType.Bool or PsoType.SByte or PsoType.UByte => 1,
        PsoType.SShort or PsoType.UShort => 2,
        PsoType.SInt or PsoType.UInt or PsoType.Float or PsoType.Flags => 4,
        PsoType.Enum when e.Subtype == 0 => 4,
        PsoType.Float2 => 8,
        PsoType.Float3 or PsoType.Float4 => 16,
        PsoType.Structure when e.Subtype == 0 => _doc.Structs.TryGetValue(e.Reference, out var s) ? s.Length : 0,
        PsoType.Structure => 8,
        PsoType.Array when e.Subtype == 0 => 16,
        _ => 0,
    };

    private Span<byte> Bytes(int n) => _doc.Data.AsSpan(Offset, n);

    /// <summary>The value as text (invariant culture), or null for structs/arrays/unsupported types.</summary>
    public string? ValueText => Field?.Type switch
    {
        PsoType.Bool => (Bytes(1)[0] != 0).ToString(),
        PsoType.SByte => ((sbyte)Bytes(1)[0]).ToString(CultureInfo.InvariantCulture),
        PsoType.UByte => Bytes(1)[0].ToString(CultureInfo.InvariantCulture),
        PsoType.SShort => BinaryPrimitives.ReadInt16BigEndian(Bytes(2)).ToString(CultureInfo.InvariantCulture),
        PsoType.UShort => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2)).ToString(CultureInfo.InvariantCulture),
        PsoType.SInt => BinaryPrimitives.ReadInt32BigEndian(Bytes(4)).ToString(CultureInfo.InvariantCulture),
        PsoType.UInt => BinaryPrimitives.ReadUInt32BigEndian(Bytes(4)).ToString(CultureInfo.InvariantCulture),
        PsoType.Float => BinaryPrimitives.ReadSingleBigEndian(Bytes(4)).ToString("R", CultureInfo.InvariantCulture),
        PsoType.Flags => "0x" + BinaryPrimitives.ReadUInt32BigEndian(Bytes(4)).ToString("X8"),
        PsoType.Enum when IsPlainEnum => BinaryPrimitives.ReadInt32BigEndian(Bytes(4)).ToString(CultureInfo.InvariantCulture),
        PsoType.Float2 or PsoType.Float3 or PsoType.Float4 => "(" + string.Join(", ",
            Enumerable.Range(0, Field.Type == PsoType.Float2 ? 2 : Field.Type == PsoType.Float3 ? 3 : 4)
                .Select(i => BinaryPrimitives.ReadSingleBigEndian(_doc.Data.AsSpan(Offset + 4 * i)).ToString("G6", CultureInfo.InvariantCulture))) + ")",
        _ => null,
    };

    public int GetInt32() => Field?.Type is PsoType.SInt or PsoType.UInt or PsoType.Enum
        ? BinaryPrimitives.ReadInt32BigEndian(Bytes(4))
        : throw new InvalidOperationException($"{Path} is not a 32-bit integer.");

    public void SetInt32(int value)
    {
        if (Field?.Type is not (PsoType.SInt or PsoType.UInt) && !IsPlainEnum)
            throw new InvalidOperationException($"{Path} is not a 32-bit integer.");
        BinaryPrimitives.WriteInt32BigEndian(Bytes(4), value);
    }

    /// <summary>Parses and writes a value of this node's type. Throws FormatException/OverflowException on bad input.</summary>
    public void SetValue(string text)
    {
        if (!IsValue) throw new InvalidOperationException($"{Path} is not an editable value.");
        text = text.Trim();
        var inv = CultureInfo.InvariantCulture;
        switch (Field!.Type)
        {
            case PsoType.Bool:
                Bytes(1)[0] = text.ToLowerInvariant() switch
                {
                    "true" or "1" or "yes" => 1,
                    "false" or "0" or "no" => 0,
                    _ => throw new FormatException("Enter true or false."),
                };
                break;
            case PsoType.SByte: Bytes(1)[0] = (byte)sbyte.Parse(text, inv); break;
            case PsoType.UByte: Bytes(1)[0] = byte.Parse(text, inv); break;
            case PsoType.SShort: BinaryPrimitives.WriteInt16BigEndian(Bytes(2), short.Parse(text, inv)); break;
            case PsoType.UShort: BinaryPrimitives.WriteUInt16BigEndian(Bytes(2), ushort.Parse(text, inv)); break;
            case PsoType.SInt or PsoType.Enum: BinaryPrimitives.WriteInt32BigEndian(Bytes(4), int.Parse(text, inv)); break;
            case PsoType.UInt: BinaryPrimitives.WriteUInt32BigEndian(Bytes(4), uint.Parse(text, inv)); break;
            case PsoType.Flags:
                BinaryPrimitives.WriteUInt32BigEndian(Bytes(4), text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                    ? uint.Parse(text[2..], NumberStyles.HexNumber, inv) : uint.Parse(text, inv));
                break;
            case PsoType.Float:
                var f = float.Parse(text, NumberStyles.Float, inv);
                if (!float.IsFinite(f)) throw new FormatException("Enter a finite number.");
                BinaryPrimitives.WriteSingleBigEndian(Bytes(4), f);
                break;
        }
    }

    public override string ToString() => Path;
}

/// <summary>Known script save variable names, matched to the hashes stored in the save (JOAAT of the exact-case name).</summary>
public static class PsoNames
{
    public const string Root = "(save)";

    private static readonly Dictionary<uint, string> Names = Load();

    public static uint Hash(string name) => GtaHash.Joaat(Encoding.ASCII.GetBytes(name));

    public static bool IsKnown(uint hash) => Names.ContainsKey(hash);

    public static string NameOf(uint hash) => Names.TryGetValue(hash, out var n) ? n : $"0x{hash:X8}";

    private static Dictionary<uint, string> Load()
    {
        var names = new Dictionary<uint, string>();
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.PsoNames.txt");
        if (stream == null) return names;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            line = line.Trim();
            if (line.Length > 0 && !line.StartsWith('#')) names.TryAdd(Hash(line), line);
        }
        return names;
    }
}
