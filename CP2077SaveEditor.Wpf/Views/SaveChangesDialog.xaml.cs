using System;
using System.Windows;
using System.Windows.Input;
using CP2077SaveEditor.Wpf.ViewModels;

namespace CP2077SaveEditor.Wpf.Views;

public partial class SaveChangesDialog : Window
{
    public SaveChangesDialog(SaveChangesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.AnalyzeAsync();
    }

    private SaveChangesViewModel ViewModel => (SaveChangesViewModel)DataContext;

    /// <summary>Where the save was written, set when the dialog closes with true.</summary>
    public (string Folder, string? Backup) Result { get; private set; }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveButton.IsEnabled = false;
        try
        {
            Result = await ViewModel.WriteAsync();
            DialogResult = true;
        }
        catch (Exception ex)
        {
            UserPrompt.Notice("Saving failed: " + ex.Message, "Error");
            SaveButton.IsEnabled = true;
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();
}
