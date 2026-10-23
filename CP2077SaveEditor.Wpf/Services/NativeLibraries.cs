using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace CP2077SaveEditor.Wpf.Services;

/// <summary>
/// Lets the single-file release load DirectXTexNet (used to decode item icons). DirectXTexNet finds its native
/// DirectXTexNetImpl.dll in a "lib" folder next to its own file, which a single-file bundle does not have. The DLLs
/// are embedded in the exe, unpacked once to the local app data folder, and loaded from there.
/// </summary>
public static class NativeLibraries
{
    private const string ManagedName = "DirectXTexNet";
    private static readonly string[] s_files = { "DirectXTexNet.dll", "lib/DirectXTexNetImpl.dll", "lib/Ijwhost.dll" };

    /// <summary>Hooks assembly resolution; only fires when DirectXTexNet.dll is not next to the exe (single-file).</summary>
    public static void Register() => AssemblyLoadContext.Default.Resolving += Resolve;

    private static Assembly? Resolve(AssemblyLoadContext context, AssemblyName name)
    {
        if (name.Name != ManagedName)
        {
            return null;
        }
        var assembly = typeof(NativeLibraries).Assembly;
        if (assembly.GetManifestResourceInfo("native/" + s_files[0]) == null)
        {
            return null;
        }

        // one folder per editor version, so an update never mixes old and new files
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CP2077 Save Editor", "native",
            assembly.GetName().Version?.ToString() ?? "0");
        foreach (var file in s_files)
        {
            var target = Path.Combine(dir, file.Replace('/', Path.DirectorySeparatorChar));
            using var source = assembly.GetManifestResourceStream("native/" + file)!;
            if (File.Exists(target) && new FileInfo(target).Length == source.Length)
            {
                continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var temp = target + "." + Environment.ProcessId + ".tmp";
            using (var output = File.Create(temp))
            {
                source.CopyTo(output);
            }
            File.Move(temp, target, overwrite: true);
        }
        return context.LoadFromAssemblyPath(Path.Combine(dir, s_files[0]));
    }
}
