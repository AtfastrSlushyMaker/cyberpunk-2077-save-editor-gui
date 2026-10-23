using CommunityToolkit.Mvvm.ComponentModel;
using CP2077SaveEditor.Wpf.Services;

namespace CP2077SaveEditor.Wpf.ViewModels;

/// <summary>A screen reachable from the navigation rail.</summary>
public abstract partial class PageViewModel : ObservableObject
{
    public abstract string Title { get; }

    /// <summary>Segoe Fluent Icons / MDL2 glyph shown in the navigation rail.</summary>
    public abstract string Glyph { get; }

    /// <summary>Pages that edit save data are disabled until a save is open.</summary>
    public virtual bool RequiresSave => true;

    [ObservableProperty]
    private bool _isEnabled;

    /// <summary>Called when a save is opened (session set) or closed (null).</summary>
    public virtual void OnSessionChanged(EditorSession? session)
    {
        IsEnabled = !RequiresSave || session != null;
    }
}
