using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace FamicomPlayer;

public partial class WidgetWindow
{
    private async Task VerifyBrowserSignInFlow()
    {
        var core = Browser.CoreWebView2;
        const string opener = "https://www.youtube.com/famicomplayer-signin-fixture";
        const string start = "https://accounts.google.com/famicomplayer-signin-fixture";
        const string session = "https://accounts.youtube.com/accounts/famicomplayer-signin-fixture";
        const string sessionGoogle = "https://www.google.com/accounts/famicomplayer-signin-fixture";
        const string finish = "https://www.youtube.com/famicomplayer-signin-fixture-done";
        const string cookieName = "famicomplayer_signin_fixture";
        void InstallFixtures(CoreWebView2 view)
        {
            view.AddWebResourceRequestedFilter("*famicomplayer-signin-fixture*", CoreWebView2WebResourceContext.Document);
            view.WebResourceRequested += (_, e) =>
            {
                string url = e.Request.Uri;
                string? next = url == start ? session : url == session ? sessionGoogle : url == sessionGoogle ? finish : null;
                if (next != null)
                {
                    e.Response = view.Environment.CreateWebResourceResponse(null, 302, "Found", "Location: " + next);
                    return;
                }
                string html;
                if (url == opener)
                    html = "<!doctype html><title>Sign-in fixture</title><script>window.loginCallback=false;addEventListener('message',e=>{if(e.origin==='https://www.youtube.com'&&e.data==='verified')window.loginCallback=true})</script>";
                else if (url == finish)
                    html = "<!doctype html><title>Verified</title><script>document.cookie='" + cookieName + "=complete;path=/;SameSite=Lax;Secure';window.loginFinished=true;if(window.opener){window.opener.postMessage('verified','https://www.youtube.com');window.close()}</script>";
                else return;
                e.Response = view.Environment.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(html)), 200, "OK", "Content-Type: text/html; charset=utf-8");
            };
        }
        InstallFixtures(core);
        try
        {
            // Full-page device confirmation must reach both session-transfer hosts.
            core.Navigate(start);
            await WaitScript("window.loginFinished===true");
            if (core.Source != finish) throw new Exception("Sign-in redirect did not finish inside the existing profile.");
            core.CookieManager.DeleteCookies(cookieName, "https://www.youtube.com");
            core.Navigate(opener);
            await WaitScript("window.loginCallback===false");
            // Blank popups are common: the opener chooses the URL after creating one.
            await core.ExecuteScriptAsync("window.loginPopup=window.open('about:blank','famicomplayer-signin-fixture')");
            await Until(() => browserPopups.Count == 1 && browserPopups[0].Browser.CoreWebView2 != null, "로그인 팝업 생성", 12000);
            var popup = browserPopups[0];
            var child = popup.Browser.CoreWebView2;
            if (child.Profile.ProfileName != core.Profile.ProfileName || child.Profile.IsInPrivateModeEnabled != core.Profile.IsInPrivateModeEnabled)
                throw new Exception("Sign-in popup did not keep its opener's profile.");
            InstallFixtures(child);
            await core.ExecuteScriptAsync("window.loginPopup.location.href=" + JsonSerializer.Serialize(start));
            await WaitScript("window.loginCallback===true");
            await Until(() => browserPopups.Count == 0, "로그인 완료 팝업 닫기", 10000);
            if (core.Source != opener) throw new Exception("Sign-in popup replaced the opener document.");
            var cookies = await core.CookieManager.GetCookiesAsync("https://www.youtube.com");
            if (!cookies.Any(cookie => cookie.Name == cookieName && cookie.Value == "complete"))
                throw new Exception("Sign-in popup did not share its authenticated session with the opener.");
        }
        finally
        {
            CloseBrowserPopups();
            core.CookieManager.DeleteCookies(cookieName, "https://www.youtube.com");
        }
    }

    private async Task BrowserSmokeChecks()
    {
        var output = Environment.GetEnvironmentVariable("FAMICOMPLAYER_ARTIFACTS") ?? Path.Combine(Program.DataDirectory, "artifacts");
        Directory.CreateDirectory(output);
        try
        {
            await Until(() => playing && currentVideoId == "2qfoSxRRCJc", "확장 프로그램 요청으로 앱 실행 및 재생", 60000);
            double firstTime = lastState.GetProperty("time").GetDouble();
            await Until(() => playing && currentVideoId == "2qfoSxRRCJc" && lastState.GetProperty("time").GetDouble() > firstTime + .5,
                "첫 영상 재생 시간 진행", 45000);
            Hide();
            File.WriteAllText(Path.Combine(output, "browser-ready.json"), JsonSerializer.Serialize(new { processId = Environment.ProcessId, hidden = !IsVisible }));
            await Until(() => playing && currentVideoId == "dQw4w9WgXcQ", "실행 중인 앱에 두 번째 재생 요청", 60000);
            if (!IsVisible) throw new Exception("숨겨진 위젯이 복원되지 않았습니다.");
            double secondTime = lastState.GetProperty("time").GetDouble();
            await Until(() => playing && lastState.GetProperty("time").GetDouble() > secondTime + .5, "두 번째 영상 재생 시간 진행");
            Capture("browser-playing");
            File.WriteAllText(Path.Combine(output, "browser-verification.json"), JsonSerializer.Serialize(new {
                success = true, processId = Environment.ProcessId, coldStart = true, existingInstance = true,
                hiddenWidgetRestored = true, playing, url = Browser.CoreWebView2.Source
            }, new JsonSerializerOptions { WriteIndented = true }));
            await Task.Delay(1000);
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "browser-failure.json"), error.ToString());
            Application.Current.Shutdown(1);
        }
    }
}
