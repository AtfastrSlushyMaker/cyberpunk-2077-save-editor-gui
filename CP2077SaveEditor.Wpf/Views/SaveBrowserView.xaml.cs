using System.Windows.Controls;
using System.Windows.Input;
using CP2077SaveEditor.Wpf.ViewModels;

namespace CP2077SaveEditor.Wpf.Views;

public partial class SaveBrowserView : UserControl
{
    public SaveBrowserView() => InitializeComponent();

    private SaveBrowserViewModel ViewModel => (SaveBrowserViewModel)DataContext;

    private void Gallery_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Gallery.SelectedItem is SaveSlotViewModel slot)
        {
            ViewModel.OpenCommand.Execute(slot);
        }
    }

    private void Gallery_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Gallery.SelectedItem is SaveSlotViewModel slot)
        {
            ViewModel.OpenCommand.Execute(slot);
            e.Handled = true;
        }
    }
}
