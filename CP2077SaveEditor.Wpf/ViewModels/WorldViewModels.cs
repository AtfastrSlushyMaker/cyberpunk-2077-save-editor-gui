using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CP2077SaveEditor.Editing;
using CP2077SaveEditor.Utils;
using CP2077SaveEditor.Wpf.Services;
using Microsoft.Win32;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>Base for screens with a search box over a list.</summary>
public abstract partial class ListPageViewModel<TRow> : PageViewModel where TRow : class
{
    protected EditorSession? Session;

    protected ListPageViewModel()
    {
        RowsView = CollectionViewSource.GetDefaultView(Rows);
        RowsView.Filter = o => string.IsNullOrWhiteSpace(Search) || Matches((TRow)o, Search.Trim());
    }

    public ObservableCollection<TRow> Rows { get; } = new();
    public ICollectionView RowsView { get; }

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private string? _notice;

    partial void OnSearchChanged(string value) => RefreshView();

    protected abstract bool Matches(TRow row, string search);

    protected void RefreshView()
    {
        RowsView.Refresh();
        var shown = Rows.Count(r => string.IsNullOrWhiteSpace(Search) || Matches(r, Search.Trim()));
        Summary = shown == Rows.Count ? $"{Rows.Count} entries" : $"{shown} of {Rows.Count} entries";
    }

    public override void OnSessionChanged(EditorSession? session)
    {
        base.OnSessionChanged(session);
        Session = session;
        Rows.Clear();
        Notice = null;
        if (session != null)
        {
            try
            {
                Load(session);
            }
            catch (Exception e)
            {
                Notice = "This data could not be read from the save: " + e.Message;
            }
        }
        RefreshView();
    }

    protected abstract void Load(EditorSession session);

    protected void Changed() => Session?.MarkChanged();
}

// ---------------- Appearance ----------------

public sealed partial class AppearanceEntryRow : ObservableObject
{
    public AppearanceEntryRow(AppearanceEntry entry)
    {
        Entry = entry;
        Reset();
    }

    public AppearanceEntry Entry { get; }
    public string Key => Entry.Key;

    [ObservableProperty] private string _definition = "";
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _resourcePath = "";

    public void Reset()
    {
        Definition = Entry.Entry.Definition.GetResolvedText() ?? "";
        Name = Entry.Entry.Name.GetResolvedText() ?? "";
        ResourcePath = Entry.Entry.Resource.DepotPath.GetResolvedText() ?? "";
    }
}

public sealed partial class AppearanceViewModel : ListPageViewModel<AppearanceEntryRow>
{
    public override string Title => "Appearance";
    public override string Glyph => "";

    [ObservableProperty] private bool _isBodyMale;
    [ObservableProperty] private bool _isVoiceMale;
    [ObservableProperty] private AppearanceEntryRow? _selectedEntry;

    protected override bool Matches(AppearanceEntryRow row, string search) => row.Key.Contains(search, StringComparison.OrdinalIgnoreCase);

    protected override void Load(EditorSession session)
    {
        IsBodyMale = session.Save.Appearance.BodyGender == AppearanceGender.Male;
        IsVoiceMale = session.Save.Appearance.VoiceTone == AppearanceGender.Male;
        foreach (var entry in AppearanceEditor.Entries(session.Save))
        {
            Rows.Add(new AppearanceEntryRow(entry));
        }
    }

    private void Reload()
    {
        var session = Session;
        OnSessionChanged(session);
    }

    [RelayCommand]
    private void SetBodyGender(string gender)
    {
        if (Session == null) return;
        var target = gender == "Male" ? AppearanceGender.Male : AppearanceGender.Female;
        if (Session.Save.Appearance.BodyGender == target) return;
        Session.Save.Appearance.BodyGender = target; // asks for confirmation (resets appearance to default)
        if (Session.Save.Appearance.BodyGender == target) Changed();
        Reload();
    }

    [RelayCommand]
    private void SetVoiceTone(string gender)
    {
        if (Session == null) return;
        var target = gender == "Male" ? AppearanceGender.Male : AppearanceGender.Female;
        if (Session.Save.Appearance.VoiceTone == target) return;
        Session.Save.Appearance.VoiceTone = target; // asks for confirmation
        if (Session.Save.Appearance.VoiceTone == target) Changed();
        Reload();
    }

