using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace CP2077SaveEditor.Wpf.Views;

public partial class ImagePreviewWindow : Window
{
    private ImagePreviewWindow(BitmapSource image, string title, string? subtitle)
    {
        InitializeComponent();
        TitleText.Text = title;
        SubtitleText.Text = subtitle ?? "";
        PreviewImage.Source = image;
        // icons are at most 320 px wide: show them at twice their size
        PreviewImage.Width = image.PixelWidth * 2;
        PreviewImage.Height = image.PixelHeight * 2;
    }

    /// <summary>Shows an item icon enlarged over the given window.</summary>
    public static void Show(Window? owner, BitmapSource? image, string title, string? subtitle = null)
    {
        if (image == null)
        {
            return;
        }
        new ImagePreviewWindow(image, title, subtitle) { Owner = owner ?? Application.Current.MainWindow }.ShowDialog();
    }

    private void Close_Click(object sender, MouseButtonEventArgs e) => Close();

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Space or Key.Enter)
        {
            Close();
        }
    }
}
