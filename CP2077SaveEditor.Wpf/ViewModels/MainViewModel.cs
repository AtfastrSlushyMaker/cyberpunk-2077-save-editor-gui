using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    public MainViewModel()
    {
        Browser = new SaveBrowserViewModel(OpenAsync);
        Info = new InfoViewModel();
        Pages = new ObservableCollection<PageViewModel>
        {
            Browser,
            new PlayerViewModel(),
            new InventoryViewModel(),
            new AppearanceViewModel(),
            new VehiclesViewModel(),
            new QuestFactsViewModel(),
            new ExtrasViewModel(),
            Info,
        };
        foreach (var page in Pages)
        {
            page.OnSessionChanged(null);
        }
        _currentPage = Browser;
    }

    public static string Version { get; } = "v" + typeof(MainViewModel).Assembly.GetName().Version!.ToString(3);

    public SaveBrowserViewModel Browser { get; }
    public InfoViewModel Info { get; }
    public ObservableCollection<PageViewModel> Pages { get; }

    [ObservableProperty]
    private PageViewModel _currentPage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSession), nameof(SessionTitle), nameof(SessionDetails))]
    private EditorSession? _session;

    [ObservableProperty]
    private string _status = "Ready";

    [ObservableProperty]
    private bool _isBusy;


    public bool HasSession => Session != null;
    public bool HasUnsavedChanges => Session?.HasUnsavedChanges == true;
    public string SessionTitle => Session == null ? "No save open" : Session.Slot.FolderName;
    public string SessionDetails => Session == null ? "Pick a save to start editing" :
        $"Level {Session.Slot.Level}  ·  {Session.Slot.LifePath}  ·  patch {Session.Slot.BuildPatch}";

    [RelayCommand(CanExecute = nameof(HasSession))]
    private async Task SaveAsync()
    {
        if (Session == null || IsBusy)
        {
            return;
        }

        var dialog = new Views.SaveChangesDialog(new SaveChangesViewModel(Session, Browser.Root))
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        var (folder, backup) = dialog.Result;
        Session.MarkSaved();
        if (SaveLibrary.TryRead(folder) is { } slot)
        {
            Session.Slot = slot;
            OnPropertyChanged(nameof(SessionTitle));
            OnPropertyChanged(nameof(SessionDetails));
        }
        Status = backup == null ? $"Saved to {Path.GetFileName(folder)}" : $"Saved {Path.GetFileName(folder)} (backup: {backup})";
        await Browser.RefreshAsync();
    }

    partial void OnSessionChanged(EditorSession? oldValue, EditorSession? newValue)
    {
        if (oldValue != null) oldValue.UnsavedChangesChanged -= OnUnsavedChangesChanged;
        if (newValue != null) newValue.UnsavedChangesChanged += OnUnsavedChangesChanged;
        OnUnsavedChangesChanged();
        SaveCommand.NotifyCanExecuteChanged();
    }

    private void OnUnsavedChangesChanged() => OnPropertyChanged(nameof(HasUnsavedChanges));

    /// <summary>Asks before discarding unsaved edits; returns true if it is fine to continue.</summary>
    public bool ConfirmDiscardChanges() =>
        !HasUnsavedChanges || UserPrompt.Confirm("You have unsaved changes. Discard them?", "Unsaved changes");

    /// <summary>Opens a save folder or a sav.dat file.</summary>
    public Task OpenPathAsync(string path)
    {
        var folder = Directory.Exists(path) ? path : Path.GetDirectoryName(Path.GetFullPath(path))!;
        var slot = SaveLibrary.TryRead(folder);
        if (slot == null || !File.Exists(slot.SavePath))
        {
            UserPrompt.Notice($"No Cyberpunk 2077 save found at {path}", "Open save");
            return Task.CompletedTask;
        }
        return OpenAsync(slot);
    }

    private async Task OpenAsync(SaveSlot slot)
    {
        if (IsBusy)
        {
            return;
        }
        if (!slot.IsSupported)
        {
            UserPrompt.Notice($"{slot.FolderName} was made with game version {slot.BuildPatch}. Only saves from patch 2.0 or newer can be edited.", "Unsupported save");
            return;
        }

        if (!ConfirmDiscardChanges())
        {
            return;
        }

        IsBusy = true;
        Status = $"Opening {slot.FolderName}…";
        var watch = Stopwatch.StartNew();
        try
        {
            var session = await EditorSession.LoadAsync(slot);
            Session = session;
            foreach (var page in Pages)
            {
                page.OnSessionChanged(session);
            }
            if (CurrentPage == Browser)
            {
                CurrentPage = Pages[1]; // start editing on the Player page
            }
            Status = $"Opened {slot.FolderName} in {watch.Elapsed.TotalSeconds:0.0}s";
        }
        catch (Exception e)
        {
            Status = "Failed to open save";
            UserPrompt.Notice($"Could not open {slot.FolderName}:\n\n{e.Message}", "Error");
        }
        finally
        {
            IsBusy = false;
        }
    }
}
