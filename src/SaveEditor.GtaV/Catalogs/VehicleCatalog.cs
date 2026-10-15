namespace SaveEditor.GtaV;

/// <summary>A vehicle the editor can put in a garage: the game's model name and the name shown in game.</summary>
public sealed record VehicleModel(string Model, string Name, string Class)
{
    public uint Hash { get; } = GtaHash.Joaat(Model);
    public override string ToString() => Name;
}

/// <summary>Cars, motorcycles, aircraft and boats the editor can give.</summary>
public static class VehicleCatalog
{
    public static IReadOnlyList<VehicleModel> All { get; } = Build();

    private static readonly Dictionary<uint, VehicleModel> ByHash = All.ToDictionary(v => v.Hash);

    public static VehicleModel? Find(uint hash) => ByHash.GetValueOrDefault(hash);

    /// <summary>Display name for a model hash, or a neutral description for models the catalog doesn't list.</summary>
    public static string NameOf(uint hash) => Find(hash)?.Name ?? "Other vehicle";

    private static List<VehicleModel> Build()
    {
        var list = new List<VehicleModel>();
        void Add(string cls, params (string Model, string Name)[] items) => list.AddRange(items.Select(i => new VehicleModel(i.Model, i.Name, cls)));

        Add("Super",
            ("adder", "Truffade Adder"), ("autarch", "Överflöd Autarch"), ("banshee2", "Bravado Banshee 900R"), ("bullet", "Vapid Bullet"),
            ("cheetah", "Grotti Cheetah"), ("cyclone", "Coil Cyclone"), ("deveste", "Principe Deveste Eight"), ("emerus", "Progen Emerus"),
            ("entity2", "Överflöd Entity XXR"), ("entityxf", "Överflöd Entity XF"), ("fmj", "Vapid FMJ"), ("furia", "Grotti Furia"),
            ("gp1", "Progen GP1"), ("infernus", "Pegassi Infernus"), ("italigtb", "Progen Itali GTB"), ("krieger", "Benefactor Krieger"),
            ("le7b", "Annis RE-7B"), ("nero", "Truffade Nero"), ("osiris", "Pegassi Osiris"), ("penetrator", "Ocelot Penetrator"),
            ("pfister811", "Pfister 811"), ("prototipo", "Grotti X80 Proto"), ("reaper", "Pegassi Reaper"), ("s80", "Annis S80RR"),
            ("sc1", "Übermacht SC1"), ("sheava", "Emperor ETR1"), ("sultanrs", "Karin Sultan RS"), ("t20", "Progen T20"),
            ("taipan", "Cheval Taipan"), ("tempesta", "Pegassi Tempesta"), ("tezeract", "Pegassi Tezeract"), ("thrax", "Truffade Thrax"),
            ("turismor", "Grotti Turismo R"), ("tyrant", "Överflöd Tyrant"), ("tyrus", "Progen Tyrus"), ("vacca", "Pegassi Vacca"),
            ("vagner", "Dewbauchee Vagner"), ("visione", "Grotti Visione"), ("voltic", "Coil Voltic"), ("xa21", "Ocelot XA-21"),
            ("zentorno", "Pegassi Zentorno"), ("zorrusso", "Pegassi Zorrusso"));
        Add("Sports",
            ("alpha", "Albany Alpha"), ("banshee", "Bravado Banshee"), ("buffalo", "Bravado Buffalo"), ("buffalo2", "Bravado Buffalo S"),
            ("calico", "Karin Calico GTF"), ("carbonizzare", "Grotti Carbonizzare"), ("comet2", "Pfister Comet"), ("comet3", "Pfister Comet Retro Custom"),
            ("comet6", "Pfister Comet S2"), ("coquette", "Invetero Coquette"), ("cypher", "Übermacht Cypher"), ("drafter", "Obey 8F Drafter"),
            ("elegy", "Annis Elegy Retro Custom"), ("elegy2", "Annis Elegy RH8"), ("euros", "Annis Euros"), ("feltzer2", "Benefactor Feltzer"),
            ("furoregt", "Lampadati Furore GT"), ("fusilade", "Schyster Fusilade"), ("futo", "Karin Futo"), ("growler", "Pfister Growler"),
            ("italigto", "Grotti Itali GTO"), ("jester", "Dinka Jester"), ("jester4", "Dinka Jester RR"), ("jugular", "Ocelot Jugular"),
            ("khamelion", "Hijak Khamelion"), ("kuruma", "Karin Kuruma"), ("lynx", "Ocelot Lynx"), ("massacro", "Dewbauchee Massacro"),
            ("neon", "Pfister Neon"), ("ninef", "Obey 9F"), ("ninef2", "Obey 9F Cabrio"), ("pariah", "Ocelot Pariah"),
            ("penumbra", "Maibatsu Penumbra"), ("rapidgt", "Dewbauchee Rapid GT"), ("raptor", "BF Raptor"), ("ruston", "Hijak Ruston"),
            ("schlagen", "Benefactor Schlagen GT"), ("schwarzer", "Benefactor Schwartzer"), ("seven70", "Dewbauchee Seven-70"),
            ("specter", "Dewbauchee Specter"), ("sugoi", "Dinka Sugoi"), ("sultan", "Karin Sultan"), ("sultan2", "Karin Sultan Classic"),
            ("surano", "Benefactor Surano"), ("vectre", "Emperor Vectre"), ("verlierer2", "Bravado Verlierer"), ("zr350", "Annis ZR350"));
        Add("Classics",
            ("btype", "Albany Roosevelt"), ("casco", "Lampadati Casco"), ("cheetah2", "Grotti Cheetah Classic"), ("coquette2", "Invetero Coquette Classic"),
            ("feltzer3", "Benefactor Stirling GT"), ("gt500", "Grotti GT500"), ("infernus2", "Pegassi Infernus Classic"), ("jb700", "Dewbauchee JB 700"),
            ("mamba", "Declasse Mamba"), ("manana", "Albany Manana"), ("michelli", "Lampadati Michelli GT"), ("monroe", "Pegassi Monroe"),
            ("peyote", "Vapid Peyote"), ("pigalle", "Lampadati Pigalle"), ("rapidgt3", "Dewbauchee Rapid GT Classic"), ("savestra", "Annis Savestra"),
            ("stinger", "Grotti Stinger"), ("stingergt", "Grotti Stinger GT"), ("swinger", "Ocelot Swinger"), ("torero", "Pegassi Torero"),
            ("tornado", "Declasse Tornado"), ("turismo2", "Grotti Turismo Classic"), ("viseris", "Lampadati Viseris"), ("ztype", "Truffade Z-Type"));
        Add("Muscle",
            ("blade", "Vapid Blade"), ("buccaneer", "Albany Buccaneer"), ("chino", "Vapid Chino"), ("coquette3", "Invetero Coquette BlackFin"),
            ("dominator", "Vapid Dominator"), ("dukes", "Imponte Dukes"), ("gauntlet", "Bravado Gauntlet"), ("hotknife", "Vapid Hotknife"),
            ("phoenix", "Imponte Phoenix"), ("picador", "Cheval Picador"), ("ruiner", "Imponte Ruiner"), ("sabregt", "Declasse Sabre Turbo"),
            ("stalion", "Declasse Stallion"), ("vigero", "Declasse Vigero"), ("virgo", "Albany Virgo"), ("voodoo2", "Declasse Voodoo"));
        Add("Sedans and coupes",
            ("cognoscenti", "Enus Cognoscenti"), ("exemplar", "Dewbauchee Exemplar"), ("f620", "Ocelot F620"), ("felon", "Lampadati Felon"),
            ("oracle", "Übermacht Oracle"), ("schafter2", "Benefactor Schafter"), ("sentinel", "Übermacht Sentinel XS"), ("stretch", "Dundreary Stretch"),
            ("superd", "Enus Super Diamond"), ("tailgater", "Obey Tailgater"), ("washington", "Albany Washington"), ("zion", "Übermacht Zion"));
        Add("SUVs and off-road",
            ("baller2", "Gallivanter Baller"), ("bfinjection", "BF Injection"), ("bifta", "BF Bifta"), ("bodhi2", "Canis Bodhi"),
            ("brawler", "Coil Brawler"), ("cavalcade", "Albany Cavalcade"), ("contender", "Vapid Contender"), ("dubsta", "Benefactor Dubsta"),
            ("dune", "BF Dune Buggy"), ("granger", "Declasse Granger"), ("guardian", "Vapid Guardian"), ("huntley", "Enus Huntley S"),
            ("kalahari", "Canis Kalahari"), ("kamacho", "Canis Kamacho"), ("landstalker", "Dundreary Landstalker"), ("marshall", "Cheval Marshall"),
            ("mesa", "Canis Mesa"), ("patriot", "Mammoth Patriot"), ("rebel2", "Karin Rebel"), ("rocoto", "Obey Rocoto"),
            ("sandking", "Vapid Sandking XL"), ("trophytruck", "Vapid Trophy Truck"), ("xls", "Benefactor XLS"));
        Add("Motorcycles",
            ("akuma", "Dinka Akuma"), ("bagger", "Western Bagger"), ("bati", "Pegassi Bati 801"), ("bati2", "Pegassi Bati 801RR"),
            ("blazer", "Nagasaki Blazer"), ("carbonrs", "Nagasaki Carbon RS"), ("daemon", "Western Daemon"), ("double", "Dinka Double-T"),
            ("enduro", "Dinka Enduro"), ("faggio2", "Pegassi Faggio"), ("hakuchou", "Shitzu Hakuchou"), ("hexer", "LCC Hexer"),
            ("innovation", "LCC Innovation"), ("lectro", "Principe Lectro"), ("nemesis", "Principe Nemesis"), ("pcj", "Shitzu PCJ 600"),
            ("ruffian", "Pegassi Ruffian"), ("sanchez", "Maibatsu Sanchez"), ("shotaro", "Nagasaki Shotaro"), ("sovereign", "Western Sovereign"),
            ("thrust", "Dinka Thrust"), ("vader", "Shitzu Vader"), ("vindicator", "Dinka Vindicator"));
        Add(Planes,
            ("besra", "Western Besra"), ("cuban800", "Cuban 800"), ("dodo", "Mammoth Dodo"), ("duster", "Western Duster"),
            ("hydra", "Mammoth Hydra"), ("lazer", "P-996 Lazer"), ("luxor", "Buckingham Luxor"), ("luxor2", "Buckingham Luxor Deluxe"),
            ("mammatus", "JoBuilt Mammatus"), ("miljet", "Buckingham Miljet"), ("nimbus", "Buckingham Nimbus"), ("shamal", "Buckingham Shamal"),
            ("stunt", "Mallard"), ("titan", "Titan"), ("velum", "JoBuilt Velum"), ("velum2", "JoBuilt Velum 5-Seater"),
            ("vestra", "Buckingham Vestra"));
        Add(Helicopters,
            ("annihilator", "Western Annihilator"), ("buzzard", "Nagasaki Buzzard Attack Chopper"), ("buzzard2", "Nagasaki Buzzard"),
            ("cargobob", "Cargobob"), ("frogger", "Maibatsu Frogger"), ("frogger2", "Maibatsu Frogger (TPI)"), ("maverick", "Western Maverick"),
            ("savage", "Savage"), ("supervolito", "Buckingham SuperVolito"), ("supervolito2", "Buckingham SuperVolito Carbon"),
            ("swift", "Buckingham Swift"), ("swift2", "Buckingham Swift Deluxe"), ("valkyrie", "Valkyrie"), ("volatus", "Buckingham Volatus"));
        Add(Boats,
            ("dinghy", "Nagasaki Dinghy"), ("jetmax", "Shitzu Jetmax"), ("marquis", "Dinka Marquis"), ("seashark", "Speedophile Seashark"),
            ("speeder", "Pegassi Speeder"), ("squalo", "Shitzu Squalo"), ("submersible", "Submersible"), ("submersible2", "Kraken"),
            ("suntrap", "Shitzu Suntrap"), ("toro", "Lampadati Toro"), ("tropic", "Shitzu Tropic"));
        return list;
    }

    public const string Planes = "Planes", Helicopters = "Helicopters", Boats = "Boats";

    /// <summary>Cars and motorcycles (what fits in a garage).</summary>
    public static IReadOnlyList<VehicleModel> RoadVehicles { get; } = All.Where(v => v.Class is not (Planes or Helicopters or Boats)).ToList();

    public static IReadOnlyList<VehicleModel> OfClass(string cls) => All.Where(v => v.Class == cls).ToList();
}
