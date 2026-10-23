using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using CP2077SaveEditor.Utils;
using WolvenKit.RED4.CR2W.JSON;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Editing;

/// <summary>One character-customization entry (head, arms or body), as edited in the advanced appearance editor.</summary>
public sealed record AppearanceEntry(string Section, string Group, gameuiCustomizationAppearance Entry)
{
    public string Key => $"{Section}/{Group}/{Entry.Name.GetResolvedText()}";
}

/// <summary>Appearance presets and raw customization entries.</summary>
public static class AppearanceEditor
{
    public const string PresetExtension = ".v2preset";
    public const string LegacyPresetExtension = ".preset";

    public static List<AppearanceEntry> Entries(SaveFileHelper save)
    {
        var preset = save.GetAppearanceContainer().Preset;
        var result = new List<AppearanceEntry>();
        void Add(string section, IEnumerable<gameuiCustomizationGroup> groups)
        {
            foreach (var group in groups)
            {
                foreach (var entry in group.Customization)
                {
                    result.Add(new AppearanceEntry(section, group.Name.GetResolvedText() ?? "", entry));
                }
            }
        }
        Add("head", preset.HeadGroups);
        Add("arms", preset.ArmsGroups);
        Add("body", preset.BodyGroups);
        return result;
    }

    /// <summary>Updates an entry; only fields that differ are written.</summary>
    public static bool Apply(AppearanceEntry entry, string definition, string name, string resourcePath)
    {
        var e = entry.Entry;
        var changed = false;
        if (e.Definition.GetResolvedText() != definition) { e.Definition = definition; changed = true; }
        if (e.Name.GetResolvedText() != name) { e.Name = name; changed = true; }
        if (e.Resource.DepotPath.GetResolvedText() != resourcePath)
        {
            e.Resource = new CResourceAsyncReference<appearanceAppearanceResource>(resourcePath, e.Resource.Flags);
            changed = true;
        }
        return changed;
    }

    /// <summary>Serializes the save's appearance as a .v2preset file.</summary>
    public static string SavePreset(SaveFileHelper save) =>
        RedJsonSerializer.Serialize(new PresetDto { Data = save.GetAppearanceContainer() });

    /// <summary>
    /// Applies a .v2preset (or legacy CyberCAT .preset) to the save. Body gender is switched first if the preset needs it.
    /// Throws <see cref="FormatException"/> for files that cannot be read.
    /// </summary>
    public static void LoadPreset(SaveFileHelper save, string text, string extension)
    {
        gameuiCharacterCustomizationPresetWrapper values;
        if (string.Equals(extension, LegacyPresetExtension, StringComparison.OrdinalIgnoreCase))
        {
            if (text.StartsWith('L'))
            {
                throw new FormatException("\"Appearance Change Unlocker\" presets are not supported.");
            }
            values = LegacyPresetHelper.Convert(text);
        }
        else
        {
            if (JsonNode.Parse(text) is not JsonObject json)
            {
                throw new FormatException("The preset is not a JSON object.");
            }
            if (json.TryGetPropertyValue("JsonVersion", out var version) && version != null)
            {
                values = RedJsonSerializer.Deserialize<PresetDto>(text, new RedJsonSerializerOptions { JsonVersion = version.GetValue<string>() })!.Data;
            }
            else
            {
                values = TryDeserialize(text, "0.0.6") ?? TryDeserialize(text, "0.0.7")
                         ?? throw new FormatException("Unknown preset format.");
            }
        }

        if ((bool)values.Preset.IsMale != (bool)save.GetAppearanceContainer().Preset.IsMale)
        {
            save.Appearance.SuppressBodyGenderPrompt = true;
            save.Appearance.BodyGender = values.Preset.IsMale ? AppearanceGender.Male : AppearanceGender.Female;
        }
        save.SetAppearanceContainer(values);
    }

    private static gameuiCharacterCustomizationPresetWrapper? TryDeserialize(string text, string jsonVersion)
    {
        try
        {
            return RedJsonSerializer.Deserialize<gameuiCharacterCustomizationPresetWrapper>(text, new RedJsonSerializerOptions { JsonVersion = jsonVersion });
        }
        catch (Exception)
        {
            return null;
        }
    }
}