    [RelayCommand]
    private void SavePreset()
    {
        if (Session == null) return;
        var dialog = new SaveFileDialog { Filter = "Character preset|*" + AppearanceEditor.PresetExtension, FileName = Session.Slot.FolderName };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, AppearanceEditor.SavePreset(Session.Save));
            Notice = "Preset saved to " + dialog.FileName;
        }
    }

    [RelayCommand]
    private void LoadPreset()
    {
        if (Session == null) return;
        var dialog = new OpenFileDialog
        {
            Filter = $"Character preset|*{AppearanceEditor.PresetExtension};*{AppearanceEditor.LegacyPresetExtension}"
        };
        if (dialog.ShowDialog() != true) return;
        try
        {
            AppearanceEditor.LoadPreset(Session.Save, File.ReadAllText(dialog.FileName), Path.GetExtension(dialog.FileName));
            Changed();
            Reload();
            Notice = "Preset loaded: " + Path.GetFileName(dialog.FileName);
        }
        catch (Exception e)
        {
            UserPrompt.Notice("The preset could not be loaded: " + e.Message, "Load preset");
        }
    }

    [RelayCommand]
    private void ApplyEntry()
    {
        if (SelectedEntry is not { } row) return;
        if (AppearanceEditor.Apply(row.Entry, row.Definition, row.Name, row.ResourcePath))
        {
            Changed();
        }
    }

    [RelayCommand]
    private void ResetEntry() => SelectedEntry?.Reset();
}

// ---------------- Vehicles ----------------

public sealed partial class VehicleRow : ObservableObject
{
    private readonly VehiclesViewModel _owner;

    public VehicleRow(VehiclesViewModel owner, VehicleEntry entry)
    {
        _owner = owner;
        Record = entry.Record;
        DisplayName = entry.DisplayName;
        _unlocked = entry.Unlocked;
    }

    public string Record { get; }
    public string DisplayName { get; }

    [ObservableProperty] private bool _unlocked;

    partial void OnUnlockedChanged(bool value) => _owner.SetUnlocked(this, value);
}

public sealed partial class VehiclesViewModel : ListPageViewModel<VehicleRow>
{
    private bool _bulk;

    public override string Title => "Vehicles";
    public override string Glyph => "";

    protected override bool Matches(VehicleRow row, string search) =>
        row.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase) || row.Record.Contains(search, StringComparison.OrdinalIgnoreCase);

    protected override void Load(EditorSession session)
    {
        var vehicles = WorldEditor.Vehicles(session.Save);
        if (vehicles == null)
        {
            Notice = "This save has no garage data yet. Unlock at least one vehicle in the game first.";
            return;
        }
        _bulk = true;
        foreach (var v in vehicles) Rows.Add(new VehicleRow(this, v));
        _bulk = false;
    }

    internal void SetUnlocked(VehicleRow row, bool unlocked)
    {
        if (_bulk || Session == null) return;
        WorldEditor.SetVehicleUnlocked(Session.Save, row.Record, unlocked);
        Changed();
    }

    [RelayCommand]
    private void UnlockAll()
    {
        foreach (var row in Rows.Where(r => !r.Unlocked).ToList()) row.Unlocked = true;
    }
}

// ---------------- Quest facts ----------------

public sealed partial class FactRow : ObservableObject
{
    private readonly QuestFactsViewModel _owner;

    public FactRow(QuestFactsViewModel owner, FactEntryInfo info)
    {
        _owner = owner;
        Info = info;
        _value = info.Entry.Value;
    }

    public FactEntryInfo Info { get; }
    public string Name => Info.Name;
    public bool IsKnown => Info.IsKnown;

    [ObservableProperty] private long _value;

    partial void OnValueChanged(long value)
    {
        if (Info.Entry.Value != (uint)value)
        {
            Info.Entry.Value = (uint)value;
            _owner.MarkChanged();
        }
    }
}

public sealed partial class QuestFactsViewModel : ListPageViewModel<FactRow>
{
    public override string Title => "Quest facts";
    public override string Glyph => "";

    [ObservableProperty] private FactRow? _selectedFact;
    [ObservableProperty] private string _newFactName = "";
    [ObservableProperty] private long _newFactValue = 1;

    protected override bool Matches(FactRow row, string search) => row.Name.Contains(search, StringComparison.OrdinalIgnoreCase);

    protected override void Load(EditorSession session)
    {
        foreach (var fact in WorldEditor.Facts(session.Save).OrderBy(f => !f.IsKnown).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            Rows.Add(new FactRow(this, fact));
        }
    }

    internal void MarkChanged() => Changed();

    private void Reload() => OnSessionChanged(Session);

    [RelayCommand]
    private void RemoveFact()
    {
        if (SelectedFact is not { } row) return;
        WorldEditor.RemoveFact(row.Info);
        Rows.Remove(row);
        Changed();
        RefreshView();
    }

    [RelayCommand]
    private void AddFact()
    {
        if (Session == null || string.IsNullOrWhiteSpace(NewFactName)) return;
        if (!WorldEditor.SetFact(Session.Save, NewFactName.Trim(), (uint)NewFactValue))
        {
            UserPrompt.Notice($"\"{NewFactName}\" is not a known quest fact name.", "Add fact");
            return;
        }
        Changed();
        Search = NewFactName.Trim();
        Reload();
    }

