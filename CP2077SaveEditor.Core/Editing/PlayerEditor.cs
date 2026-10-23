using System.Collections.Generic;
using System.Linq;
using WolvenKit.RED4.Archive.Buffer;
using WolvenKit.RED4.Types;
using static WolvenKit.RED4.Types.Enums;

namespace CP2077SaveEditor.Editing;

/// <summary>Editable player values, read from and applied to a save with <see cref="PlayerEditor"/>.</summary>
public sealed class PlayerSnapshot
{
    public int Level { get; set; }
    public int StreetCred { get; set; }
    public gamedataLifePath LifePath { get; set; }

    /// <summary>Body, Reflexes, Technical Ability, Intelligence, Cool.</summary>
    public Dictionary<gamedataStatType, int> Attributes { get; } = new();

    /// <summary>Headhunter, Netrunner, Shinobi, Solo, Engineer.</summary>
    public Dictionary<gamedataProficiencyType, int> Skills { get; } = new();

    public int AttributePoints { get; set; }
    public int PerkPoints { get; set; }
    public int RelicPoints { get; set; }

    /// <summary>Eurodollars, or null if the player inventory has no money item.</summary>
    public uint? Money { get; set; }

    public PlayerSnapshot Clone()
    {
        var copy = new PlayerSnapshot
        {
            Level = Level,
            StreetCred = StreetCred,
            LifePath = LifePath,
            AttributePoints = AttributePoints,
            PerkPoints = PerkPoints,
            RelicPoints = RelicPoints,
            Money = Money
        };
        foreach (var (k, v) in Attributes) copy.Attributes[k] = v;
        foreach (var (k, v) in Skills) copy.Skills[k] = v;
        return copy;
    }
}

/// <summary>One-click changes to a <see cref="PlayerSnapshot"/>.</summary>
public static class PlayerPresets
{
    public static void MaxLevelAndStreetCred(PlayerSnapshot s)
    {
        s.Level = PlayerEditor.MaxLevel;
        s.StreetCred = PlayerEditor.MaxStreetCred;
    }

    public static void MaxAttributes(PlayerSnapshot s)
    {
        foreach (var key in s.Attributes.Keys.ToList()) s.Attributes[key] = PlayerEditor.MaxAttribute;
    }

    public static void MaxSkills(PlayerSnapshot s)
    {
        foreach (var key in s.Skills.Keys.ToList()) s.Skills[key] = PlayerEditor.MaxSkill;
    }

    public static void MaxEverything(PlayerSnapshot s)
    {
        MaxLevelAndStreetCred(s);
        MaxAttributes(s);
        MaxSkills(s);
    }

    /// <summary>Puts every attribute back to its minimum and returns the spent points as unspent attribute points.</summary>
    public static void RefundAttributes(PlayerSnapshot s)
    {
        foreach (var key in s.Attributes.Keys.ToList())
        {
            var value = s.Attributes[key];
            if (value > PlayerEditor.MinAttribute)
            {
                s.AttributePoints += value - PlayerEditor.MinAttribute;
                s.Attributes[key] = PlayerEditor.MinAttribute;
            }
        }
    }
}

/// <summary>
/// Reads and writes player progression. Each value lives in the character sheet (PlayerDevelopmentData) and in the
/// player's forced stat modifiers; both are kept in sync, as the game expects. Only changed values are written.
/// </summary>
public static class PlayerEditor
{
    // Patch 2.x limits
    public const int MaxLevel = 60;
    public const int MaxStreetCred = 50;
    public const int MinAttribute = 3;
    public const int MaxAttribute = 20;
    public const int MaxSkill = 60;

    public const ulong PlayerSubInventoryId = 1;
    public static readonly TweakDBID MoneyItem = "Items.money";

    public static readonly gamedataStatType[] AttributeOrder =
    {
        gamedataStatType.Strength, gamedataStatType.Reflexes, gamedataStatType.TechnicalAbility,
        gamedataStatType.Intelligence, gamedataStatType.Cool
    };

    public static readonly gamedataProficiencyType[] SkillOrder =
    {
        gamedataProficiencyType.CoolSkill, gamedataProficiencyType.IntelligenceSkill, gamedataProficiencyType.ReflexesSkill,
        gamedataProficiencyType.StrengthSkill, gamedataProficiencyType.TechnicalAbilitySkill
    };

    private static readonly Dictionary<gamedataProficiencyType, gamedataStatType> s_proficiencyStats = new()
    {
        [gamedataProficiencyType.Level] = gamedataStatType.Level,
        [gamedataProficiencyType.StreetCred] = gamedataStatType.StreetCred,
        [gamedataProficiencyType.CoolSkill] = gamedataStatType.CoolSkill,
        [gamedataProficiencyType.IntelligenceSkill] = gamedataStatType.IntelligenceSkill,
        [gamedataProficiencyType.ReflexesSkill] = gamedataStatType.ReflexesSkill,
        [gamedataProficiencyType.StrengthSkill] = gamedataStatType.StrengthSkill,
        [gamedataProficiencyType.TechnicalAbilitySkill] = gamedataStatType.TechnicalAbilitySkill,
    };

