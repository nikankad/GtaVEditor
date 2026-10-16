using System.Collections;
using System.IO;
using System.Reflection;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using SaveEditor.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var application = new Application();
        try
        {
            var assembly = typeof(GamePictures).Assembly;
            using var aliasStream = assembly.GetManifestResourceStream("GtaVSaveEditor.PictureAliases.txt")!;
            using var aliasReader = new StreamReader(aliasStream);
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            while (aliasReader.ReadLine() is { } line)
            {
                if (line.Length == 0 || line[0] == '#') continue;
                var fields = line.Split('|');
                Require(fields.Length == 2, "Invalid alias entry");
                aliases.Add(fields[0], fields[1]);
            }

            const string root = "assets/game/";
            using var stream = assembly.GetManifestResourceStream("GtaVSaveEditor.g.resources")!;
            using var reader = new ResourceReader(stream);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            var entries = reader.GetEnumerator();
            while (entries.MoveNext())
            {
                if (entries.Key is not string name || !name.StartsWith(root, StringComparison.Ordinal)) continue;
                using var picture = (Stream)entries.Value!;
                hashes.Add(name[root.Length..], Convert.ToHexStringLower(SHA256.HashData(picture)));
            }
            foreach (var (alias, target) in aliases)
            {
                Require(!aliases.ContainsKey(target), $"Alias chain: {alias}");
                Require(hashes.ContainsKey(target), $"Missing target: {target}");
                Require(!hashes.ContainsKey(alias), $"Duplicate still bundled: {alias}");
            }

            var get = typeof(GamePictures).GetMethod("Get", BindingFlags.Static | BindingFlags.NonPublic)!;
            BitmapSource? Picture(string path, int width = 0) => (BitmapSource?)get.Invoke(null, [path, width]);
            var beforeIndex = GC.GetTotalAllocatedBytes(true);
            Require(GamePictures.Character("Michael") is BitmapSource, "Character portrait is missing");
            Console.WriteLine($"Picture index and first portrait allocated {GC.GetTotalAllocatedBytes(true) - beforeIndex:N0} managed bytes.");

            foreach (var name in hashes.Keys)
            {
                var bitmap = Picture(name);
                Require(bitmap is { IsFrozen: true, PixelWidth: > 0, PixelHeight: > 0 }, $"Could not decode {name}");
            }
            foreach (var (alias, target) in aliases)
            {
                var canonical = Picture(target);
                Require(ReferenceEquals(canonical, Picture(alias)), $"Alias does not share its image: {alias}");
            }

            // Optional migration check: every old lookup must return byte-identical artwork.
            if (args.Length > 0)
            {
                var baseline = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(args[0]))!;
                Require(baseline.Count == hashes.Count + aliases.Count, "Picture lookup count changed");
                foreach (var (name, hash) in baseline)
                    Require(hashes.GetValueOrDefault(aliases.GetValueOrDefault(name) ?? name) == hash, $"Artwork changed: {name}");
                Console.WriteLine($"PASS: all {baseline.Count} original picture lookups are byte-identical.");
            }

            Require(GamePictures.VehicleThumb(0xB779A091) is BitmapSource { PixelWidth: 200 }, "Vehicle thumbnails lost their decode size");
            Require(GamePictures.Checklist("M_AH3A") != null, "Checklist sibling fallback is missing");
            Require(GamePictures.Mission("AH3a") != null && GamePictures.Stranger(4) != null, "Mission artwork is missing");
            Require(GamePictures.Person("Rampages") != null && GamePictures.Rim(2, "Classic Rod") != null, "Shared portraits or rims are missing");
            Require(GamePictures.WeaponPart("WEAPON_CARBINERIFLE", 6) != null, "Shared attachment artwork is missing");
            var cache = (IDictionary)typeof(GamePictures).GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            var count = cache.Count;
            for (int i = 0; i < 1000; i++) Require(Picture($"missing/{i}.png") == null, "Unknown pictures must return null");
            Require(cache.Count == count, "Missing-picture lookups grow the image cache");
            var discarded = TemporaryPicture(get);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            Require(!discarded.IsAlive, "The image cache retains a picture no control uses");
            Require(GamePictures.Vehicle(0xB779A091) is BitmapSource, "A collected picture cannot be reloaded");
            Console.WriteLine($"PASS: {hashes.Count} unique pictures decode, {aliases.Count} aliases share images, thumbnails and fallback lookups work.");
            Console.WriteLine("PASS: unused pictures can be collected and reloaded.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally { application.Shutdown(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference TemporaryPicture(MethodInfo get) =>
        new(get.Invoke(null, ["vehicles/b779a091.jpg", 0]));
}
