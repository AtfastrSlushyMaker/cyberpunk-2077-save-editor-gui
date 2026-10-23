using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CP2077SaveEditor.SaveIO;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>Backs the "Save changes" dialog: what changed, and where to write it.</summary>
public sealed partial class SaveChangesViewModel : ObservableObject
{
    private readonly EditorSession _session;
    private byte[]? _serialized;

    public SaveChangesViewModel(EditorSession session, string savesRoot)
    {
        _session = session;
        SavesRoot = savesRoot;
        NewSlotName = SaveExporter.NextManualSlotName(savesRoot);
    }

    public string SavesRoot { get; }
    public string NewSlotName { get; }
    public string CurrentSlotName => _session.Slot.FolderName;
    public string BackupRoot => SaveExporter.BackupRoot;

    [ObservableProperty]
    private bool _saveAsNewSlot = true;

    [ObservableProperty]
    private bool _isAnalyzing = true;

    [ObservableProperty]
    private IReadOnlyList<string> _changes = Array.Empty<string>();

    [ObservableProperty]
    private string _changesSummary = "Checking what changed…";

    /// <summary>Serializes the save and compares it with the original. Must finish before <see cref="WriteAsync"/>.</summary>
    public async Task AnalyzeAsync()
    {
        try
        {
            var (bytes, changes) = await Task.Run(() =>
            {
                var data = SaveExporter.Serialize(_session.Save);
                return (data, SaveDiff.Compare(_session.OriginalBytes, data));
            });
            _serialized = bytes;
            OnPropertyChanged(nameof(CanWrite));
            var labels = changes.Select(c => c.Label).Distinct().ToList();
            var metadata = _session.Save.Metadata;
            if (metadata != null && _session.OriginalMetadata != null && !metadata.AsSpan().SequenceEqual(_session.OriginalMetadata))
            {
                labels.Add("Save flags (metadata.9.json)");
            }
            Changes = labels;
            ChangesSummary = Changes.Count == 0
                ? "No changes yet. Saving will write an identical copy."
                : $"{Changes.Count} part{(Changes.Count == 1 ? "" : "s")} of the save will change:";
        }
        catch (Exception e)
        {
            ChangesSummary = "Could not prepare the save: " + e.Message;
        }
        finally
        {
            IsAnalyzing = false;
        }
    }

    public bool CanWrite => _serialized != null;

    /// <summary>Writes the save; returns the slot folder written to and the backup folder (if any).</summary>
    public Task<(string Folder, string? Backup)> WriteAsync() => Task.Run(() =>
    {
        var bytes = _serialized ?? throw new InvalidOperationException("Save was not prepared");
        string folder;
        string? backup = null;
        if (SaveAsNewSlot)
        {
            folder = Path.Combine(SavesRoot, NewSlotName);
            if (Directory.Exists(folder))
            {
                throw new IOException($"{NewSlotName} already exists");
            }
        }
        else
        {
            folder = _session.Slot.Folder;
            backup = SaveExporter.Backup(folder);
        }

        SaveExporter.WriteSlot(folder, bytes, _session.Save.Metadata, _session.Save.ImageData);
        _session.OriginalBytes = bytes;
        _session.OriginalMetadata = _session.Save.Metadata?.ToArray();
        return (folder, backup);
    });
}
