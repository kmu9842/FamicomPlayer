using System;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Wpf;

namespace FamicomPlayer;

internal sealed class BrowserPopupWindow : Window
{
    internal readonly WebView2 Browser = new();
    internal bool IsClosed { get; private set; }
    private readonly TextBlock status = new() { Margin = new Thickness(12, 8, 12, 8), TextWrapping = TextWrapping.Wrap };

    internal BrowserPopupWindow(Window owner)
    {
        Owner = owner; Title = "YouTube 로그인 · FamicomPlayer";
        Width = 560; Height = 740; MinWidth = 360; MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new DockPanel();
        DockPanel.SetDock(status, Dock.Top); panel.Children.Add(status); panel.Children.Add(Browser); Content = panel;
        Closed += (_, _) => { IsClosed = true; Browser.Dispose(); };
    }

    internal void SetStatus(string message) => status.Text = message;
}
