using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CP2077SaveEditor.Wpf.Controls;

/// <summary>Integer stepper clamped to [Minimum, Maximum], with typing, mouse wheel, arrow keys and a fill bar.</summary>
public partial class NumberBox : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(long), typeof(NumberBox),
        new FrameworkPropertyMetadata(0L, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, (d, _) => ((NumberBox)d).Refresh(), Coerce));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(long), typeof(NumberBox), new PropertyMetadata(0L, (d, _) => d.CoerceValue(ValueProperty)));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(long), typeof(NumberBox), new PropertyMetadata(100L, (d, _) => { d.CoerceValue(ValueProperty); ((NumberBox)d).Refresh(); }));

    public static readonly DependencyProperty ShowFillProperty = DependencyProperty.Register(
        nameof(ShowFill), typeof(bool), typeof(NumberBox), new PropertyMetadata(true, (d, _) => ((NumberBox)d).Refresh()));

    public NumberBox()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Refresh();
        Loaded += (_, _) => Refresh();
    }

    public long Value { get => (long)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public long Minimum { get => (long)GetValue(MinimumProperty); set => SetValue(MinimumProperty, value); }
    public long Maximum { get => (long)GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public bool ShowFill { get => (bool)GetValue(ShowFillProperty); set => SetValue(ShowFillProperty, value); }

    private static object Coerce(DependencyObject d, object value)
    {
        var box = (NumberBox)d;
        return Math.Clamp((long)value, box.Minimum, Math.Max(box.Minimum, box.Maximum));
    }

    private void Refresh()
    {
        Input.Text = Value.ToString("N0", CultureInfo.CurrentCulture);
        var range = Maximum - Minimum;
        Fill.Visibility = ShowFill && range > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (range > 0)
        {
            Fill.Width = Math.Max(0, Input.ActualWidth * (Value - Minimum) / range);
        }
    }

    private long Step => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;

    private void Decrement_Click(object sender, RoutedEventArgs e) => Value -= Step;
    private void Increment_Click(object sender, RoutedEventArgs e) => Value += Step;

    private void Commit()
    {
        var text = Input.Text.Replace(",", "").Replace(".", "").Replace(" ", "").Replace(" ", "");
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            Value = parsed;
        }
        Refresh();
    }

    private void Input_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => Commit();

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter: Commit(); e.Handled = true; break;
            case Key.Up: Value += Step; e.Handled = true; break;
            case Key.Down: Value -= Step; e.Handled = true; break;
        }
    }

    private void Input_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Input.IsKeyboardFocused)
        {
            return; // don't hijack page scrolling
        }
        Value += e.Delta > 0 ? Step : -Step;
        e.Handled = true;
    }
}
