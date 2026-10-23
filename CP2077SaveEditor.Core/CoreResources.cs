using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace CP2077SaveEditor;

/// <summary>
/// Data files embedded in the Core assembly. Each file is decoded with the encoding it was authored in.
/// </summary>
public static class CoreResources
{
    static CoreResources() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static string AppearanceValues => Read("AppearanceValues.json", 1252);
    public static string Facts => Read("Facts.json", 1252);
    public static string FastTravel => Read("FastTravel.json", 28591);
    public static string FemaleDefaultPreset => Read("FemaleDefault.preset", 1252);
    public static string ItemClasses => Read("ItemClasses.json", 65001);
    public static string MaleDefaultPreset => Read("MaleDefault.preset", 1252);
    public static string ModifierGroups => Read("ModifierGroups.json", 28591);
    public static string Mods => Read("Mods.json", 1252);
    public static string Vehicles => Read("Vehicles.json", 1252);
    public static string ItemNames => Read("ItemNames.json", 65001);

    private static string Read(string fileName, int codePage)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"CP2077SaveEditor.Resources.{fileName}")
                           ?? throw new FileNotFoundException(fileName);
        using var reader = new StreamReader(stream, Encoding.GetEncoding(codePage), detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
