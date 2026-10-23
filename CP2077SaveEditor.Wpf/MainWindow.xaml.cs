using System.Windows;

namespace CP2077SaveEditor.Wpf;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) => BorderThickness = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        Closing += (_, e) => e.Cancel = DataContext is ViewModels.MainViewModel vm && !vm.ConfirmDiscardChanges();
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
