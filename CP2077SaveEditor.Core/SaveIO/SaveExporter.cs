using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using WolvenKit.RED4.Save.IO;

namespace CP2077SaveEditor.SaveIO;

/// <summary>Writes an edited save to disk: to a new manual slot, or over its own slot after a backup.</summary>
public static class SaveExporter
{
    /// <summary>Backups are kept outside the game's save folder so the game never lists them as saves.</summary>
    public static string BackupRoot { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "CP2077 Save Editor", "Backups");

    /// <summary>Serializes the save, committing pending stat edits first.</summary>
    public static byte[] Serialize(SaveFileHelper save)
    {
        save.CommitStats();
        using var ms = new MemoryStream();
        using (var writer = new CyberpunkSaveWriter(ms, Encoding.UTF8, true))
        {
            writer.WriteFile(save.SaveFile, true);
        }
        return ms.ToArray();
    }

    /// <summary>The next free "ManualSave-N" folder name in <paramref name="savesRoot"/>.</summary>
    public static string NextManualSlotName(string savesRoot)
    {
        var used = Directory.Exists(savesRoot)
            ? Directory.EnumerateDirectories(savesRoot, "ManualSave-*")
                .Select(d => int.TryParse(Path.GetFileName(d)["ManualSave-".Length..], out var n) ? n : -1)
                .ToHashSet()
            : new HashSet<int>();

        var next = 0;
        while (used.Contains(next))
        {
            next++;
        }
        return $"ManualSave-{next}";
    }

    /// <summary>Copies all files of a save folder into a new timestamped backup folder and returns its path.</summary>
    public static string Backup(string slotFolder)
    {
        var target = Path.Combine(BackupRoot, Path.GetFileName(slotFolder), DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(slotFolder))
        {
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: false);
        }
        return target;
    }

    /// <summary>
    /// Writes sav.dat, metadata.9.json and screenshot.png into <paramref name="slotFolder"/> (created if needed).
    /// The metadata is updated for the new file: slot name, file size, and marked as a regular manual save.
    /// </summary>
    public static void WriteSlot(string slotFolder, byte[] savData, byte[]? metadata, byte[]? screenshot)
    {
        Directory.CreateDirectory(slotFolder);

        // Write to a temporary file first so a failure never leaves a truncated sav.dat behind
        var savPath = Path.Combine(slotFolder, "sav.dat");
        var tempPath = savPath + ".tmp";
        File.WriteAllBytes(tempPath, savData);
        File.Move(tempPath, savPath, overwrite: true);

        if (metadata != null)
        {
            File.WriteAllBytes(Path.Combine(slotFolder, "metadata.9.json"), UpdateMetadata(metadata, Path.GetFileName(slotFolder), savData.Length));
        }

        var screenshotPath = Path.Combine(slotFolder, "screenshot.png");
        if (screenshot != null && !File.Exists(screenshotPath))
        {
            File.WriteAllBytes(screenshotPath, screenshot);
        }
    }

    /// <summary>
    /// Edits the metadata text in place (keeping its formatting) rather than re-serializing it.
    /// </summary>
    public static byte[] UpdateMetadata(byte[] metadata, string slotName, long fileSize)
    {
        var hasBom = metadata.Length >= 3 && metadata[0] == 0xEF && metadata[1] == 0xBB && metadata[2] == 0xBF;
        var text = Encoding.UTF8.GetString(metadata, hasBom ? 3 : 0, metadata.Length - (hasBom ? 3 : 0));

        text = Regex.Replace(text, "\"name\": \"[^\"]*\"", $"\"name\": \"{slotName}\"");
        text = Regex.Replace(text, "\"fileSize\": \\d+", $"\"fileSize\": {fileSize}");
        if (slotName.StartsWith("ManualSave-", StringComparison.Ordinal))
        {
            text = text.Replace("\"isForced\": true", "\"isForced\": false").Replace("\"isCheckpoint\": true", "\"isCheckpoint\": false");
        }

        var bytes = Encoding.UTF8.GetBytes(text);
        return hasBom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray() : bytes;
    }
}
