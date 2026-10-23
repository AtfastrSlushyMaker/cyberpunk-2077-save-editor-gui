using System.Windows.Controls;
using System.Windows.Input;
using CP2077SaveEditor.Wpf.ViewModels;

namespace CP2077SaveEditor.Wpf.Views;

public partial class QuestFactsView : UserControl
{
    public QuestFactsView() => InitializeComponent();

    private void Facts_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Delete && e.OriginalSource is not TextBox && DataContext is QuestFactsViewModel vm)
        {
            vm.RemoveFactCommand.Execute(null);
            e.Handled = true;
        }
    }
}
