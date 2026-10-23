using System;
using WolvenKit.RED4.Types;

namespace CP2077SaveEditor.Utils;

public static class CNameExtensions
{
    public static bool Contains(this CName value, string search)
    {
        var text = value.GetResolvedText();
        if (text == null)
        {
            throw new Exception();
        }
        return text.Contains(search);
    }

    public static string[] Split(this CName value, string separator, StringSplitOptions options = StringSplitOptions.None)
    {
        var text = value.GetResolvedText();
        if (text == null)
        {
            throw new Exception();
        }
        return text.Split(separator, options);
    }
}
