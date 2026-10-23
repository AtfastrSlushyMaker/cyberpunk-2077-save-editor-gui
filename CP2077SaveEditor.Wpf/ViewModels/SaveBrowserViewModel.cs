using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

public sealed partial class SaveSlotViewModel : ObservableObject
{
    public SaveSlotViewModel(SaveSlot slot) => Slot = slot;

    public SaveSlot Slot { get; }

    public string Title => Slot.Kind switch
    {
        SaveKind.Manual => "Manual save",
        SaveKind.Auto => "Autosave",
        SaveKind.Quick => "Quicksave",
        SaveKind.PointOfNoReturn => "Point of no return",
        SaveKind.EndGame => "End game",
        _ => Slot.FolderName
    };

    public string Quest => string.IsNullOrEmpty(Slot.Quest) ? "—" : Slot.Quest;
    public string Character => string.Join("  ·  ", new[] { $"LVL {Slot.Level}", $"CRED {Slot.StreetCred}", Slot.LifePath }).Trim();
    public string When => Slot.SavedAt.ToString("dd MMM yyyy  HH:mm");
    public string PlayTime => $"{(int)Slot.PlayTime.TotalHours}h {Slot.PlayTime.Minutes:00}m played";
    public string Patch => string.IsNullOrEmpty(Slot.BuildPatch) ? "?" : Slot.BuildPatch;

    [ObservableProperty]
    private ImageSource? _thumbnail;

    public async Task LoadThumbnailAsync()
    {
        if (Thumbnail != null || Slot.ScreenshotPath == null)
        {
            return;
        }

        Thumbnail = await Task.Run(() =>
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.DecodePixelWidth = 400;
                image.StreamSource = new MemoryStream(File.ReadAllBytes(Slot.ScreenshotPath));
                image.EndInit();
                image.Freeze();
                return (ImageSource?)image;
            }
            catch (Exception)
            {
                return null;
            }
        });
    }
}

public sealed partial class SaveBrowserViewModel : PageViewModel
{
    private readonly Func<SaveSlot, Task> _open;

    public SaveBrowserViewModel(Func<SaveSlot, Task> open)
    {
        _open = open;
        SlotsView = CollectionViewSource.GetDefaultView(Slots);
        SlotsView.Filter = o => Matches((SaveSlotViewModel)o);
        IsEnabled = true;
    }

    public override string Title => "Saves";
    public override string Glyph => "";
    public override bool RequiresSave => false;

    public string Root { get; private set; } = SaveLibrary.DefaultRoot;

    public ObservableCollection<SaveSlotViewModel> Slots { get; } = new();
    public ICollectionView SlotsView { get; }

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _summary = "";

    partial void OnFilterChanged(string value) => SlotsView.Refresh();

    private bool Matches(SaveSlotViewModel s)
    {
        if (string.IsNullOrWhiteSpace(Filter))
        {
            return true;
        }
        var f = Filter.Trim();
        return s.Slot.FolderName.Contains(f, StringComparison.OrdinalIgnoreCase)
               || s.Quest.Contains(f, StringComparison.OrdinalIgnoreCase)
               || s.Title.Contains(f, StringComparison.OrdinalIgnoreCase)
               || s.Slot.LifePath.Contains(f, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        IsScanning = true;
        try
        {
            var slots = await SaveLibrary.ScanAsync(Root);
            Slots.Clear();
            foreach (var slot in slots)
            {
                Slots.Add(new SaveSlotViewModel(slot));
            }
            Summary = Slots.Count == 0 ? $"No saves found in {Root}" : $"{Slots.Count} saves  ·  {Root}";

            foreach (var slot in Slots)
            {
                _ = slot.LoadThumbnailAsync();
            }
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private Task OpenAsync(SaveSlotViewModel? slot) => slot == null ? Task.CompletedTask : _open(slot.Slot);

    [RelayCommand]
    private void OpenFolder() => System.Diagnostics.Process.Start("explorer.exe", Root);
}
