using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using CP2077SaveEditor.Utils;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Editing;

/// <summary>An item record the editor knows about.</summary>
public sealed record CatalogItem(ulong Id, string Record, string DisplayName, string Type);

/// <summary>
/// Item record names and types. Names come from a compact list generated from the game's TweakDB
/// (Resources/ItemNames.json, see gen_item_names.ps1) instead of WolvenKit's full name table, which costs ~900 MB.
/// </summary>
public static class ItemCatalog
{
    private static readonly Lazy<Dictionary<ulong, string>> s_names = new(() =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(CoreResources.ItemNames)!
            .ToDictionary(kv => ulong.Parse(kv.Key), kv => kv.Value));

    private static readonly Lazy<List<CatalogItem>> s_addable = new(() =>
        ResourceHelper.ItemClasses
            .Where(kv => s_names.Value.ContainsKey(kv.Key))
            .Select(kv => new CatalogItem(kv.Key, s_names.Value[kv.Key], DisplayNameOf(s_names.Value[kv.Key]), kv.Value.Type))
            .OrderBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList());

    /// <summary>Items the editor can create (records with known item class data).</summary>
    public static IReadOnlyList<CatalogItem> Addable => s_addable.Value;

    /// <summary>Every item record name the editor knows (addable items plus quest items, shards, …).</summary>
    public static IEnumerable<string> AllRecordNames() => s_names.Value.Values;

    /// <summary>"Items.Preset_Saratoga_Arasaka_2077", or null if unknown.</summary>
    public static string? RecordName(TweakDBID id) => s_names.Value.TryGetValue((ulong)id, out var name) ? name : null;

    public static string DisplayName(TweakDBID id) =>
        RecordName(id) is { } record ? DisplayNameOf(record) : $"Unknown item ({(ulong)id:X})";

    /// <summary>Item class from ItemClasses.json (Weapon, Clothing, Cyberware, Consumable, …) or "Other".</summary>
    public static string Type(TweakDBID id) => ResourceHelper.ItemClasses.TryGetValue(id, out var record) ? record.Type : "Other";

    /// <summary>"Items.Preset_Saratoga_Arasaka_2077" -> "Saratoga Arasaka 2077", "Ammo.HandgunAmmo" -> "Handgun Ammo".</summary>
    public static string DisplayNameOf(string record)
    {
        var name = Regex.Replace(record, "^(Items|Ammo)\\.", "");
        name = Regex.Replace(name, "^(Preset_|Preset|w_|q\\d{3}_)", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, "(?<=[a-z])(?=[A-Z])", " "); // CamelCase -> words
        name = Regex.Replace(name.Replace('_', ' '), " {2,}", " ").Trim();
        return name.Length == 0 ? record : char.ToUpperInvariant(name[0]) + name[1..];
    }
}
