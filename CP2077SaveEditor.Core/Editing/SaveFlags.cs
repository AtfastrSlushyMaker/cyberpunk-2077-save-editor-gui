using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.Classes;

namespace CP2077SaveEditor.Editing;

/// <summary>The save's "modded" markers as found in metadata.9.json and sav.dat.</summary>
public sealed record ModdedState(bool? MetadataModded, int ModioModCount, bool? SaveDataModded)
{
    public bool IsModded => MetadataModded == true || SaveDataModded == true || ModioModCount > 0;
}

/// <summary>An inventory item that belongs to the Phantom Liberty expansion.</summary>
public sealed record ExpansionItem(SubInventory Container, ItemData Item, string Record);

/// <summary>
/// Patch 2.5 save flags. Saves made with mods enabled are marked in two places: metadata.9.json
/// ("isModded", "enabledModioInfo") and the one-byte ModdingSystem section of sav.dat. Expansion content is listed in
/// metadata "additionalContentIds".
/// <para>
/// The ModdingSystem byte is 0 in unmodded saves; that 1 means "modded" is inferred (the section was added for mod
/// support and holds a single flag), not yet confirmed against a save made with mods.
/// </para>
/// </summary>
public static class SaveFlags
{
    public const string ModdingSystemNode = "ModdingSystem";

    // "ep1" as its own token (Vest_EP1, sts_ep1_04_shard, EP1_Stanalone_…) or a Phantom Liberty quest prefix (q301…, mq303…, sq30x…)
    private static readonly Regex s_expansionRecord = new(@"(?i)((?<![a-z])ep1(?![0-9])|^Items\.(m|s)?q30\d)", RegexOptions.Compiled);

    // ---------------- modded flag ----------------

    public static ModdedState ReadModded(SaveFileHelper save)
    {
        bool? metadataModded = null;
        var modioCount = 0;
        if (Metadata(save) is { } meta)
        {
            metadataModded = meta["isModded"]?.GetValue<bool>();
            modioCount = (meta["enabledModioInfo"] as JsonArray)?.Count ?? 0;
        }

        bool? saveModded = ModdingSystemBlob(save) is { Length: >= 1 } blob ? blob[0] != 0 : null;
        return new ModdedState(metadataModded, modioCount, saveModded);
    }

    /// <summary>Sets or clears the modded markers. Clearing also empties the list of enabled mod.io mods.</summary>
    public static void SetModded(SaveFileHelper save, bool modded)
    {
        if (save.Metadata is { } metadata)
        {
            var text = Decode(metadata, out var bom);
            text = Regex.Replace(text, "\"isModded\":\\s*(true|false)", $"\"isModded\": {(modded ? "true" : "false")}");
            if (!modded)
            {
                // the array may hold objects with nested arrays: match up to one nesting level
                text = Regex.Replace(text, "\"enabledModioInfo\":\\s*\\[(?:[^\\[\\]]|\\[[^\\[\\]]*\\])*\\]", "\"enabledModioInfo\": []");
            }
            save.Metadata = Encode(text, bom);
        }

        if (ModdingSystemBlob(save) is { Length: >= 1 } blob)
        {
            blob[0] = (byte)(modded ? 1 : 0);
        }
    }

    private static byte[]? ModdingSystemBlob(SaveFileHelper save) =>
        (save.SaveFile.Nodes.FirstOrDefault(n => n.Name == ModdingSystemNode)?.Value as DefaultRepresentation)?.HeaderBlob;

    // ---------------- expansion (DLC) flags ----------------

    /// <summary>Entries of metadata "additionalContentIds" (e.g. the Phantom Liberty id), as text.</summary>
    public static List<string> AdditionalContent(SaveFileHelper save) =>
        (Metadata(save)?["additionalContentIds"] as JsonArray)?.Select(n => n?.ToJsonString().Trim('"') ?? "").ToList() ?? new List<string>();

    /// <summary>Removes one entry from metadata "additionalContentIds". The expansion's world data stays in sav.dat.</summary>
    public static bool RemoveAdditionalContent(SaveFileHelper save, string id)
    {
        if (save.Metadata is not { } metadata || Metadata(save)?["additionalContentIds"] is not JsonArray array)
        {
            return false;
        }
        var remaining = array.Where(n => (n?.ToJsonString().Trim('"') ?? "") != id).Select(n => n?.DeepClone()).ToList();
        if (remaining.Count == array.Count)
        {
            return false;
        }

        var text = Decode(metadata, out var bom);
        var replacement = "\"additionalContentIds\": " + new JsonArray(remaining.ToArray()).ToJsonString();
        text = Regex.Replace(text, "\"additionalContentIds\":\\s*\\[[^\\]]*\\]", replacement.Replace("$", "$$"));
        save.Metadata = Encode(text, bom);
        return true;
    }

    /// <summary>True for item records that belong to Phantom Liberty (tagged "ep1" or from its quests q301–q307/mq30x/sq30x).</summary>
    public static bool IsExpansionRecord(string? record) => record != null && s_expansionRecord.IsMatch(record);

    public static List<ExpansionItem> ExpansionItems(SaveFileHelper save) =>
        save.GetInventoriesContainer().SubInventories
            .SelectMany(c => c.Items.Select(i => (Container: c, Item: i, Record: ItemCatalog.RecordName(i.ItemInfo.ItemId.Id))))
            .Where(x => IsExpansionRecord(x.Record))
            .Select(x => new ExpansionItem(x.Container, x.Item, x.Record!))
            .ToList();

    /// <summary>Removes all Phantom Liberty items (unequipping them first); returns how many were removed.</summary>
    public static int RemoveExpansionItems(SaveFileHelper save)
    {
        var items = ExpansionItems(save);
        foreach (var x in items)
        {
            InventoryEditor.Remove(save, x.Container, x.Item);
        }
        return items.Count;
    }

    // ---------------- metadata helpers ----------------

    private static JsonNode? Metadata(SaveFileHelper save)
    {
        if (save.Metadata == null)
        {
            return null;
        }
        try
        {
            return JsonNode.Parse(Decode(save.Metadata, out _))?["Data"]?["metadata"];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Decode(byte[] bytes, out bool bom)
    {
        bom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
        return Encoding.UTF8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
    }

    private static byte[] Encode(string text, bool bom)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return bom ? new byte[] { 0xEF, 0xBB, 0xBF }.Concat(bytes).ToArray() : bytes;
    }
}
