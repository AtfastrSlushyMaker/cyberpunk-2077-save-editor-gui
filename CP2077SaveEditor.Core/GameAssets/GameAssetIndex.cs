using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using WolvenKit.Common.Services;
using WolvenKit.Core.Interfaces;
using WolvenKit.RED4.Archive;
using WolvenKit.RED4.Archive.CR2W;
using WolvenKit.RED4.Archive.IO;
using WolvenKit.RED4.TweakDB;
using WolvenKit.RED4.Types;
using EFileReadErrorCodes = WolvenKit.RED4.Archive.IO.EFileReadErrorCodes;

namespace CP2077SaveEditor.GameAssets;

/// <summary>Where an item's icon is: a texture inside a game archive, and the icon's rectangle in UV coordinates.</summary>
public sealed record IconRef(string Archive, ulong Texture, float Left, float Top, float Right, float Bottom);

/// <summary>
/// Real name, icon(s) and catalog details of one item record. Clothing has separate male/female icons. ItemType is the
/// TweakDB item type without its prefix ("Wea_Revolver", "Clo_Head"), EquipArea likewise ("ArmsCW"), Quality is
/// "Common" … "Legendary" (or "Random"), and Expansion marks Phantom Liberty content. Vehicles use ItemType for their
/// kind ("Car", "Bike") and Manufacturer for the brand.
/// </summary>
public sealed record ItemAsset(string? Name, IconRef? Icon, IconRef? IconFemale,
    string? ItemType = null, string? EquipArea = null, string? Quality = null, bool Iconic = false, bool Expansion = false,
    string? Manufacturer = null);

/// <summary>
/// Item names and icon locations read from the player's own game installation (tweakdb.bin + archives), cached on disk
/// per game build. Nothing from the game is redistributed with the editor.
/// </summary>
public sealed class GameAssetIndex
{
    public const int FormatVersion = 3;

    public static string CacheRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CP2077 Save Editor", "GameData");

    public int Version { get; set; } = FormatVersion;
    public string GameDir { get; set; } = "";
    public string BuildKey { get; set; } = "";
    public Dictionary<ulong, ItemAsset> Items { get; set; } = new();

    /// <summary>Player vehicle records ("Vehicle.v_…_player") found in the game database; their details are in Items.</summary>
    public List<string> VehicleRecords { get; set; } = new();

    public ItemAsset? Get(TweakDBID id) => Items.TryGetValue((ulong)id, out var a) ? a : null;

    /// <summary>Identifies the game build by tweakdb.bin size and timestamp, so updates rebuild the index.</summary>
    public static string BuildKeyOf(string gameDir)
    {
        var info = new FileInfo(GameInstall.TweakDbPath(gameDir));
        return $"{info.Length:X}-{info.LastWriteTimeUtc.Ticks:X}-v{FormatVersion}";
    }

    public static string CacheDir(string gameDir) => Path.Combine(CacheRoot, BuildKeyOf(gameDir));
    private static string IndexPath(string gameDir) => Path.Combine(CacheDir(gameDir), "items.json");

