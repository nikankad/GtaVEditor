using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SaveEditor.Games.GtaV;

// Read-only research, pinned to the inspected build-2628 decompiler layout.
// Never executes the downloaded source, modifies a save, or guesses unsupported hashes.
if (args.Length < 3)
{
    Console.Error.WriteLine("Usage: StoryResearch <initial.c> <key-file> <save> [save ...]");
    return 2;
}
var source = File.ReadAllText(args[0]);
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string digest = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(args[0])));
const string expectedDigest = "C151D005FA9CDBB3789D82D645F86BF19C2B29FB6E0A97DABF6CDAD74B6AEA8F";
if (digest != expectedDigest) throw new InvalidDataException("Unsupported initial.c revision; review the extractor before using a different source.");
Console.WriteLine($"Source SHA256: {digest}");
var functions = Regex.Matches(source, @"(?m)^(?:void|int|bool) func_(\d+)\([^\r\n]*\)\r?\n\{.*?^\}", RegexOptions.Singleline | RegexOptions.Multiline)
    .ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Value);
var builders = Regex.Matches(functions[85], @"case (\d+):\s*func_(\d+)\(\);").ToArray();
if (builders.Length != 32) throw new InvalidDataException("Expected 32 strand builders; unsupported source.");
var commands = new List<Command>();
var labels = new HashSet<string>();
foreach (var builder in builders)
{
    int strand = int.Parse(builder.Groups[1].Value);
    var body = functions[int.Parse(builder.Groups[2].Value)];
    if (body.Contains("while (") || body.Contains("if ("))
        throw new InvalidDataException("Conditional strand builder requires manual review.");
    int start = commands.Count;
    foreach (Match call in Regex.Matches(body, @"(?m)^\s*func_(\d+)\((.*)\);"))
    {
        int id = int.Parse(call.Groups[1].Value);
        if (id is 132 or 87) continue; // begin/end strand, no command emitted
        string helper = functions[id];
        var op = Regex.Match(helper, @"f_6825\[Global_78127.f_12591 /\*3\*/\] = (-?\d+);");
        if (!op.Success) throw new InvalidDataException($"Unhandled builder helper func_{id}.");
        var kindMatch = Regex.Match(helper, @"iVar0 = (\d+);");
        int kind = int.Parse(kindMatch.Groups[1].Value);
        var parameters = call.Groups[2].Value.Split(", ");
        // func_128 emits a label only once across the entire command list.
        if (id == 128 && !labels.Add(parameters[0])) continue;
        var c = new Command(commands.Count, strand, id, int.Parse(op.Groups[1].Value), kind, parameters, null);
        string prefix = c.Opcode.ToString(CultureInfo.InvariantCulture) + strand;
        if (kind == 12) c = c with { Hash = GtaHash.Joaat(prefix) };
        else if (kind == 0)
        {
            string expr = Regex.Match(helper, @"f_6825\[Global_78127.f_12591 /\*3\*/\].f_2 = ([^;]+);").Groups[1].Value;
            if (id is 101 or 104)
                c = c with { Hash = GtaHash.Joaat(prefix + (int.Parse(parameters[0]) + int.Parse(parameters[1]) * 1000000)) };
            else if (Regex.IsMatch(expr, @"^-?\d+$")) c = c with { Hash = GtaHash.Joaat(prefix + expr) };
            else if (Regex.Match(expr, @"^[ib]Param(\d+)$") is { Success: true } param &&
                int.TryParse(parameters[int.Parse(param.Groups[1].Value)], out int value))
                c = c with { Hash = GtaHash.Joaat(prefix + value.ToString(CultureInfo.InvariantCulture)) };
        }
        else if (id is 127 or 200 or 128)
        {
            int[] p = parameters.Select(x => int.Parse(x, CultureInfo.InvariantCulture)).ToArray();
            int last = id == 128 ? -1 : id == 127 ? p[4] : p[3];
            if (id != 128 && last == -1) last = p[1];
            int third = id == 128 ? -1 : p[2];
            int a = unchecked((int)GtaHash.Joaat($"{p[0]}{p[1]}"));
            int b = unchecked((int)GtaHash.Joaat($"{third}{last}"));
            c = c with { Hash = GtaHash.Joaat(prefix + a.ToString(CultureInfo.InvariantCulture) + b.ToString(CultureInfo.InvariantCulture)) };
        }
        commands.Add(c);
    }
    Console.WriteLine($"Strand {strand,2}: {start,4}..{commands.Count - 1,4}");
}
Console.WriteLine($"Commands: {commands.Count}; supported hashes: {commands.Count(c => c.Hash != null)}");
if (commands.Count != 1882) throw new InvalidDataException("Command extraction count differs from the inspected source.");
var key = File.ReadAllBytes(args[1]);
int failures = 0;
foreach (string path in args.Skip(2))
{
    var save = GtaVSaveDocument.Load(path, key);
    if (!save.Integrity.IsValid) throw new InvalidDataException(save.Integrity.Summary);
    var flow = save.Pso.Root;
    foreach (uint field in new uint[] { 0x4B041FD7, 0x77714C97, 0x412DDFE6, 0x2FC0B451, 0xDEE6F89E })
        flow = flow.Children.Single(n => n.NameHash == field);
    if (flow.Children.Count != 32) throw new InvalidDataException("Unsupported strand count.");
    int matched = 0, relocated = 0, unknown = 0;
    Console.WriteLine($"\n{path}: {save.Title}");
    for (int i = 0; i < 32; i++)
    {
        var fields = flow.Children[i].Children;
        int position = int.Parse(fields[1].ValueText!, CultureInfo.InvariantCulture);
        uint hash = unchecked((uint)int.Parse(fields[2].ValueText!, CultureInfo.InvariantCulture));
        var command = commands.FirstOrDefault(c => c.Strand == i && c.Position == position);
        if (command?.Hash == hash) { matched++; continue; }
        var equivalents = commands.Where(c => c.Strand == i && c.Hash == hash).ToArray();
        if (equivalents.Length == 1)
        {
            relocated++;
            Console.WriteLine($"  Strand {i}: hash matches command {equivalents[0].Position} (saved position {position})");
        }
        else
        {
            unknown++;
            Console.WriteLine($"  Strand {i}: unresolved position {position}, hash {hash:X8}, helper {command?.Helper}, kind {command?.Kind}");
        }
    }
    Console.WriteLine($"Hash matches: {matched} at saved positions, {relocated} at different positions; unresolved: {unknown}.");
    failures += unknown;
}
// Unresolved means incomplete research, not proof of an invalid save.
return failures == 0 ? 0 : 3;

record Command(int Position, int Strand, int Helper, int Opcode, int Kind, string[] Parameters, uint? Hash);
