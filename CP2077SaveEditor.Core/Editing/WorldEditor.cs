using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CP2077SaveEditor.ModSupport;
using CP2077SaveEditor.Utils;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Editing;

public sealed record VehicleEntry(string Record, string DisplayName, bool Unlocked);

/// <summary>A quest fact with its readable name (or hash when the name is not known).</summary>
public sealed record FactEntryInfo(string Name, bool IsKnown, FactsTable Table, FactsTable.FactEntry Entry);

/// <summary>Vehicles, quest facts and miscellaneous world edits.</summary>
public static class WorldEditor
{
    private static readonly Lazy<List<string>> s_vehicles = new(() => JsonSerializer.Deserialize<List<string>>(CoreResources.Vehicles)!);
    private static readonly Lazy<Dictionary<uint, string>> s_factNames = new(() => JsonSerializer.Deserialize<Dictionary<uint, string>>(CoreResources.Facts)!);

    // ---------------- Vehicles ----------------

    private static vehicleGarageComponentPS? Garage(SaveFileHelper save) =>
        save.GetPSDataContainer()?.OfType<vehicleGarageComponentPS>().FirstOrDefault();

    /// <summary>All known player vehicles with their unlocked state; null if the save has no garage data yet.</summary>
    public static List<VehicleEntry>? Vehicles(SaveFileHelper save)
    {
        var garage = Garage(save);
        if (garage == null)
        {
            return null;
        }

        // compare by id hash: resolving names would load WolvenKit's full name table
        var unlocked = garage.UnlockedVehicleArray?.Select(v => (ulong)v.VehicleID.RecordID).ToHashSet() ?? new HashSet<ulong>();
        return s_vehicles.Value
            .Select(record => new VehicleEntry(record, VehicleName(record), unlocked.Contains((ulong)(TweakDBID)record)))
            .OrderBy(v => v.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static void SetVehicleUnlocked(SaveFileHelper save, string record, bool unlocked)
    {
        var garage = Garage(save) ?? throw new InvalidOperationException("The save has no garage data.");
        garage.UnlockedVehicleArray ??= new();
        var id = (TweakDBID)record;
        var existing = garage.UnlockedVehicleArray.FirstOrDefault(v => v.VehicleID.RecordID == id);
        if (unlocked && existing == null)
        {
            garage.UnlockedVehicleArray.Add(new vehicleUnlockedVehicle { VehicleID = new vehicleGarageVehicleID { RecordID = id } });
        }
        else if (!unlocked && existing != null)
        {
            garage.UnlockedVehicleArray.Remove(existing);
        }
    }

    /// <summary>"Vehicle.v_sport2_quadra_type66_player" -> "Quadra Type66".</summary>
    public static string VehicleName(string record)
    {
        var name = record.StartsWith("Vehicle.", StringComparison.Ordinal) ? record[8..] : record;
        name = name.StartsWith("v_", StringComparison.Ordinal) ? name[2..] : name;
        // drop the internal category prefix (sport1_, sportbike2_, standard3_, utility4_ …)
        name = System.Text.RegularExpressions.Regex.Replace(name, @"^(sport|sportbike|standard|utility)\d*_", "");
        name = name.Replace("_player", "").Replace('_', ' ').Trim();
        return string.Join(' ', name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }

    // ---------------- Quest facts ----------------

    public static List<FactEntryInfo> Facts(SaveFileHelper save)
    {
        var names = s_factNames.Value;
        var result = new List<FactEntryInfo>();
        foreach (var tableNode in save.GetFactsContainer().Children)
        {
            var table = (FactsTable)tableNode.Value;
            foreach (var fact in table.FactEntries)
            {
                var hash = (uint)fact.FactName;
                var known = names.TryGetValue(hash, out var name);
                result.Add(new FactEntryInfo(known ? name! : $"0x{hash:X8}", known, table, fact));
            }
        }
        return result;
    }

    public static IEnumerable<string> KnownFactNames => s_factNames.Value.Values;

    public static void RemoveFact(FactEntryInfo fact) => fact.Table.FactEntries.Remove(fact.Entry);

    /// <summary>Sets a fact by its name, adding it if the save does not have it yet. Returns false for unknown names.</summary>
    public static bool SetFact(SaveFileHelper save, string name, uint value)
    {
        save.GetKnownFacts(); // loads the name table SetFactByName relies on
        return save.SetFactByName(name, value);
    }

    public static void EnableSecretEnding(SaveFileHelper save) => SetFact(save, "sq032_johnny_friend", 1);

    public static void MakeAllRomanceable(SaveFileHelper save)
    {
        foreach (var fact in new[] { "judy_romanceable", "river_romanceable", "panam_romanceable", "kerry_romanceable" })
        {
            SetFact(save, fact, 1);
        }
    }

    public static string ExportFacts(SaveFileHelper save) =>
        JsonSerializer.Serialize(Facts(save).Select(f => new KeyValuePair<string, uint>(f.Name, f.Entry.Value)), new JsonSerializerOptions { WriteIndented = true });

    // ---------------- Extras ----------------

    /// <summary>Unlocks every fast travel point; returns how many points are now unlocked.</summary>
    public static int UnlockAllFastTravel(SaveFileHelper save)
    {
        var fts = save.GetScriptableSystem<FastTravelSystem>() ?? throw new InvalidOperationException("The save has no fast travel data.");
        fts.FastTravelNodes.Clear();
        foreach (var record in ResourceHelper.FastTravelRecords)
        {
            fts.FastTravelNodes.Add(new gameFastTravelPointData { MarkerRef = record.MarkerRef, PointRecord = record.PointRecord });
        }
        return fts.FastTravelNodes.Count;
    }

    /// <summary>Removes the player's "invulnerable" god-mode overrides (left over by some quest scripts or mods).</summary>
    public static int MakePlayerVulnerable(SaveFileHelper save)
    {
        if (save.GetGodModeSystem()?.RootChunk is not gameGodModeSaveData data)
        {
            return 0;
        }
        var removed = 0;
        foreach (var entity in data.Gods.Where(g => g.EntityId.Hash == 1))
        {
            for (var i = entity.Data.Overrides.Count - 1; i >= 0; i--)
            {
                if (entity.Data.Overrides[i].Type == Enums.gameGodModeType.Invulnerable)
                {
                    entity.Data.Overrides.RemoveAt(i);
                    removed++;
                }
            }
        }
        return removed;
    }

    public static bool HasExtraWardrobeSlots(SaveFileHelper save) => save.GetScriptableSystem<WardrobeSystemExtra>() != null;

    public static void ClearWardrobeBlacklist(SaveFileHelper save)
    {
        if (save.GetScriptableSystem<WardrobeSystemExtra>() is { } wardrobe)
        {
            wardrobe.Blacklist = null;
        }
    }
}
