using System.IO;
using System.Linq;
using System.Threading.Tasks;
using WolvenKit.RED4.Save.IO;

namespace CP2077SaveEditor.Wpf.Services;

/// <summary>The save currently open in the editor.</summary>
public sealed class EditorSession
{
    /// <summary>The slot the save was loaded from or last written to.</summary>
    public required SaveSlot Slot { get; set; }
    public required SaveFileHelper Save { get; init; }

    /// <summary>sav.dat as last read or written, used to summarize changes before saving.</summary>
    public required byte[] OriginalBytes { get; set; }

    /// <summary>metadata.9.json as last read or written (save flags live there).</summary>
    public byte[]? OriginalMetadata { get; set; }

    /// <summary>True after an edit, until the save is written.</summary>
    public bool HasUnsavedChanges { get; private set; }

    public event System.Action? UnsavedChangesChanged;

    /// <summary>Called by screens after they change save data.</summary>
    public void MarkChanged() => SetUnsaved(true);

    public void MarkSaved() => SetUnsaved(false);

    private void SetUnsaved(bool value)
    {
        if (HasUnsavedChanges != value)
        {
            HasUnsavedChanges = value;
            UnsavedChangesChanged?.Invoke();
        }
    }

    public static Task<EditorSession> LoadAsync(SaveSlot slot) => Task.Run(() =>
    {
        var bytes = File.ReadAllBytes(slot.SavePath);
        using var reader = new CyberpunkSaveReader(new MemoryStream(bytes));

        var status = reader.ReadFile(out var save);
        switch (status)
        {
            case EFileReadErrorCodes.NoError:
                break;
            case EFileReadErrorCodes.UnsupportedVersion:
                throw new InvalidDataException($"Saves from game version {slot.GameVersion} are not supported (patch 2.0 or newer required).");
            default:
                throw new InvalidDataException("The file is not a valid Cyberpunk 2077 save.");
        }

        var helper = new SaveFileHelper { SaveFile = save! };
        var metadataPath = Path.Combine(slot.Folder, "metadata.9.json");
        if (File.Exists(metadataPath))
        {
            helper.Metadata = File.ReadAllBytes(metadataPath);
        }
        if (slot.ScreenshotPath != null)
        {
            helper.ImageData = File.ReadAllBytes(slot.ScreenshotPath);
        }

        return new EditorSession { Slot = slot, Save = helper, OriginalBytes = bytes, OriginalMetadata = helper.Metadata?.ToArray() };
    });
}