    /// <summary>The cached index for this game build, or null if it has not been built yet.</summary>
    public static GameAssetIndex? TryLoad(string gameDir)
    {
        try
        {
            var path = IndexPath(gameDir);
            if (!File.Exists(path))
            {
                return null;
            }
            var index = JsonSerializer.Deserialize<GameAssetIndex>(File.ReadAllText(path));
            return index is { Version: FormatVersion } ? index : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the index for the given item records (takes ~20 s and ~1 GB while running) and caches it.
    /// </summary>
    public static GameAssetIndex Build(string gameDir, IEnumerable<string> itemRecords, IProgress<string>? progress = null)
    {
        var watch = Stopwatch.StartNew();
        progress?.Report("Reading game database…");
        TweakDB db;
        using (var reader = new TweakDBReader(File.OpenRead(GameInstall.TweakDbPath(gameDir))))
        {
            if (reader.ReadFile(out var file) != WolvenKit.RED4.TweakDB.EFileReadErrorCodes.NoError || file == null)
            {
                throw new InvalidDataException("tweakdb.bin could not be read.");
            }
            db = file;
        }

        progress?.Report("Opening game archives…");
        var hashService = new HashService();
        var archives = new List<(string Path, Archive Archive)>();
        foreach (var path in Directory.GetFiles(GameInstall.ContentDir(gameDir), "*.archive"))
        {
            var name = System.IO.Path.GetFileName(path);
            if (name.StartsWith("audio_") || (name.StartsWith("lang_") && name != "lang_en_text.archive"))
            {
                continue;
            }
            if (new ArchiveReader().ReadArchive(path, hashService, out var archive) == EFileReadErrorCodes.NoError)
            {
                archives.Add((path, archive));
            }
        }

        progress?.Report("Reading item names…");
        var names = ReadOnscreens(archives);

        progress?.Report("Resolving item icons…");
        var atlasCache = new Dictionary<ulong, inkTextureAtlas?>();
        var index = new GameAssetIndex { GameDir = gameDir, BuildKey = BuildKeyOf(gameDir) };
        foreach (var record in itemRecords)
        {
            if (Flat(db, record + ".displayName") is not gamedataLocKeyWrapper key)
            {
                continue; // not an item in this game version (removed or never finished)
            }
            var name = names.TryGetValue((ulong)key, out var text) ? text : null;

            var male = ResolveIcon(db, record, "Male", archives, atlasCache);
            var female = ResolveIcon(db, record, "Female", archives, atlasCache);
            if (male == null && CraftedItem(db, record) is { } crafted)
            {
                // recipes show the icon of the item they craft
                male = ResolveIcon(db, crafted, "Male", archives, atlasCache);
                female = ResolveIcon(db, crafted, "Female", archives, atlasCache);
            }

            var tags = Flat(db, record + ".tags") is IRedArray tagArray
                ? tagArray.Cast<object>().OfType<CName>().Select(t => t.GetResolvedText() ?? "").ToList()
                : new List<string>();
            index.Items[(ulong)(TweakDBID)record] = new ItemAsset(name, male ?? female, female == male ? null : female,
                ItemType: RecordSuffix(Flat(db, record + ".itemType")),
                EquipArea: RecordSuffix(Flat(db, record + ".equipArea")),
                Quality: RecordSuffix(Flat(db, record + ".quality")),
                Iconic: tags.Contains("IconicWeapon") || tags.Contains("IconicItem"),
                Expansion: tags.Any(t => t.StartsWith("EP1", StringComparison.OrdinalIgnoreCase)) || Editing.SaveFlags.IsExpansionRecord(record));
        }

        progress?.Report("Reading vehicles…");
        var vehicleRecords = GameData.TweakDbStrings.GetAll().Select(kv => kv.Value)
            .Where(n => n != null && n.StartsWith("Vehicle.v_") && n.EndsWith("_player") && n.IndexOf('.', 8) < 0)
            .Distinct().ToList();
        foreach (var record in vehicleRecords)
        {
            if (Flat(db, record + ".displayName") is not gamedataLocKeyWrapper key)
            {
                continue;
            }
            var tags = Flat(db, record + ".tags") is IRedArray tagArray
                ? tagArray.Cast<object>().OfType<CName>().Select(t => t.GetResolvedText() ?? "").ToList()
                : new List<string>();
            index.Items[(ulong)(TweakDBID)record] = new ItemAsset(names.TryGetValue((ulong)key, out var text) ? text : null,
                ResolveIcon(db, record, "Male", archives, atlasCache), null,
                ItemType: RecordSuffix(Flat(db, record + ".type")),
                Expansion: tags.Any(t => t.StartsWith("EP1", StringComparison.OrdinalIgnoreCase)) || Editing.SaveFlags.IsExpansionRecord(record),
                Manufacturer: RecordSuffix(Flat(db, record + ".manufacturer")));
            index.VehicleRecords.Add(record);
        }

        Directory.CreateDirectory(CacheDir(gameDir));
        File.WriteAllText(IndexPath(gameDir), JsonSerializer.Serialize(index));
        progress?.Report($"Game data ready: {index.Items.Count:N0} items ({watch.Elapsed.TotalSeconds:0}s)");
        return index;
    }

    /// <summary>LocKey -> English text from every onscreens*.json in lang_en_text.archive.</summary>
    private static Dictionary<ulong, string> ReadOnscreens(List<(string Path, Archive Archive)> archives)
    {
        var result = new Dictionary<ulong, string>();
        foreach (var (_, archive) in archives.Where(a => System.IO.Path.GetFileName(a.Path) == "lang_en_text.archive"))
        {
            foreach (var entry in archive.Files.Values.Where(f => f.FileName?.Contains("onscreens", StringComparison.OrdinalIgnoreCase) == true && f.FileName.EndsWith(".json")))
            {
                if (Read(entry)?.RootChunk is not JsonResource { Root.Chunk: localizationPersistenceOnScreenEntries entries })
                {
                    continue;
                }
                foreach (var e in entries.Entries)
                {
                    var text = !string.IsNullOrEmpty(e.FemaleVariant) ? (string)e.FemaleVariant : (string)e.MaleVariant;
                    if (!string.IsNullOrEmpty(text))
                    {
                        result[e.PrimaryKey] = text;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>Applies the game's UIIcon naming rules (see class docs) and returns the icon's texture region.</summary>
    private static IconRef? ResolveIcon(TweakDB db, string record, string gender, List<(string Path, Archive Archive)> archives, Dictionary<ulong, inkTextureAtlas?> atlasCache)
    {
        string? S(string flat) => Flat(db, $"{record}.{flat}") switch
        {
            CName n => n.GetResolvedText(),
            CString s => (string)s,
            _ => null
        };

        var candidates = new List<string>();
        if (Flat(db, record + ".icon") is TweakDBID icon && (ulong)icon != (ulong)(TweakDBID)"UIIcon.ItemIcon" && (ulong)icon != 0)
        {
            if (RecordName(icon) is { } iconRecord)
            {
                candidates.Add(iconRecord);
            }
        }
        if (S("iconPath") is { Length: > 0 } iconPath)
        {
            candidates.Add("UIIcon." + iconPath);
        }

        var arn = S("appearanceResourceName");
        var an = S("appearanceName");
        if (!string.IsNullOrEmpty(arn) && arn != "None")
        {
            var prefix = $"UIIcon.weapon_{arn}_{an ?? "None"}__";
            if (Flat(db, record + ".visualTags") is IRedArray tags)
            {
                candidates.AddRange(tags.Cast<object>().OfType<CName>().Select(t => prefix + t.GetResolvedText()));
            }
            candidates.Add(prefix + "Default");
        }
        if (!string.IsNullOrEmpty(an) && an != "None" && S("entityName") is { Length: > 0 } entity)
        {
            candidates.Add($"UIIcon.clothing_{entity}_{an}_{gender}_");
            candidates.Add($"UIIcon.outfit_{entity}_{an}_{gender}_");
            // items with a camera appearance suffix have their icon under the third-person appearance
            candidates.Add($"UIIcon.clothing_{entity}_{an}_{gender}_TPP_");
            candidates.Add($"UIIcon.outfit_{entity}_{an}_{gender}_TPP_");
        }

        foreach (var candidate in candidates)
        {
            var atlasValue = Flat(db, candidate + ".atlasResourcePath");
            var partValue = Flat(db, candidate + ".atlasPartName");
            if (atlasValue is not IRedResourceAsyncReference atlasRef || partValue is not CName part)
            {
                continue;
            }

            var atlasPath = (ulong)atlasRef.DepotPath;
            if (!atlasCache.TryGetValue(atlasPath, out var atlas))
            {
                atlas = archives.Select(a => a.Archive.Files.TryGetValue(atlasRef.DepotPath, out var f) ? Read(f)?.RootChunk as inkTextureAtlas : null)
                    .FirstOrDefault(x => x != null);
                atlasCache[atlasPath] = atlas;
            }
            if (atlas == null)
            {
                continue;
            }

            var partName = part.GetResolvedText();
            foreach (var slot in atlas.Slots)
            {
                var mapper = slot.Parts.FirstOrDefault(p => p.PartName.GetResolvedText() == partName);
                if (mapper == null)
                {
                    continue;
                }
                var texture = slot.Texture.DepotPath;
                var archive = archives.FirstOrDefault(a => a.Archive.Files.ContainsKey(texture));
                if (archive.Archive == null)
                {
                    continue;
                }
                var r = mapper.ClippingRectInUVCoords;
                return new IconRef(System.IO.Path.GetFileName(archive.Path), (ulong)texture, r.Left, r.Top, r.Right, r.Bottom);
            }
        }
        return null;
    }

    /// <summary>Name of a TweakDB record id, from WolvenKit's string table (only loaded in the builder process).</summary>
    private static string? RecordName(TweakDBID id) => GameData.TweakDbStrings.GetString((ulong)id) is { Length: > 0 } s ? s : null;

    /// <summary>"Quality.Legendary" -> "Legendary"; null for an empty id.</summary>
    private static string? RecordSuffix(IRedType? value) =>
        value is TweakDBID id && (ulong)id != 0 && RecordName(id) is { } name ? name[(name.IndexOf('.') + 1)..] : null;

    /// <summary>The item record a recipe crafts (recipe.craftingResult -> result.item), or null.</summary>
    private static string? CraftedItem(TweakDB db, string record) =>
        Flat(db, record + ".craftingResult") is TweakDBID result && RecordName(result) is { } resultRecord
            && Flat(db, resultRecord + ".item") is TweakDBID item ? RecordName(item) : null;

    /// <summary>
    /// Flat lookup by id. TweakDB.GetFlatValue(string) scans all ~3M flats linearly; the pool lookup is a dictionary.
    /// </summary>
    private static IRedType? Flat(TweakDB db, string path) => db.Flats.GetValue((ulong)(TweakDBID)path);

    internal static CR2WFile? Read(IGameFile file)
    {
        using var ms = new MemoryStream();
        file.Extract(ms);
        ms.Position = 0;
        using var reader = new CR2WReader(ms);
        return reader.ReadFile(out var cr2w) == EFileReadErrorCodes.NoError ? cr2w : null;
    }
}
