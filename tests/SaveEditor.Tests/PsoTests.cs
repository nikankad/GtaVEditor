using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveEditor.GtaV;

namespace SaveEditor.Tests;

public class PsoTests
{
    private const uint RootType = 0xAAAA0001, WeaponsType = 0xAAAA0002, CharType = 0xAAAA0003, TableType = 0xAAAA0004;
    private const int FarOffset = 0x10008; // needs the high offset bits kept in the subtype byte
    private const int ArrayField = 0x2000, Slots = 40, PistolSlot = 8;
    private static readonly uint Pistol = GtaHash.Joaat("WEAPON_PISTOL");

    private sealed record F(uint Name, PsoType Type, byte Sub, int Offset, uint Ref = 0);

    /// <summary>
    /// Builds PSIN/PMAP/PSCH for a small PSO document laid out like a real save: a root struct with a few named
    /// script variables, a value past 64 KB, an atArray of bytes, and a WEAPONS struct (3 characters, each with one
    /// base-game slot table of 40 slots of <paramref name="slotInts"/> ints; only the pistol is owned).
    /// </summary>
    private static (byte[] Psin, byte[] Pmap, byte[] Psch) BuildPso(int slotInts = 5)
    {
        static byte[] Struct(int length, params F[] fields)
        {
            var b = new byte[12 + fields.Length * 12];
            BinaryPrimitives.WriteUInt32BigEndian(b, (uint)fields.Length);
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), length);
            for (int i = 0; i < fields.Length; i++)
            {
                var e = b.AsSpan(12 + i * 12);
                var f = fields[i];
                BinaryPrimitives.WriteUInt32BigEndian(e, f.Name);
                e[4] = (byte)f.Type;
                e[5] = (byte)(f.Sub | (f.Offset >> 16) << 6);
                BinaryPrimitives.WriteUInt16BigEndian(e[6..], (ushort)f.Offset);
                BinaryPrimitives.WriteUInt32BigEndian(e[8..], f.Ref);
            }
            return b;
        }

        // Weapon table: slot 0 = size, then the slots' ints at 8-byte spacing (like script globals). The weapon fields of
        // slots 0 and 1 carry the real games' names; the slot size is found from them.
        int ints = Slots * slotInts, tableLength = 8 + ints * 8;
        const int rootLength = FarOffset + 8;
        var tableFields = Enumerable.Range(0, ints).Select(i => new F(
            i == 0 ? 0x6DBFBC23 : i == slotInts ? 0x7866D171 : 0xBB000000 + (uint)i, PsoType.SInt, 0, 8 + i * 8)).ToArray();
        var schema = new (uint Hash, byte[] Data)[]
        {
            (RootType, Struct(rootLength,
                new F(PsoNames.Hash("iHairdoShopVisits"), PsoType.SInt, 0, 0x00),
                new F(PsoNames.Hash("EPSDESERT_DISTANCE_RUN"), PsoType.Float, 0, 0x08),
                new F(PsoNames.Hash("WEAPONS"), PsoType.Structure, 0, 0x10, WeaponsType),
                new F(0x100, PsoType.UByte, 0, 0),
                new F(PsoNames.Hash("HEIST_CHOICE_HELP_DISPLAYED_ARRAY"), PsoType.Array, 0, ArrayField, 3),
                new F(0xCC000001, PsoType.SInt, 0, FarOffset))),
            (WeaponsType, Struct(3 * tableLength,
                new F(0xDD000001, PsoType.Structure, 0, 0, CharType),
                new F(0xDD000002, PsoType.Structure, 0, tableLength, CharType),
                new F(0xDD000003, PsoType.Structure, 0, 2 * tableLength, CharType))),
            (CharType, Struct(tableLength, new F(0xEE000001, PsoType.Structure, 0, 0, TableType))),
            (TableType, Struct(tableLength, tableFields)),
        };
        var psch = new MemoryStream();
        var index = new byte[4 + schema.Length * 8];
        BinaryPrimitives.WriteInt32BigEndian(index, schema.Length);
        int at = 8 + index.Length; // offsets count from the section start, including its 8-byte header
        for (int i = 0; i < schema.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(4 + i * 8), schema[i].Hash);
            BinaryPrimitives.WriteInt32BigEndian(index.AsSpan(8 + i * 8), at);
            at += schema[i].Data.Length;
        }
        psch.Write(index);
        foreach (var s in schema) psch.Write(s.Data);

        // PSIN: 8 bytes padding, block 1 = root struct, block 2 = the byte array's 3 elements.
        int rootAt = 8, arrayAt = rootAt + rootLength;
        var psin = new byte[arrayAt + 3];
        var root = psin.AsSpan(rootAt);
        BinaryPrimitives.WriteInt32BigEndian(root, 18);
        BinaryPrimitives.WriteSingleBigEndian(root[0x08..], 2.5f);
        for (int c = 0; c < 3; c++)
        {
            var table = root[(0x10 + c * tableLength + 8)..];
            BinaryPrimitives.WriteUInt32BigEndian(table, WeaponCatalog.Unarmed);
            var pistol = table[(PistolSlot * slotInts * 8)..];
            BinaryPrimitives.WriteUInt32BigEndian(pistol, Pistol);
            BinaryPrimitives.WriteInt32BigEndian(pistol[8..], 60 + c);
        }
        BinaryPrimitives.WriteUInt32BigEndian(root[ArrayField..], 2); // pointer: block 2, offset 0
        BinaryPrimitives.WriteUInt16BigEndian(root[(ArrayField + 8)..], 3);
        BinaryPrimitives.WriteInt32BigEndian(root[FarOffset..], 0x1234);
        psin[arrayAt] = 7;
        psin[arrayAt + 2] = 9;

        var pmap = new byte[8 + 2 * 16];
        BinaryPrimitives.WriteInt32BigEndian(pmap, 1);
        BinaryPrimitives.WriteUInt16BigEndian(pmap.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt16BigEndian(pmap.AsSpan(6), 0x7070);
        foreach (var (i, type, offset, length) in new[] { (0, RootType, rootAt, rootLength), (1, 2u, arrayAt, 3) })
        {
            var e = pmap.AsSpan(8 + i * 16);
            BinaryPrimitives.WriteUInt32BigEndian(e, type);
            BinaryPrimitives.WriteInt32BigEndian(e[4..], offset + 8);
            BinaryPrimitives.WriteInt32BigEndian(e[12..], length);
        }
        return (psin, pmap, psch.ToArray());
    }

    private static (string Path, byte[] Key) WriteSave(int slotInts = 5)
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var (psin, pmap, psch) = BuildPso(slotInts);
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        File.WriteAllBytes(path, GtaVSaveTests.BuildSaveFile(key, "t",
            GtaVSaveTests.BuildBody(("PSIN", psin), ("PMAP", pmap), ("PSCH", psch))));
        return (path, key);
    }

    private static PsoNode Find(GtaVSaveDocument doc, string path) => doc.Pso.Walk().Single(n => n.Path == path);

    [Fact]
    public void ReadsNamedValuesArraysAndFarOffsets()
    {
        var (path, key) = WriteSave();
        var doc = GtaVSaveDocument.Load(path, key);

        Assert.Equal("18", Find(doc, "iHairdoShopVisits").ValueText);
        Assert.Equal("2.5", Find(doc, "EPSDESERT_DISTANCE_RUN").ValueText);
        Assert.Equal(["7", "0", "9"], Find(doc, "HEIST_CHOICE_HELP_DISPLAYED_ARRAY").Children.Select(c => c.ValueText));
        Assert.True(Find(doc, "HEIST_CHOICE_HELP_DISPLAYED_ARRAY[2]").IsNamed);
        var far = Find(doc, "0xCC000001");
        Assert.Equal(8 + FarOffset, far.Offset);
        Assert.Equal(0x1234, far.GetInt32());
        Assert.False(far.IsNamed);
    }

    [Fact]
    public void EditsValuesAndKeepsChecksumValid()
    {
        var (path, key) = WriteSave();
        var doc = GtaVSaveDocument.Load(path, key);
        Find(doc, "iHairdoShopVisits").SetValue("42");
        Find(doc, "EPSDESERT_DISTANCE_RUN").SetValue("7.25");
        Find(doc, "HEIST_CHOICE_HELP_DISPLAYED_ARRAY[1]").SetValue("200");
        Assert.Throws<OverflowException>(() => Find(doc, "HEIST_CHOICE_HELP_DISPLAYED_ARRAY[0]").SetValue("300"));
        Assert.Throws<FormatException>(() => Find(doc, "iHairdoShopVisits").SetValue("lots"));
        doc.Save(path);

        var reloaded = GtaVSaveDocument.Load(path, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal("42", Find(reloaded, "iHairdoShopVisits").ValueText);
        Assert.Equal("7.25", Find(reloaded, "EPSDESERT_DISTANCE_RUN").ValueText);
        Assert.Equal("200", Find(reloaded, "HEIST_CHOICE_HELP_DISPLAYED_ARRAY[1]").ValueText);
        Assert.Equal("7", Find(reloaded, "HEIST_CHOICE_HELP_DISPLAYED_ARRAY[0]").ValueText);
    }

    private static CharacterWeapon Weapon(GtaVSaveDocument doc, string character, string id) =>
        doc.Weapons.Single(w => w.Character == character && w.Weapon.Id == id);

    private static int[] RawSlot(GtaVSaveDocument doc, int character, int slot, int slotInts) =>
        doc.Pso.FindStruct("WEAPONS")!.Children[character].Children[0].Children
            .Skip(slot * slotInts).Take(slotInts).Select(n => n.GetInt32()).ToArray();

    [Theory]
    [InlineData(5)] // current saves: [weapon, ammo, attachments, tint, -1]
    [InlineData(3)] // older saves: [weapon, ammo, packed tint/attachments]
    public void WeaponsGiveRemoveAndEditAmmo(int slotInts)
    {
        var (path, key) = WriteSave(slotInts);
        var doc = GtaVSaveDocument.Load(path, key);

        // Only base-game weapons whose slot exists in the 40-slot table are listed; the DLC table is missing here.
        var expected = WeaponCatalog.All.Count(w => w.Table == 0 && w.Slot < Slots);
        Assert.All(StatCatalog.Characters, c => Assert.Equal(expected, doc.Weapons.Count(w => w.Character == c)));
        Assert.Equal(["Michael", "Franklin", "Trevor"], doc.Weapons.Where(w => w.Owned).Select(w => w.Character));
        Assert.All(doc.Weapons.Where(w => w.Owned), w => Assert.Equal("Pistol", w.Weapon.Label));
        Assert.Equal([60, 61, 62], doc.Weapons.Where(w => w.Owned).Select(w => w.Ammo));

        var pistol = Weapon(doc, "Franklin", "WEAPON_PISTOL");
        Assert.Throws<ArgumentOutOfRangeException>(() => pistol.Ammo = 10_000);
        pistol.Ammo = 250;

        var rpg = Weapon(doc, "Franklin", "WEAPON_RPG");
        Assert.False(rpg.Owned);
        Assert.Throws<InvalidOperationException>(() => rpg.Ammo = 5);
        rpg.Give();
        Assert.Equal(20, rpg.Ammo);
        Assert.Throws<ArgumentOutOfRangeException>(() => rpg.Ammo = 21);
        var knife = Weapon(doc, "Franklin", "WEAPON_KNIFE");
        knife.Give();
        Assert.Throws<ArgumentOutOfRangeException>(() => knife.Ammo = 1);

        Weapon(doc, "Trevor", "WEAPON_PISTOL").Remove();
        doc.Save(path);

        var reloaded = GtaVSaveDocument.Load(path, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal(250, Weapon(reloaded, "Franklin", "WEAPON_PISTOL").Ammo);
        Assert.True(Weapon(reloaded, "Franklin", "WEAPON_RPG").Owned);
        Assert.True(Weapon(reloaded, "Franklin", "WEAPON_KNIFE").Owned);
        Assert.False(Weapon(reloaded, "Michael", "WEAPON_RPG").Owned);
        Assert.False(Weapon(reloaded, "Trevor", "WEAPON_PISTOL").Owned);
        Assert.Equal(60, Weapon(reloaded, "Michael", "WEAPON_PISTOL").Ammo);

        int rpgHash = (int)GtaHash.Joaat("WEAPON_RPG");
        Assert.Equal(slotInts == 5 ? [rpgHash, 20, 0, 0, -1] : [rpgHash, 20, 0x100000],
            RawSlot(reloaded, 1, WeaponCatalog.All.Single(w => w.Id == "WEAPON_RPG").Slot, slotInts));
        Assert.All(RawSlot(reloaded, 2, PistolSlot, slotInts), v => Assert.Equal(0, v));
        Assert.Equal((int)WeaponCatalog.Unarmed, RawSlot(reloaded, 1, 0, slotInts)[0]); // unarmed slot untouched
    }

    [Fact]
    public void WeaponsInAnUnfamiliarTableLayoutStayEditableButNothingIsPlacedBlindly()
    {
        var (path, key) = WriteSave();
        var doc = GtaVSaveDocument.Load(path, key);
        // Michael's table holds the SMG where current saves keep the pistol: the fixed slot map doesn't fit that table.
        var smgHash = (int)GtaHash.Joaat("WEAPON_SMG");
        doc.Pso.FindStruct("WEAPONS")!.Children[0].Children[0].Children[PistolSlot * 5].SetInt32(smgHash);

        Assert.False(doc.Weapons.Complete);
        var michaels = doc.Weapons.Where(w => w.Character == "Michael").ToList();
        Assert.Equal(["WEAPON_SMG"], michaels.Select(w => w.Weapon.Id)); // only what was found; the rest can't be placed
        Assert.Contains(doc.Weapons, w => w.Character == "Franklin" && w.Weapon.Id == "WEAPON_RPG"); // other tables unaffected

        michaels[0].Remove();
        michaels[0].Give();
        Assert.Equal([smgHash, 9999, 0, 0, -1], RawSlot(doc, 0, PistolSlot, 5)); // back into the slot it came from
    }

    /// <summary>Real save checks; set GTAV_SAMPLE_SAVE and GTAV_KEY_FILE to enable.</summary>
    [Fact]
    public void RealSave_PsoTreeAndWeapons()
    {
        var path = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile)) return;
        var key = File.ReadAllBytes(keyFile);

        var doc = GtaVSaveDocument.Load(path, key);
        var hair = doc.Pso.Walk().Single(n => n.Name == "iHairdoShopVisits");
        Assert.True(hair.GetInt32() >= 0);
        Assert.Equal(45, doc.Pso.Walk().Single(n => n.Name == "iRCMissionsCompleted").GetInt32());
        // PSO stat arrays and the anchor-based stat tables must point at the same bytes.
        Assert.Contains(doc.Pso.Walk(), n => n.Path.StartsWith("Stats/") && n.Offset == doc.Stats.Tables[StatKind.Int32].Offset);
        Assert.Equal(3 * WeaponCatalog.All.Count, doc.Weapons.Count);
        Assert.True(Weapon(doc, "Trevor", "WEAPON_PISTOL").Owned);

        // Edit a copy, never the original.
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        Weapon(doc, "Michael", "WEAPON_PISTOL").Ammo = 123;
        var owned = doc.Weapons.Where(w => w.Character == "Franklin" && w.Owned).ToList();
        Assert.NotEmpty(owned);
        owned[0].Remove();
        owned[0].Give();
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal(123, Weapon(reloaded, "Michael", "WEAPON_PISTOL").Ammo);
        Assert.True(Weapon(reloaded, "Franklin", owned[0].Weapon.Id).Owned);
        Assert.Equal(new FileInfo(path).Length, new FileInfo(copy).Length);
    }
}
