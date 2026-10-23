using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace CP2077SaveEditor.Wpf.Services;

public enum SaveKind
{
    Manual,
    Auto,
    Quick,
    PointOfNoReturn,
    EndGame,
    Other
}

/// <summary>A save folder (sav.dat + metadata.9.json + screenshot.png) as shown in the save browser.</summary>
public sealed record SaveSlot
{
    public required string Folder { get; init; }
    public string FolderName => Path.GetFileName(Folder);
    public string SavePath => Path.Combine(Folder, "sav.dat");
    public string? ScreenshotPath { get; init; }

    public SaveKind Kind { get; init; }
    public int Level { get; init; }
    public int StreetCred { get; init; }
    public string LifePath { get; init; } = "";
    public string BodyGender { get; init; } = "";
    public string Difficulty { get; init; } = "";
    public string Quest { get; init; } = "";
    public string BuildPatch { get; init; } = "";
    public int GameVersion { get; init; }
    public int SaveVersion { get; init; }
    public TimeSpan PlayTime { get; init; }
    public DateTime SavedAt { get; init; }
    public bool IsModded { get; init; }

    /// <summary>Where the save was found: "Game saves", "My library" or a folder the user added.</summary>
    public string Source { get; init; } = "";

    /// <summary>True for saves in the game's own save folder (the ones the game loads).</summary>
    public bool IsGameSave { get; init; }

    /// <summary>The editor supports saves from patch 2.0 onwards.</summary>
    public bool IsSupported => GameVersion >= 2000;
}

public static class SaveLibrary
{
    public static string DefaultRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games", "CD Projekt Red", "Cyberpunk 2077");

    public static Task<List<SaveSlot>> ScanAsync(string root) => Task.Run(() =>
    {
        if (!Directory.Exists(root))
        {
            return new List<SaveSlot>();
        }

        return Directory.EnumerateDirectories(root)
            .Where(d => File.Exists(Path.Combine(d, "sav.dat")))
            .AsParallel()
            .Select(TryRead)
            .Where(s => s != null)
            .Select(s => s!)
            .OrderByDescending(s => s.SavedAt)
            .ToList();
    });

    /// <summary>The editor's own save library: saves kept outside the game's folder (imported or copied there).</summary>
    public static string LibraryRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CP2077 Save Editor", "Library");

    public const string GameSourceName = "Game saves";
    public const string LibrarySourceName = "My library";

    /// <summary>Scans the game folder, the library and every extra folder, tagging each save with where it was found.</summary>
    public static async Task<List<SaveSlot>> ScanAllAsync(string gameRoot, IEnumerable<string> extraFolders)
    {
        var sources = new List<(string Name, string Path, bool IsGame)> { (GameSourceName, gameRoot, true), (LibrarySourceName, LibraryRoot, false) };
        sources.AddRange(extraFolders.Where(Directory.Exists).Select(f => (SourceName(f), f, false)));

        var result = new List<SaveSlot>();
        foreach (var (name, path, isGame) in sources)
        {
            result.AddRange((await ScanAsync(path)).Select(s => s with { Source = name, IsGameSave = isGame }));
        }
        return result.OrderByDescending(s => s.SavedAt).ToList();
    }

    /// <summary>Chip label of an added folder: its folder name.</summary>
    public static string SourceName(string folder) => Path.GetFileName(folder.TrimEnd('\\', '/'));

    /// <summary>Copies a save folder into the library; returns the new folder.</summary>
    public static string CopyToLibrary(string folder)
    {
        Directory.CreateDirectory(LibraryRoot);
        var name = Path.GetFileName(folder.TrimEnd('\\', '/'));
        var target = Path.Combine(LibraryRoot, name);
        for (var i = 2; Directory.Exists(target); i++)
        {
            target = Path.Combine(LibraryRoot, $"{name} ({i})");
        }
        CopyFolder(folder, target);
        return target;
    }

