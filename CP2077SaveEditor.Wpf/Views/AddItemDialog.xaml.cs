using System.Windows;
using System.Windows.Input;
using CP2077SaveEditor.Wpf.ViewModels;

namespace CP2077SaveEditor.Wpf.Views;

public partial class AddItemDialog : Window
{
    public AddItemDialog(AddItemViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => SearchBox.Focus();
    }

    private AddItemViewModel ViewModel => (AddItemViewModel)DataContext;

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.CanAdd)
        {
            DialogResult = true;
        }
    }

    private void List_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Add_Click(sender, e);

    private void Preview_Click(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel.SelectedItem is { } row)
        {
            ImagePreviewWindow.Show(this, row.Icon, row.Name, row.Variants[0].Item.Record);
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
