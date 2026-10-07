using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;

namespace FamicomPlayer;

public partial class ThemedDialog : Window
{
    internal ThemedDialog(Window? owner, string title, string message, string confirmText, bool showCancel = true)
    {
        InitializeComponent();
        Title = Heading.Text = title;
        MessageText.Text = message;
        ConfirmButton.Content = confirmText;
        AutomationProperties.SetName(this, title);
        AutomationProperties.SetHelpText(this, message);
        AutomationProperties.SetName(ConfirmButton, confirmText);
        CancelButton.Visibility = showCancel ? Visibility.Visible : Visibility.Collapsed;
        // Enter starts on Cancel for a destructive confirmation; Space/Enter on Delete still accepts.
        CancelButton.IsDefault = showCancel;
        ConfirmButton.IsDefault = !showCancel;
        if (owner?.IsVisible == true)
        {
            Owner = owner;
            Icon = owner.Icon;
            Topmost = owner.Topmost;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }
        else WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(240, workArea.Width - 24));
        MaxHeight = Math.Max(180, workArea.Height - 24);
        ContentRendered += (_, _) => { if (showCancel) CancelButton.Focus(); else ConfirmButton.Focus(); };
    }

    internal static bool Confirm(Window owner, string title, string message, string confirmText = "삭제") =>
        new ThemedDialog(owner, title, message, confirmText).ShowDialog() == true;

    internal static void Notify(Window? owner, string title, string message) =>
        new ThemedDialog(owner, title, message, "확인", false).ShowDialog();

    private void Accept(object sender, RoutedEventArgs e) { e.Handled = true; DialogResult = true; }
    private void Dismiss(object sender, RoutedEventArgs e) { e.Handled = true; DialogResult = false; }
    private void DialogKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        DialogResult = false;
    }
    private void DragHeader(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