    /// <summary>
    /// Copies (or moves) a save into the game's save folder as the next free "ManualSave-N", the naming the game's
    /// Load Game menu expects; returns the new folder.
    /// </summary>
    public static string CopyToGame(string folder, string gameRoot, bool move)
    {
        Directory.CreateDirectory(gameRoot);
        var n = 0;
        while (Directory.Exists(Path.Combine(gameRoot, $"ManualSave-{n}"))) n++;
        var target = Path.Combine(gameRoot, $"ManualSave-{n}");
        CopyFolder(folder, target);
        if (move)
        {
            Directory.Delete(folder, recursive: true);
        }
        return target;
    }

    /// <summary>Copies the files of a save folder (sav.dat, metadata, screenshot; no subfolders).</summary>
    private static void CopyFolder(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from))
        {
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)), overwrite: false);
        }
    }

    public static SaveSlot? TryRead(string folder)
    {
        try
        {
            return Read(folder);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static SaveSlot Read(string folder)
    {
        var screenshot = Path.Combine(folder, "screenshot.png");
        var slot = new SaveSlot
        {
            Folder = folder,
            ScreenshotPath = File.Exists(screenshot) ? screenshot : null,
            Kind = KindFromName(Path.GetFileName(folder)),
            SavedAt = File.GetLastWriteTime(Path.Combine(folder, "sav.dat"))
        };

        var metadataPath = Path.Combine(folder, "metadata.9.json");
        if (!File.Exists(metadataPath))
        {
            return slot;
        }

        using var doc = JsonDocument.Parse(File.ReadAllBytes(metadataPath));
        var m = doc.RootElement.GetProperty("Data").GetProperty("metadata");

        var savedAt = slot.SavedAt;
        if (DateTime.TryParseExact(Str(m, "timestampString"), "HH:mm:ss, dd.MM.yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var ts))
        {
            savedAt = ts;
        }

        var kind = slot.Kind;
        if (Bool(m, "isEndGameSave")) kind = SaveKind.EndGame;
        else if (Bool(m, "isPointOfNoReturn")) kind = SaveKind.PointOfNoReturn;

        return slot with
        {
            Kind = kind,
            SavedAt = savedAt,
            Level = (int)Num(m, "level"),
            StreetCred = (int)Num(m, "streetCred"),
            LifePath = Pretty(Str(m, "lifePath")),
            BodyGender = Str(m, "bodyGender"),
            Difficulty = Str(m, "difficulty"),
            Quest = QuestName(Str(m, "trackedQuestEntry")),
            BuildPatch = Str(m, "buildPatch"),
            GameVersion = (int)Num(m, "gameVersion"),
            SaveVersion = (int)Num(m, "saveVersion"),
            PlayTime = TimeSpan.FromSeconds(Num(m, "playthroughTime")),
            IsModded = Bool(m, "isModded")
        };
    }

    private static SaveKind KindFromName(string name) => name.Split('-')[0] switch
    {
        "ManualSave" => SaveKind.Manual,
        "AutoSave" => SaveKind.Auto,
        "QuickSave" => SaveKind.Quick,
        "PointOfNoReturnSave" => SaveKind.PointOfNoReturn,
        "EndGameSave" => SaveKind.EndGame,
        _ => SaveKind.Other
    };

    /// <summary>"quests/main_quest/act_01/q101_resurrection/base/prepare_before_leave" -> "Resurrection".</summary>
    private static string QuestName(string entry)
    {
        var parts = entry.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var quest = parts.FirstOrDefault(p => p.Length > 5 && (p[0] == 'q' || p[0] == 's' || p[0] == 'm') && char.IsDigit(p[1]) && p.Contains('_'));
        if (quest == null)
        {
            return "";
        }
        return Pretty(quest[(quest.IndexOf('_') + 1)..]);
    }

    /// <summary>"street_kid" / "StreetKid" -> "Street Kid".</summary>
    private static string Pretty(string s) =>
        string.Join(' ', System.Text.RegularExpressions.Regex.Replace(s.Replace('_', ' '), "(?<=[a-z])(?=[A-Z])", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
