using System.Globalization;
using System.Reflection;

namespace SaveEditor.GtaV;

/// <summary>A piece of clothing a story-mode shop sells, with the game's name for it.</summary>
/// <param name="Slot">The ped component (0–11), or 14 for a prop (hat, glasses…).</param>
/// <param name="Item">The item's number in the shop's list; also its bit in the wardrobe.</param>
/// <param name="Anchor">For a prop, where it's worn (0 head, 1 eyes, 2 ears…); -1 for clothes.</param>
public sealed record ClothingItem(int Character, int Slot, int Item, int Drawable, int Texture, int Anchor, string Name)
{
    public override string ToString() => Name;
}

/// <summary>The clothes the story-mode shops sell, read from the editor's embedded data file.</summary>
public static class ClothingItems
{
    public const int PropSlot = 14;

    private static readonly IReadOnlyList<ClothingItem> All = Load();

    /// <summary>A character's items for one clothing slot (or prop anchor), in shop order, each name given once.</summary>
    public static IReadOnlyList<ClothingItem> For(int character, int slot, int anchor = -1)
    {
        var items = All.Where(i => i.Character == character && i.Slot == slot && i.Anchor == anchor).ToList();
        // Colours of one design often share a name; number the repeats so each choice reads differently.
        return items.GroupBy(i => i.Name).SelectMany(g => g.Count() == 1 ? g.ToList()
                : g.Select((i, n) => n == 0 ? i : i with { Name = $"{i.Name} ({n + 1})" }))
            .OrderBy(i => i.Item).ToList();
    }

    private static List<ClothingItem> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.ClothingItems.txt")
                           ?? throw new InvalidOperationException("The clothing data is missing.");
        using var reader = new StreamReader(stream);
        var inv = CultureInfo.InvariantCulture;
        var list = new List<ClothingItem>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 3 || line[0] == '#') continue;
            var f = line.Split('|', 7);
            list.Add(new ClothingItem(int.Parse(f[0], inv), int.Parse(f[1], inv), int.Parse(f[2], inv), int.Parse(f[3], inv),
                int.Parse(f[4], inv), int.Parse(f[5], inv), f[6]));
        }
        return list;
    }
}

/// <summary>
/// One clothing slot of what a character is wearing now. The save keeps, per character, the model (drawable) and
/// colour (texture) of each ped component and prop in PP_INFO_STRUCT/VARIATIONS; TRACKED_COMPS keeps the shop item
/// number worn in each component. Wearing an item also puts it in the character's wardrobe.
/// </summary>
public sealed class OutfitSlot
{
    private readonly PsoNode _drawable, _texture;
    private readonly PsoNode? _tracked;
    private readonly IReadOnlyList<PsoNode> _owned;

    internal OutfitSlot(string label, int slot, int anchor, IReadOnlyList<ClothingItem> options, PsoNode drawable, PsoNode texture,
        PsoNode? tracked, IReadOnlyList<PsoNode> owned)
    {
        (Label, Slot, Anchor, Options) = (label, slot, anchor, options);
        (_drawable, _texture, _tracked, _owned) = (drawable, texture, tracked, owned);
    }

    public string Label { get; }
    public int Slot { get; }
    public int Anchor { get; }

    /// <summary>True for hats, glasses and earrings, which can be taken off.</summary>
    public bool IsProp => Slot == ClothingItems.PropSlot;

    /// <summary>The shop items the character can wear here.</summary>
    public IReadOnlyList<ClothingItem> Options { get; }

    /// <summary>True when nothing is worn here (props only).</summary>
    public bool IsEmpty => IsProp && _drawable.GetInt32() < 0;

    /// <summary>The shop item worn now, or null when it's nothing or something the shops don't sell.</summary>
    public ClothingItem? Current => Match(_drawable.GetInt32(), _texture.GetInt32());

    private ClothingItem? Match(int drawable, int texture) =>
        Options.FirstOrDefault(o => o.Drawable == drawable && o.Texture == texture);

    /// <summary>Puts an item on, or takes a prop off with null.</summary>
    public void Wear(ClothingItem? item)
    {
        if (item == null)
        {
            if (!IsProp) throw new InvalidOperationException($"The {Label.ToLowerInvariant()} can't be left empty.");
            _drawable.SetInt32(-1);
            _texture.SetInt32(-1);
            return;
        }
        if (!Options.Contains(item)) throw new ArgumentException($"{item.Name} isn't a {Label.ToLowerInvariant()} for this character.");
        _drawable.SetInt32(item.Drawable);
        _texture.SetInt32(item.Texture);
        _tracked?.SetInt32(item.Item);
        if (item.Item / 32 < _owned.Count)
            _owned[item.Item / 32].SetInt32(_owned[item.Item / 32].GetInt32() | (1 << (item.Item % 32)));
    }

    /// <summary>Every value this slot writes, for putting it back as it was.</summary>
    public int[] Snapshot() =>
        [_drawable.GetInt32(), _texture.GetInt32(), _tracked?.GetInt32() ?? 0, .. _owned.Select(o => o.GetInt32())];