    [RelayCommand]
    private void SecretEnding()
    {
        if (Session == null) return;
        WorldEditor.EnableSecretEnding(Session.Save);
        Changed();
        Reload();
        Notice = "Secret ending enabled (sq032_johnny_friend = 1).";
    }

    [RelayCommand]
    private void AllRomanceable()
    {
        if (Session == null) return;
        WorldEditor.MakeAllRomanceable(Session.Save);
        Changed();
        Reload();
        Notice = "Judy, River, Panam and Kerry are now romanceable.";
    }

    [RelayCommand]
    private void Export()
    {
        if (Session == null) return;
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = Session.Slot.FolderName + "-facts.json" };
        if (dialog.ShowDialog() == true)
        {
            File.WriteAllText(dialog.FileName, WorldEditor.ExportFacts(Session.Save));
            Notice = "Facts exported to " + dialog.FileName;
        }
    }
}

// ---------------- Extras ----------------

public sealed partial class ExtrasViewModel : PageViewModel
{
    private EditorSession? _session;

    public override string Title => "Extras";
    public override string Glyph => "";

    [ObservableProperty] private bool _hasExtraWardrobe;
    [ObservableProperty] private string? _notice;

    // save flags
    private bool _loadingFlags;
    [ObservableProperty] private bool _isModded;
    [ObservableProperty] private string _moddedDetails = "";
    public ObservableCollection<string> AdditionalContent { get; } = new();
    [ObservableProperty] private int _expansionItemCount;

    public override void OnSessionChanged(EditorSession? session)
    {
        base.OnSessionChanged(session);
        _session = session;
        Notice = null;
        HasExtraWardrobe = session != null && WorldEditor.HasExtraWardrobeSlots(session.Save);
        RefreshFlags();
    }

    private void RefreshFlags()
    {
        AdditionalContent.Clear();
        if (_session == null)
        {
            return;
        }
        var state = SaveFlags.ReadModded(_session.Save);
        _loadingFlags = true;
        IsModded = state.IsModded;
        _loadingFlags = false;
        ModdedDetails = $"metadata isModded: {Text(state.MetadataModded)}  ·  mod.io mods listed: {state.ModioModCount}  ·  sav.dat ModdingSystem: {Text(state.SaveDataModded)}";
        foreach (var id in SaveFlags.AdditionalContent(_session.Save))
        {
            AdditionalContent.Add(id);
        }
        ExpansionItemCount = SaveFlags.ExpansionItems(_session.Save).Count;

        static string Text(bool? b) => b == null ? "not present" : b.Value ? "modded" : "clean";
    }

    partial void OnIsModdedChanged(bool value)
    {
        if (_loadingFlags || _session == null) return;
        SaveFlags.SetModded(_session.Save, value);
        _session.MarkChanged();
        RefreshFlags();
        Notice = value ? "Save marked as modded." : "Modded markers cleared (metadata and sav.dat).";
    }

    [RelayCommand]
    private void RemoveContent(string id)
    {
        if (_session == null) return;
        if (!UserPrompt.Confirm($"Remove \"{id}\" from the save's expansion list?\n\nThe expansion's quests and world data stay in the save; " +
                                "the game may refuse to load it or behave oddly. A backup is made when you overwrite.", "Expansion flag"))
        {
            return;
        }
        if (SaveFlags.RemoveAdditionalContent(_session.Save, id))
        {
            _session.MarkChanged();
            Notice = $"\"{id}\" removed from the expansion list.";
        }
        RefreshFlags();
    }

    [RelayCommand]
    private void RemoveExpansionItems()
    {
        if (_session == null || ExpansionItemCount == 0) return;
        if (!UserPrompt.Confirm($"Remove {ExpansionItemCount} Phantom Liberty item(s) from all containers?", "Phantom Liberty items")) return;
        var removed = SaveFlags.RemoveExpansionItems(_session.Save);
        _session.MarkChanged();
        RefreshFlags();
        Notice = $"Removed {removed} Phantom Liberty item(s).";
    }

    private void Run(Func<SaveFileHelper, string> action)
    {
        if (_session == null) return;
        try
        {
            Notice = action(_session.Save);
            _session.MarkChanged();
        }
        catch (Exception e)
        {
            Notice = "Failed: " + e.Message;
        }
    }

    [RelayCommand] private void UnlockFastTravel() => Run(s => $"{WorldEditor.UnlockAllFastTravel(s)} fast travel points unlocked.");
    [RelayCommand] private void MakeVulnerable() => Run(s => $"Removed {WorldEditor.MakePlayerVulnerable(s)} invulnerability override(s).");
    [RelayCommand] private void ClearWardrobeBlacklist() => Run(s => { WorldEditor.ClearWardrobeBlacklist(s); return "Wardrobe blacklist cleared."; });
}
