using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>A project this editor is built on, shown on the Info page.</summary>
public sealed record CreditEntry(string Name, string Author, string Role, string Url, string Glyph);

/// <summary>About the editor: version, updates, and the projects it builds on.</summary>
public sealed partial class InfoViewModel : PageViewModel
{
    public InfoViewModel() => IsEnabled = true;

    public override string Title => "Info";
    public override string Glyph => "";
    public override bool RequiresSave => false;

    public string Version => "v" + UpdateService.CurrentVersion.ToString(3);
    public string RepositoryUrl => UpdateService.RepositoryUrl;

    public IReadOnlyList<CreditEntry> Credits { get; } = new[]
    {
        new CreditEntry("CP2077 Save Editor", "AtfastrSlushyMaker", "This editor: patch 2.5 save support, the new interface and its features.",
            UpdateService.RepositoryUrl, ""),
        new CreditEntry("CyberCAT-SimpleGUI", "Deweh", "The original save editor (Nexus mod 718) this project is based on. All original features and research are theirs.",
            "https://github.com/Deweh/CyberCAT-SimpleGUI", ""),
        new CreditEntry("WolvenKit", "The WolvenKit team", "Reads and writes the save, the game database and the game archives (names and icons).",
            "https://github.com/WolvenKit/WolvenKit", ""),
        new CreditEntry("CyberCAT", "SirBitesalot and contributors", "The original Cyberpunk 2077 save format research.",
            "https://github.com/WolvenKit/CyberCAT", ""),
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUpdate))]
    private UpdateInfo? _update;

    [ObservableProperty]
    private string _updateStatus = "Not checked yet";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _checkOnStart = AppSettings.Current.CheckForUpdates;

    public bool HasUpdate => Update != null;

    partial void OnCheckOnStartChanged(bool value)
    {
        AppSettings.Current.CheckForUpdates = value;
        AppSettings.Current.Save();
    }

    [RelayCommand]
    private void OpenLink(string url) => UpdateService.OpenUrl(url);

    [RelayCommand]
    public async Task CheckForUpdatesAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        UpdateStatus = "Checking GitHub for updates…";
        try
        {
            Update = await UpdateService.CheckAsync();
            UpdateStatus = Update == null ? $"You have the latest version ({Version})." : $"Version {Update.Tag} is available.";
        }
        catch (Exception e)
        {
            UpdateStatus = "Could not check for updates: " + e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (Update is not { } update || IsBusy) return;
        if (update.ZipUrl == null)
        {
            UpdateService.OpenUrl(update.PageUrl);
            return;
        }
        if (!UserPrompt.Confirm($"Download and install {update.Tag}? The editor closes, updates and starts again. Unsaved changes are lost.", "Update"))
        {
            return;
        }
        IsBusy = true;
        try
        {
            await UpdateService.PrepareInstallAsync(update, new Progress<string>(s => UpdateStatus = s));
            System.Windows.Application.Current.Shutdown();
        }
        catch (Exception e)
        {
            App.Log(e);
            UpdateStatus = "The update failed: " + e.Message + " You can download it from the releases page.";
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenReleases() => UpdateService.OpenUrl(Update?.PageUrl ?? UpdateService.ReleasesUrl);
}
