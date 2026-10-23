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
