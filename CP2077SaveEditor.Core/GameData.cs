using WolvenKit.Common.Services;
using WolvenKit.RED4.TweakDB.Helper;

namespace CP2077SaveEditor;

/// <summary>Game data shared by the editing logic and the front ends.</summary>
public static class GameData
{
    private static TweakDBStringHelper? s_tweakDbStrings;

    /// <summary>TweakDB record names (e.g. "Items.Preset_Saratoga_Arasaka_2077"), loaded on first use.</summary>
    public static TweakDBStringHelper TweakDbStrings
    {
        get
        {
            if (s_tweakDbStrings == null)
            {
                var stream = typeof(TweakDBService).Assembly.GetManifestResourceStream("WolvenKit.Common.Resources.tweakdbstr.kark")!;
                var helper = new TweakDBStringHelper();
                helper.LoadFromStream(stream);
                s_tweakDbStrings = helper;
            }
            return s_tweakDbStrings;
        }
        set => s_tweakDbStrings = value;
    }
}