    public void Restore(int[] snapshot)
    {
        _drawable.SetInt32(snapshot[0]);
        _texture.SetInt32(snapshot[1]);
        _tracked?.SetInt32(snapshot[2]);
        for (int i = 0; i < _owned.Count && 3 + i < snapshot.Length; i++) _owned[i].SetInt32(snapshot[3 + i]);
    }

    /// <summary>What a snapshot shows as worn, for the change review.</summary>
    public string NameIn(int[] snapshot) =>
        IsProp && snapshot[0] < 0 ? "Nothing" : Match(snapshot[0], snapshot[1])?.Name ?? "Something the shops don't sell";
}

/// <summary>What one character is wearing now, slot by slot.</summary>
public sealed class Outfit
{
    // The slots offered, with the ped component (or prop anchor) behind each. Hair and beard are left out: the save also
    // keeps STORED_HAIR/STORED_BEARD, whose meaning isn't certain yet.
    private static readonly (string Label, int Slot, int Anchor)[] Offered =
    [
        ("Top", 3, -1), ("Shirt or vest underneath", 11, -1), ("Trousers or shorts", 4, -1), ("Shoes", 6, -1),
        ("Tie, watch or extra", 8, -1), ("Mask", 9, -1), ("T-shirt print", 10, -1),
        ("Hat, helmet or mask", ClothingItems.PropSlot, 0), ("Glasses", ClothingItems.PropSlot, 1), ("Earrings", ClothingItems.PropSlot, 2),
    ];

    // The wardrobe group names (COMPONENTS_ARRAY/SP<c>/<prefix><n>) for each slot; item i is bit i % 32 of group i / 32.
    private static readonly Dictionary<int, string> GroupPrefix = new()
    {
        [0] = "HEAD", [1] = "BEARD", [2] = "HAIR", [3] = "TORSO", [4] = "LEGS", [5] = "HAND", [6] = "FEET", [7] = "TEETH",
        [8] = "SPEC", [9] = "SPEC2_", [10] = "DECL", [11] = "JBIB", [ClothingItems.PropSlot] = "PROP",
    };

    private Outfit(string character, IReadOnlyList<OutfitSlot> slots) => (Character, Slots) = (character, slots);

    public string Character { get; }

    /// <summary>The slots this character has shop items for.</summary>
    public IReadOnlyList<OutfitSlot> Slots { get; }

    public static IReadOnlyList<Outfit> Load(PsoDocument pso)
    {
        var data = CompletionOrder.ScriptData(pso);
        var info = data.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("PP_INFO_STRUCT"))
                   ?? throw new SaveFormatException("This save has no player outfit data.");
        var variations = Child(info, "VARIATIONS");
        var tracked = info.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("TRACKED_COMPS"));
        var wardrobe = data.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("COMPONENTS_ARRAY"));
        if (variations.Children.Count < StatCatalog.Characters.Length)
            throw new SaveFormatException("The outfit data in this save is not in the expected layout.");

        var list = new List<Outfit>();
        for (int c = 0; c < StatCatalog.Characters.Length; c++)
        {
            // VARIATIONS holds Michael, Franklin and Trevor in that order (each one's saved clothes are in their own shop lists).
            var v = variations.Children[c];
            PsoNode[] drawables = Values(v, "DRAWABLE_VARIATION", 12), textures = Values(v, "TEXTURE_VARIATION", 12),
                props = Values(v, "PROP_INDEX", 9), propTextures = Values(v, "PROP_TEXTURE", 9);
            var groups = wardrobe?.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash($"SP{c}"));
            var slots = new List<OutfitSlot>();
            foreach (var (label, slot, anchor) in Offered)
            {
                var options = ClothingItems.For(c, slot, anchor);
                if (options.Count == 0) continue;
                bool prop = slot == ClothingItems.PropSlot;
                var trackedNode = prop || tracked == null || slot >= tracked.Children.Count || tracked.Children[slot].Children.Count <= c
                    ? null : tracked.Children[slot].Children[c];
                slots.Add(new OutfitSlot(label, slot, anchor, options, prop ? props[anchor] : drawables[slot],
                    prop ? propTextures[anchor] : textures[slot], trackedNode, OwnedGroups(groups, GroupPrefix[slot])));
            }
            list.Add(new Outfit(StatCatalog.Characters[c], slots));
        }
        return list;
    }

    private static IReadOnlyList<PsoNode> OwnedGroups(PsoNode? character, string prefix)
    {
        var owned = new List<PsoNode>();
        for (int n = 0; character?.Children.FirstOrDefault(g => g.NameHash == PsoNames.Hash(prefix + n)) is { } group; n++)
        {
            if (group.Children.FirstOrDefault(x => x.NameHash == PsoNames.Hash("ACQUIRED") && x.Type == PsoType.SInt) is not { } acquired) break;
            owned.Add(acquired);
        }
        return owned;
    }

    private static PsoNode Child(PsoNode parent, string name) =>
        parent.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash(name))
        ?? throw new SaveFormatException("The outfit data in this save is not in the expected layout.");

    private static PsoNode[] Values(PsoNode parent, string name, int count)
    {
        var values = Child(parent, name).Children;
        if (values.Count < count || values.Take(count).Any(n => n.Type != PsoType.SInt))
            throw new SaveFormatException("The outfit data in this save is not in the expected layout.");
        return values.Take(count).ToArray();
    }
}
