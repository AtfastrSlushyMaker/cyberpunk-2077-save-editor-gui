using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CP2077SaveEditor.Editing;

/// <summary>Readable sub-categories (weapon kinds, clothing slots, cyberware areas) and grade labels for item records.</summary>
public static class ItemCategories
{
    private static readonly Dictionary<string, string> s_names = new()
    {
        // weapons
        ["Wea_Handgun"] = "Pistols",
        ["Wea_Revolver"] = "Revolvers",
        ["Wea_SubmachineGun"] = "SMGs",
        ["Wea_Rifle"] = "Assault rifles",
        ["Wea_AssaultRifle"] = "Assault rifles",
        ["Wea_PrecisionRifle"] = "Precision rifles",
        ["Wea_SniperRifle"] = "Sniper rifles",
        ["Wea_Shotgun"] = "Shotguns",
        ["Wea_ShotgunDual"] = "Double-barrel shotguns",
        ["Wea_LightMachineGun"] = "LMGs",
        ["Wea_HeavyMachineGun"] = "HMGs",
        ["Wea_Knife"] = "Knives",
        ["Wea_Katana"] = "Katanas",
        ["Wea_Sword"] = "Swords",
        ["Wea_LongBlade"] = "Long blades",
        ["Wea_ShortBlade"] = "Short blades",
        ["Wea_Machete"] = "Machetes",
        ["Wea_Chainsword"] = "Chainswords",
        ["Wea_Axe"] = "Axes",
        ["Wea_Hammer"] = "Hammers",
        ["Wea_OneHandedClub"] = "One-handed clubs",
        ["Wea_TwoHandedClub"] = "Two-handed clubs",
        ["Wea_Fists"] = "Fists",
        ["Wea_VehicleMissileLauncher"] = "Vehicle weapons",
        ["Wea_VehiclePowerWeapon"] = "Vehicle weapons",
        ["Cyb_MantisBlades"] = "Mantis Blades",
        ["Cyb_NanoWires"] = "Monowire",
        ["Cyb_StrongArms"] = "Gorilla Arms",
        ["Cyb_Launcher"] = "Projectile Launch System",
        // clothing
        ["Clo_Head"] = "Head",
        ["Clo_Face"] = "Face",
        ["Clo_OuterChest"] = "Outer torso",
        ["Clo_InnerChest"] = "Inner torso",
        ["Clo_Legs"] = "Legs",
        ["Clo_Feet"] = "Feet",
        ["Clo_Outfit"] = "Outfits",
        // cyberware areas
        ["ArmsCW"] = "Arms",
        ["CardiovascularSystemCW"] = "Circulatory system",
        ["EyesCW"] = "Ocular system",
        ["FrontalCortexCW"] = "Frontal cortex",
        ["HandsCW"] = "Hands",
        ["ImmuneSystemCW"] = "Immune system",
        ["IntegumentarySystemCW"] = "Integumentary system",
        ["LegsCW"] = "Legs",
        ["MusculoskeletalSystemCW"] = "Skeleton",
        ["NervousSystemCW"] = "Nervous system",
        ["SystemReplacementCW"] = "Operating system",
        // consumables, gadgets, misc
        ["Con_Edible"] = "Food & drink",
        ["Con_Inhaler"] = "Inhalers",
        ["Con_Injector"] = "Injectors",
        ["Con_LongLasting"] = "Boosters",
        ["Con_Skillbook"] = "Skill shards",
        ["Con_Ammo"] = "Ammo",
        ["Gad_Grenade"] = "Grenades",
        ["Gen_CraftingMaterial"] = "Crafting materials",
        ["Gen_Jewellery"] = "Jewellery",
        ["Gen_Junk"] = "Junk",
        ["Gen_Keycard"] = "Keycards",
        ["Gen_MoneyShard"] = "Money shards",
        ["Gen_Readable"] = "Shards",
        ["Gen_Tarot"] = "Tarot",
        ["Gen_Misc"] = "Misc",
        ["Prt_Program"] = "Quickhacks",
        ["Prt_Fragment"] = "Cyberware mods",
        ["Prt_FabricEnhancer"] = "Clothing mods",
    };

    /// <summary>
    /// Sub-category key: the cyberware area for cyberware (so Mantis Blades sit under Arms), otherwise the item type.
    /// </summary>
    public static string? SubKey(string type, string? itemType, string? equipArea) =>
        type == "Cyberware" && !string.IsNullOrEmpty(equipArea) ? equipArea : itemType;

    /// <summary>"Wea_SubmachineGun" -> "SMGs"; unknown keys are made readable ("Prt_ShortScope" -> "Short scope").</summary>
    public static string SubName(string? key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return "Other";
        }
        if (s_names.TryGetValue(key, out var name))
        {
            return name;
        }
        if (key.EndsWith("FabricEnhancer"))
        {
            return "Clothing mods";
        }
        if (key.StartsWith("Prt_") && key.EndsWith("Mod"))
        {
            return "Weapon mods";
        }
        var words = Regex.Replace(Regex.Replace(key, "^[A-Za-z]{3}_", ""), "(?<=[a-z])(?=[A-Z])", " ").Replace('_', ' ').ToLowerInvariant();
        return char.ToUpperInvariant(words[0]) + words[1..];
    }

    /// <summary>Sort order of a quality ("Common" = 0 … "LegendaryPlusPlus" = 10); -1 for "Random" or unknown.</summary>
    public static int GradeRank(string? quality) => quality switch
    {
        "Common" => 0,
        "CommonPlus" => 1,
        "Uncommon" => 2,
        "UncommonPlus" => 3,
        "Rare" => 4,
        "RarePlus" => 5,
        "Epic" => 6,
        "EpicPlus" => 7,
        "Legendary" => 8,
        "LegendaryPlus" => 9,
        "LegendaryPlusPlus" => 10,
        _ => -1
    };

    /// <summary>2.0 tier name of a quality: "RarePlus" -> "Tier 3+".</summary>
    public static string GradeLabel(string? quality)
    {
        var rank = GradeRank(quality);
        if (rank < 0)
        {
            return "Random";
        }
        var tier = rank switch { <= 1 => 1, <= 3 => 2, <= 5 => 3, <= 7 => 4, _ => 5 };
        var plus = rank switch { 10 => "++", 1 or 3 or 5 or 7 or 9 => "+", _ => "" };
        return $"Tier {tier}{plus}";
    }
}
