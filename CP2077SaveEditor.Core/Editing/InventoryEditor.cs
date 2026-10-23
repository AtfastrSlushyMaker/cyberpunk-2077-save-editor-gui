using System;
using System.Collections.Generic;
using System.Linq;
using CP2077SaveEditor.Utils;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Save.Classes;
using WolvenKit.RED4.Types;
using static WolvenKit.RED4.Types.Enums;

namespace CP2077SaveEditor.Editing;

/// <summary>Patch 2.x item tier: Tier 1–5 (Common…Legendary) plus "+" / "++".</summary>
public readonly record struct ItemTier(int Tier, int Plus)
{
    public static readonly ItemTier Max = new(5, 2);

    public override string ToString() => $"Tier {Tier}{new string('+', Plus)}";
}

/// <summary>Inventory editing: containers, quantities, removal, quest flags, adding items and 2.x tiers.</summary>
public static class InventoryEditor
{
    public static readonly IReadOnlyDictionary<ulong, string> ContainerNames = new Dictionary<ulong, string>
    {
        [0x1] = "V's Inventory",
        [0xF4240] = "Car Stash",
        [0x895724] = "Nomad Intro Items",
        [0x895956] = "Street Kid Intro Items",
        [0x8959E8] = "Corpo Intro Items",
        [0x38E8D0C9F9A087AE] = "Panam's Stash",
        [0x6E48C594562422DE] = "Judy's Stash",
        [0x7901DE03D136A5AF] = "V's Wardrobe",
        [0xE5F556FCBB62A706] = "V's Stash",
        [0xEDAD8C9B086A615E] = "River's Stash",
    };

    public static string ContainerName(ulong id) => ContainerNames.TryGetValue(id, out var name) ? name : $"Container {id:X}";

    /// <summary>Item types that carry stats and can have a tier.</summary>
    public static bool HasTiers(string type) => type is "Weapon" or "WeaponItem" or "Clothing" or "Cyberware";

    /// <summary>Equipped item ids mapped to the equipment area name, e.g. "Weapon 1".</summary>
    public static Dictionary<gameItemID, string> EquippedItems(SaveFileHelper save)
    {
        try
        {
            return save.GetEquippedItems();
        }
        catch (Exception)
        {
            return new Dictionary<gameItemID, string>();
        }
    }

    /// <summary>Removes the item, unequipping it first. Only this exact item (record and seed) is unequipped.</summary>
    public static void Remove(SaveFileHelper save, SubInventory container, ItemData item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var id = item.ItemInfo.ItemId;
        foreach (var slot in save.GetEquipSlotsFromID(id))
        {
            if (slot.ItemID is { } equipped && (uint)equipped.RngSeed == (uint)id.RngSeed)
            {
                slot.ItemID = new gameItemID();
            }
        }
        container.Items.Remove(item);
    }

    public static void SetQuestFlag(ItemData item, bool isQuestItem) =>
        item.Flags = isQuestItem ? item.Flags | ItemFlag.IsQuestItem : item.Flags & ~ItemFlag.IsQuestItem;

    /// <summary>Current tier from the item's constant Quality / IsItemPlus stats; null if the tier is still a random roll.</summary>
    public static ItemTier? GetTier(SaveFileHelper save, ItemData item)
    {
        if (save.GetStatsFromItemId(item.ItemInfo.ItemId)?.ModifiersBuffer?.Data is not ModifiersBuffer modifiers)
        {
            return null;
        }

        var constants = modifiers.Entries.OfType<gameConstantStatModifierData_Deprecated>()
            .Where(m => (gameStatModifierType)m.ModifierType == gameStatModifierType.Additive).ToList();
        var quality = constants.LastOrDefault(m => (gamedataStatType)m.StatType == gamedataStatType.Quality);
        if (quality == null)
        {
            return null;
        }
        var plus = constants.LastOrDefault(m => (gamedataStatType)m.StatType == gamedataStatType.IsItemPlus);
        return new ItemTier(Math.Clamp((int)(float)quality.Value + 1, 1, 5), Math.Clamp((int)(plus?.Value ?? 0f), 0, 2));
    }

