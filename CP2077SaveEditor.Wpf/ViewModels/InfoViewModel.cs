using System.Collections.Generic;
using System.Diagnostics;
using CommunityToolkit.Mvvm.Input;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>A project this editor is built on, shown on the Info page.</summary>
public sealed record CreditEntry(string Name, string Author, string Role, string Url, string Glyph);

/// <summary>About the editor: version, download links, and the projects it builds on.</summary>
public sealed partial class InfoViewModel : PageViewModel
{
    private const string RepositoryAddress = "https://github.com/AtfastrSlushyMaker/cyberpunk-2077-save-editor-gui";

    public InfoViewModel() => IsEnabled = true;

    public override string Title => "Info";
    public override string Glyph => "";
    public override bool RequiresSave => false;

    public string Version => MainViewModel.Version;
    public string RepositoryUrl => RepositoryAddress;

    public IReadOnlyList<CreditEntry> Credits { get; } = new[]
    {
        new CreditEntry("CP2077 Save Editor", "AtfastrSlushyMaker", "This editor: patch 2.5 save support, the new interface and its features.",
            RepositoryAddress, ""),
        new CreditEntry("CyberCAT-SimpleGUI", "Deweh", "The original save editor (Nexus mod 718) this project is based on. All original features and research are theirs.",
            "https://github.com/Deweh/CyberCAT-SimpleGUI", ""),
        new CreditEntry("WolvenKit", "The WolvenKit team", "Reads and writes the save, the game database and the game archives (names and icons).",
            "https://github.com/WolvenKit/WolvenKit", ""),
        new CreditEntry("CyberCAT", "SirBitesalot and contributors", "The original Cyberpunk 2077 save format research.",
            "https://github.com/WolvenKit/CyberCAT", ""),
    };

    [RelayCommand]
    private void OpenLink(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    [RelayCommand]
    private void OpenReleases() => OpenLink(RepositoryAddress + "/releases/latest");
}
