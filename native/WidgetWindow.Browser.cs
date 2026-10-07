using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Web.WebView2.Core;

namespace FamicomPlayer;

public partial class WidgetWindow
{
    private readonly List<BrowserPopupWindow> browserPopups = new();

    private void ConfigureBrowserNavigation(CoreWebView2 core, BrowserPopupWindow? popup = null)
    {
        core.NewWindowRequested += BrowserNewWindowRequested;
        core.NavigationStarting += (_, e) =>
        {
            if (popup == null) CancelCartridgeInsertion();
            if (BrowserNavigationPolicy.IsAllowed(e.Uri, popup != null))
            {
                if (popup != null) popup.SetStatus(BrowserNavigationPolicy.SafeHost(e.Uri));
                else
                {
                    ClearAmbientReflection();
                    BrowserPageHeading.Text = "YouTube · FamicomPlayer";
                    // A sign-in redirect is user interaction, not playback buffering.
                    if (!YouTubeAddress.IsYouTubePage(e.Uri)) StopPlaybackConnection();
                }
                return;
            }
            e.Cancel = true;
            string host = BrowserNavigationPolicy.SafeHost(e.Uri);
            string message = "페이지 이동을 허용할 수 없습니다" + (host.Length > 0 ? ": " + host : ".");
            // Do not record login URLs: query strings may carry session credentials.
            LogPlayback("navigation-blocked", host);
            if (popup != null) popup.SetStatus(message);
            else { StopPlaybackConnection(); Notice(message); BrowserPageHeading.Text = message; }
        };
        if (popup != null)
        {
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WindowCloseRequested += (_, _) => popup.Dispatcher.BeginInvoke(new Action(() => { if (!popup.IsClosed) popup.Close(); }));
            core.NavigationCompleted += (_, e) =>
            {
                if (!e.IsSuccess && e.WebErrorStatus != CoreWebView2WebErrorStatus.OperationCanceled)
                    popup.SetStatus("로그인 페이지를 열지 못했습니다: " + e.WebErrorStatus);
            };
        }
    }

    private async void BrowserNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        if (sender is not CoreWebView2 opener || closing) return;
        if (!BrowserNavigationPolicy.IsAllowed(e.Uri, true))
        {
            if (e.IsUserInitiated && Uri.TryCreate(e.Uri, UriKind.Absolute, out var external) && external.Scheme == Uri.UriSchemeHttps && external.UserInfo.Length == 0)
            {
                try { Process.Start(new ProcessStartInfo(external.AbsoluteUri) { UseShellExecute = true }); }
                catch (Exception) { Notice("외부 브라우저를 열지 못했습니다."); }
            }
            return;
        }
        // Keep the actual WindowProxy/opener and profile. Navigating the parent here
        // breaks authentication callbacks and strands device-verification pages.
        using var deferral = e.GetDeferral();
        BrowserPopupWindow? popup = null;
        try
        {
            popup = new BrowserPopupWindow(this);
            browserPopups.Add(popup);
            popup.Closed += (_, _) => browserPopups.Remove(popup);
            if (Program.Smoke) { popup.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual; popup.Left = -10000; popup.Top = -10000; popup.ShowActivated = false; }
            popup.Show();
            var options = opener.Environment.CreateCoreWebView2ControllerOptions();
            options.ProfileName = opener.Profile.ProfileName;
            options.IsInPrivateModeEnabled = opener.Profile.IsInPrivateModeEnabled;
            await popup.Browser.EnsureCoreWebView2Async(opener.Environment, options);
            if (popup.IsClosed || closing) return;
            var child = popup.Browser.CoreWebView2;
            child.Settings.AreDevToolsEnabled = Program.Smoke;
            child.Settings.IsStatusBarEnabled = false;
            ConfigureBrowserNavigation(child, popup);
            e.NewWindow = child;
        }
        catch (Exception)
        {
            if (popup is { IsClosed: false }) popup.Close();
            Notice("로그인 창을 열지 못했습니다. 다시 시도해 주세요.");
            BrowserPageHeading.Text = "로그인 창을 열지 못했습니다. 다시 시도해 주세요.";
        }
    }

    private void CloseBrowserPopups()
    {
        foreach (var popup in browserPopups.ToArray()) if (!popup.IsClosed) popup.Close();
    }
}