    /// <summary>
    /// Sets the tier the way the in-game verified "legendary" edit does: constant Quality (tier - 1), IsItemPlus,
    /// and RandomCurveInput multiplier 0 so the random quality roll no longer applies. Creates stat data if missing.
    /// </summary>
    public static void SetTier(SaveFileHelper save, ItemData item, ItemTier tier)
    {
        var stats = save.GetStatsFromItemId(item.ItemInfo.ItemId) ?? save.CreateStatData(item.ItemInfo.ItemId)
                    ?? throw new InvalidOperationException("Stat data could not be created for this item.");
        save.SetConstantStat(gamedataStatType.Quality, Math.Clamp(tier.Tier, 1, 5) - 1, stats);
        save.SetConstantStat(gamedataStatType.IsItemPlus, Math.Clamp(tier.Plus, 0, 2), stats);
        save.SetConstantStat(gamedataStatType.RandomCurveInput, 0, stats, gameStatModifierType.Multiplier);
    }

    /// <summary>Creates a new item from a record (port of the classic editor's Add Item) and adds it to the container.</summary>
    public static ItemData Add(SaveFileHelper save, SubInventory container, TweakDBID recordId, uint quantity)
    {
        if (!ResourceHelper.ItemClasses.TryGetValue(recordId, out var record))
        {
            throw new ArgumentException($"No item class data for {ItemCatalog.DisplayName(recordId)}");
        }

        // Stackable items live in a single stack per record: top it up instead of creating a second one
        if (record.IsSingleInstance && container.Items.FirstOrDefault(i => i.ItemInfo.ItemId.Id == recordId && i.HasQuantity()) is { } stack)
        {
            stack.Quantity = (uint)Math.Min(uint.MaxValue, (ulong)stack.Quantity + quantity);
            return stack;
        }

        var item = new ItemData
        {
            ItemInfo = new ItemInfo
            {
                ItemId = new gameItemID { Id = recordId, UniqueCounter = save.GetNextUniqueCounter() }
            }
        };

        if (record.Type == "Grenade")
        {
            item.ItemInfo.ItemStructure = ItemStructure.Quantity | ItemStructure.Extended;
            item.ItemInfo.ItemId.RngSeed = 2;
            item.Quantity = quantity;
            item.ItemAdditionalInfo = new ItemAdditionalInfo();
            item.ItemSlotPart = EmptySlotPart();
        }
        else if (record.IsSingleInstance)
        {
            item.ItemInfo.ItemStructure = ItemStructure.None;
            item.ItemInfo.ItemId.RngSeed = 2;
            item.Quantity = quantity;
        }
        else
        {
            item.ItemInfo.ItemStructure = ItemStructure.None;
            item.ItemInfo.ItemId.RngSeed = save.CreateUniqueSeed();
            item.ItemAdditionalInfo = new ItemAdditionalInfo();
            item.ItemSlotPart = EmptySlotPart();
        }

        if (item.HasExtendedData())
        {
            if (record.Type is "Clothing" or "Weapon" or "WeaponItem")
            {
                save.CreateStatData(item.ItemInfo.ItemId);
            }

            if (record.SlotParts is { Count: > 0 })
            {
                item.ItemSlotPart = SlotPart(save, record.SlotParts[0].ItemPartPreset, record.SlotParts[0].Slot);
                item.ItemSlotPart.Children = record.SlotParts.Skip(1)
                    .Select(p => SlotPart(save, p.ItemPartPreset, p.Slot)).ToList();
            }
        }

        container.Items.Add(item);
        return item;
    }

    /// <summary>True if the record stacks (quantity editable when adding).</summary>
    public static bool IsStackable(TweakDBID recordId) =>
        ResourceHelper.ItemClasses.TryGetValue(recordId, out var record) && (record.IsSingleInstance || record.Type == "Grenade");

    private static ItemSlotPart EmptySlotPart() => new()
    {
        ItemInfo = new ItemInfo { ItemId = new gameItemID() },
        AppearanceName = "None",
        ItemAdditionalInfo = new ItemAdditionalInfo()
    };

    private static ItemSlotPart SlotPart(SaveFileHelper save, TweakDBID id, TweakDBID slot) => new()
    {
        ItemInfo = new ItemInfo
        {
            ItemId = new gameItemID
            {
                Id = id,
                RngSeed = save.CreateUniqueSeed(),
                UniqueCounter = save.GetNextUniqueCounter()
            }
        },
        AppearanceName = "None",
        AttachmentSlotTdbId = slot,
        ItemAdditionalInfo = new ItemAdditionalInfo { RequiredLevel = float.MaxValue }
    };
}
