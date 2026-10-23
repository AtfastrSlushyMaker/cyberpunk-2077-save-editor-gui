#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using WolvenKit.RED4.CR2W.JSON;
using WolvenKit.RED4.Save;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Utils;

public class AppearanceHelper2
{
    private readonly SaveFileHelper _saveFileHelper;
    private gameuiCharacterCustomizationPresetWrapper PresetWrapper => _saveFileHelper.GetAppearanceContainer();

    public AppearanceHelper2(SaveFileHelper saveFileHelper)
    {
        _saveFileHelper = saveFileHelper;
    }

    public bool SuppressBodyGenderPrompt { get; set; }

    public AppearanceGender BodyGender
    {
        get => PresetWrapper.Preset.IsMale ? AppearanceGender.Male : AppearanceGender.Female;
        set
        {
            if (value == BodyGender)
            {
                return;
            }

            if (!Global.PSDataEnabled)
            {
                UserPrompt.Notice("PSData is disabled. Body Gender cannot be changed.", "Notice");
                return;
            }

            if (!SuppressBodyGenderPrompt)
            {
                if (!UserPrompt.Confirm("Changing body gender will reset all appearance options to default. Do you wish to continue?", "Warning"))
                {
                    return;
                }

                SuppressBodyGenderPrompt = false;
            }

            PresetWrapper.Preset.IsMale = value == AppearanceGender.Male;

            var playerPuppet = _saveFileHelper.GetPSDataContainer()?.OfType<PlayerPuppetPS>().FirstOrDefault();
            if (playerPuppet == null)
            {
                UserPrompt.Notice("Player data not found. Aborting.", "Notice");
                return;
            }

            var oldTone = PresetWrapper.IsBrainGenderMale;
            if (value == AppearanceGender.Female)
            {
                playerPuppet.Gender = "Female";
                _saveFileHelper.SetAppearanceContainer(RedJsonSerializer.Deserialize<gameuiCharacterCustomizationPresetWrapper>(CoreResources.FemaleDefaultPreset, new RedJsonSerializerOptions { JsonVersion = "0.0.6" }));
            }
            else
            {
                playerPuppet.Gender = "Male";
                _saveFileHelper.SetAppearanceContainer(RedJsonSerializer.Deserialize<gameuiCharacterCustomizationPresetWrapper>(CoreResources.MaleDefaultPreset, new RedJsonSerializerOptions { JsonVersion = "0.0.6" }));
            }
            PresetWrapper.IsBrainGenderMale = oldTone;
        }
    }

    public AppearanceGender VoiceTone
    {
        get => PresetWrapper.IsBrainGenderMale ? AppearanceGender.Male : AppearanceGender.Female;
        set
        {
            if (!UserPrompt.Confirm("Changing voice tone can lead to major bugs. Do you wish to continue?", "Warning"))
            {
                return;
            }

            PresetWrapper.IsBrainGenderMale = value == AppearanceGender.Male;
        }
    }

    [Browsable(false)]
    public string HairStyle
    {
        get => (string)GetValue();
        set => SetValue(value);
    }

    [Browsable(false)]
    public string HairColor
    {
        get => (string)GetValue();
        set => SetValue(value);
    }

    [Browsable(false)]
    public string EyeColor
    {
        get => (string)GetValue();
        set => SetValue(value);
    }

    private object? GetValue([CallerMemberName] string? callerMemberName = null)
    {
        if (callerMemberName == nameof(HairStyle))
        {
            var app = GetAppearance(PresetWrapper.Preset.HeadGroups, "hairs");
            if (app == null)
            {
                return "None";
            }
            return (string)app.Name!;
        }

        if (callerMemberName == nameof(HairColor))
        {
            var app = GetAppearance(PresetWrapper.Preset.HeadGroups, "hairs");
            if (app == null)
            {
                return "None";
            }
            return (string)app.Definition!;
        }

        if (callerMemberName == nameof(EyeColor))
        {
            var app = GetAppearance(PresetWrapper.Preset.HeadGroups, "TPP", "eyes_color");
            if (app == null)
            {
                return "None";
            }
            return (string)app.Definition!;
        }

        throw new Exception();
    }

    private gameuiCustomizationAppearance? GetAppearance(CArray<gameuiCustomizationGroup> group, string entryName, string? customizationName = null)
    {
        foreach (var entry in group)
        {
            if (entry.Name == entryName)
            {
                if (entry.Customization.Count == 0)
                {
                    return null;
                }

                if (customizationName == null)
                {
                    if (entry.Customization.Count > 1)
                    {
                        throw new Exception();
                    }

                    return entry.Customization[0];
                }

                foreach (var customization in entry.Customization)
                {
                    if (customization.Name == customizationName)
                    {
                        return customization;
                    }
                }
            }
        }

        throw new Exception();
    }

    private void SetValue(object value, [CallerMemberName] string? callerMemberName = null)
    {
    }
}