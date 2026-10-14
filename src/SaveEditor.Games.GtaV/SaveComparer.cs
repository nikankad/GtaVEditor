namespace SaveEditor.Games.GtaV;

public sealed record StatChange(uint Hash, string Name, string Character, string Label, StatKind Kind,
    string Before, string After, string Change, bool IsNamed);

public sealed record BlockChange(string Id, int SizeBefore, int SizeAfter, int ChangedBytes);

public sealed record SaveComparison(IReadOnlyList<StatChange> Stats, IReadOnlyList<BlockChange> Blocks);

/// <summary>
/// Compares two saves: every stat whose value differs (named where the name is known), plus how much each
/// data block changed. Make one change in-game between two back-to-back saves to see where it is stored.
/// </summary>
public static class SaveComparer
{
    public static SaveComparison Compare(GtaVSaveDocument before, GtaVSaveDocument after)
    {
        var stats = new List<StatChange>();
        foreach (var kind in new[] { StatKind.Int32, StatKind.Float, StatKind.UInt64 })
        {
            var a = before.Stats.Tables[kind].Entries().ToDictionary(e => e.Hash, e => e.Raw);
            var b = after.Stats.Tables[kind].Entries().ToDictionary(e => e.Hash, e => e.Raw);
            foreach (var hash in a.Keys.Union(b.Keys))
            {
                bool inA = a.TryGetValue(hash, out var x), inB = b.TryGetValue(hash, out var y);
                if (inA && inB && x == y) continue;
                var def = StatCatalog.Find(hash);
                var name = StatCatalog.NameOf(hash);
                stats.Add(new StatChange(hash, name ?? $"0x{hash:X8}", def?.Character ?? CharacterOf(name),
                    StatCatalog.LabelOf(hash) ?? "Unknown stat", kind,
                    inA ? Format(kind, x, def) : "(missing)", inB ? Format(kind, y, def) : "(missing)",
                    inA && inB ? Delta(kind, x, y, def) : "", name != null));
            }
        }

        var blocks = new List<BlockChange>();
        foreach (var blockA in before.Body.Blocks)
        {
            var blockB = after.Body.Blocks.FirstOrDefault(x => x.Id == blockA.Id);
            if (blockB == null) { blocks.Add(new(blockA.Id, blockA.Data.Length, 0, blockA.Data.Length)); continue; }
            int n = Math.Min(blockA.Data.Length, blockB.Data.Length), changed = Math.Abs(blockA.Data.Length - blockB.Data.Length);
            for (int i = 0; i < n; i++) if (blockA.Data[i] != blockB.Data[i]) changed++;
            blocks.Add(new(blockA.Id, blockA.Data.Length, blockB.Data.Length, changed));
        }

        return new(stats.OrderByDescending(s => s.IsNamed).ThenBy(s => s.Character).ThenBy(s => s.Label).ToList(), blocks);
    }

    private static string CharacterOf(string? name) => name?[..Math.Min(4, name.Length)] switch
    {
        "SP0_" => StatCatalog.Characters[0],
        "SP1_" => StatCatalog.Characters[1],
        "SP2_" => StatCatalog.Characters[2],
        _ => "",
    };

    private static string Format(StatKind kind, ulong raw, StatDefinition? def) => kind switch
    {
        StatKind.Int32 => (def?.Unit == "$" ? "$" : "") + ((int)(uint)raw).ToString("N0"),
        StatKind.Float => BitConverter.UInt32BitsToSingle((uint)raw).ToString("G6"),
        _ => def?.Unit == "ms" ? TimeSpan.FromMilliseconds(raw).ToString(@"d\d\ hh\h\ mm\m\ ss\s") : raw.ToString("N0"),
    };

    private static string Delta(StatKind kind, ulong x, ulong y, StatDefinition? def)
    {
        double d = kind switch
        {
            StatKind.Int32 => (long)(int)(uint)y - (int)(uint)x,
            StatKind.Float => BitConverter.UInt32BitsToSingle((uint)y) - BitConverter.UInt32BitsToSingle((uint)x),
            _ => (double)y - x,
        };
        if (def?.Unit == "ms") return (d >= 0 ? "+" : "-") + TimeSpan.FromMilliseconds(Math.Abs(d)).ToString(@"hh\:mm\:ss");
        var text = kind == StatKind.Float ? Math.Abs(d).ToString("G6") : Math.Abs(d).ToString("N0");
        return (d >= 0 ? "+" : "-") + (def?.Unit == "$" ? "$" : "") + text;
    }
}
