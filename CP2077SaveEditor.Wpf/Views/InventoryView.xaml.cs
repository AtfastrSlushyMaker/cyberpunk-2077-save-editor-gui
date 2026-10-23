using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using CP2077SaveEditor.Wpf.ViewModels;

namespace CP2077SaveEditor.Wpf.Views;

public partial class InventoryView : UserControl
{
    public InventoryView() => InitializeComponent();

    private void UpgradeMenu_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        button.ContextMenu!.DataContext = DataContext;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void Preview_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is InventoryViewModel { SelectedItem: { } row })
        {
            ImagePreviewWindow.Show(Window.GetWindow(this), row.Icon, row.Name, row.Record);
        }
    }

    private void Items_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && DataContext is InventoryViewModel vm)
        {
            vm.RemoveSelectedCommand.Execute(null);
            e.Handled = true;
        }
    }
}