    public static PlayerSnapshot Read(SaveFileHelper save)
    {
        var dev = save.GetPlayerDevelopmentData();
        var snapshot = new PlayerSnapshot { LifePath = dev.LifePath };

        foreach (var proficiency in dev.Proficiencies)
        {
            var type = (gamedataProficiencyType)proficiency.Type;
            if (type == gamedataProficiencyType.Level) snapshot.Level = proficiency.CurrentLevel;
            else if (type == gamedataProficiencyType.StreetCred) snapshot.StreetCred = proficiency.CurrentLevel;
            else if (SkillOrder.Contains(type)) snapshot.Skills[type] = proficiency.CurrentLevel;
        }

        foreach (var attribute in dev.Attributes)
        {
            var type = (gamedataStatType)attribute.AttributeName;
            if (AttributeOrder.Contains(type)) snapshot.Attributes[type] = attribute.Value;
        }

        foreach (var devPoint in dev.DevPoints)
        {
            switch ((gamedataDevelopmentPointType)devPoint.Type)
            {
                case gamedataDevelopmentPointType.Attribute: snapshot.AttributePoints = devPoint.Unspent; break;
                case gamedataDevelopmentPointType.Primary: snapshot.PerkPoints = devPoint.Unspent; break;
                case gamedataDevelopmentPointType.Espionage: snapshot.RelicPoints = devPoint.Unspent; break;
            }
        }

        snapshot.Money = FindMoney(save)?.Quantity;
        return snapshot;
    }

    /// <summary>Writes every value of <paramref name="target"/> that differs from the save.</summary>
    public static void Apply(SaveFileHelper save, PlayerSnapshot target)
    {
        var dev = save.GetPlayerDevelopmentData();
        ModifiersBuffer? forced = null;
        ModifiersBuffer Forced() => forced ??= GetForcedModifiers(save);

        if ((gamedataLifePath)dev.LifePath != target.LifePath)
        {
            dev.LifePath = target.LifePath;
        }

        foreach (var proficiency in dev.Proficiencies)
        {
            var type = (gamedataProficiencyType)proficiency.Type;
            int? wanted = type switch
            {
                gamedataProficiencyType.Level => target.Level,
                gamedataProficiencyType.StreetCred => target.StreetCred,
                _ => target.Skills.TryGetValue(type, out var v) ? v : null
            };
            if (wanted is not { } value || proficiency.CurrentLevel == value)
            {
                continue;
            }

            proficiency.CurrentLevel = value;
            var max = type switch
            {
                gamedataProficiencyType.Level => MaxLevel,
                gamedataProficiencyType.StreetCred => MaxStreetCred,
                _ => MaxSkill
            };
            if (value >= max)
            {
                proficiency.CurrentExp = 0;
            }
            SetConstant(Forced(), s_proficiencyStats[type], value);
        }

        foreach (var attribute in dev.Attributes)
        {
            var type = (gamedataStatType)attribute.AttributeName;
            if (!target.Attributes.TryGetValue(type, out var value) || attribute.Value == value)
            {
                continue;
            }
            attribute.Value = value;
            SetConstant(Forced(), type, value);
        }

        foreach (var devPoint in dev.DevPoints)
        {
            int? wanted = (gamedataDevelopmentPointType)devPoint.Type switch
            {
                gamedataDevelopmentPointType.Attribute => target.AttributePoints,
                gamedataDevelopmentPointType.Primary => target.PerkPoints,
                gamedataDevelopmentPointType.Espionage => target.RelicPoints,
                _ => null
            };
            if (wanted is { } value && devPoint.Unspent != value)
            {
                devPoint.Unspent = value;
            }
        }

        if (target.Money is { } money && FindMoney(save) is { } moneyItem && moneyItem.Quantity != money)
        {
            moneyItem.Quantity = money;
        }
    }

    private static WolvenKit.RED4.Save.Classes.ItemData? FindMoney(SaveFileHelper save) =>
        save.GetInventoriesContainer().SubInventories
            .FirstOrDefault(s => s.InventoryId == PlayerSubInventoryId)?.Items
            .FirstOrDefault(i => i.ItemInfo.ItemId.Id == MoneyItem);

    private static ModifiersBuffer GetForcedModifiers(SaveFileHelper save)
    {
        var stats = save.GetStatsFromEntityId(1)
                    ?? throw new System.InvalidOperationException("The save has no player stat data.");
        if (stats.ForcedModifiersBuffer?.Data is not ModifiersBuffer buffer)
        {
            buffer = new ModifiersBuffer();
            stats.ForcedModifiersBuffer = new DataBuffer { Data = buffer };
        }
        return buffer;
    }

    private static void SetConstant(ModifiersBuffer buffer, gamedataStatType stat, float value)
    {
        var existing = buffer.Entries.OfType<gameConstantStatModifierData_Deprecated>()
            .FirstOrDefault(x => (gamedataStatType)x.StatType == stat && (gameStatModifierType)x.ModifierType == gameStatModifierType.Additive);
        if (existing == null)
        {
            buffer.Entries.Add(new gameConstantStatModifierData_Deprecated
            {
                StatType = stat,
                ModifierType = gameStatModifierType.Additive,
                Value = value
            });
        }
        else
        {
            existing.Value = value;
        }
    }
}
