using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;

public partial class WidgetWindow
{
    // Capture the real controls using a fresh, anonymous profile and sample library.
    // This path never reads the user's preferences, cookies, or saved cartridges.
    private async Task CaptureGuideScreens()
    {
        string output = Environment.GetEnvironmentVariable("FAMICOMPLAYER_ARTIFACTS")
            ?? Path.Combine(Program.DataDirectory, "screenshots");
        Directory.CreateDirectory(output);
        try
        {
            await browserInitialized.Task;
            preferences.Scale = 1; preferences.Flicker = false; preferences.Pin = false;
            ApplyOptions(); SetSize();
            var single = cartridges.Save(null, "야간 비행 · 음악 한 곡", "https://www.youtube.com/watch?v=2qfoSxRRCJc", null, true);
            cartridges.Save(null, "Me at the zoo", "https://www.youtube.com/watch?v=jNQXAC9IVRw", null, true);
            cartridges.ImportCollection(new PlaylistMetadata("좋아하는 영상 모음", "https://www.youtube.com/playlist?list=PLFamicomGuide001",
                new[] {
                    new VideoMetadata("야간 비행 · 음악 한 곡", single.Url, "2qfoSxRRCJc"),
                    new VideoMetadata("Me at the zoo", "https://www.youtube.com/watch?v=jNQXAC9IVRw", "jNQXAC9IVRw"),
                    new VideoMetadata("Never Gonna Give You Up", "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "dQw4w9WgXcQ")
                }, 0));
            await InsertCartridge(single);
            await Until(() => playing && !cartridgeChanging, "가이드 재생 화면", 60000);
            await Task.Delay(1600);
            CaptureGuideWindow(this, output, "01-player");
            await Execute("window.famicomplayerNative?.pause()");
            await Until(() => !playing, "가이드 일시정지");

            OpenLibrary(this, new RoutedEventArgs());
            var library = libraryWindow!;
            library.Cards.SelectedIndex = 1; // First loose cartridge follows the collection.
            await Task.Delay(1600);
            CaptureGuideWindow(library, output, "02-library");
            library.Cards.SelectedIndex = 0;
            await Task.Delay(400);
            CaptureGuideWindow(library, output, "03-collections");
            library.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            library.Cards.SelectedIndex = 0;
            await Task.Delay(800);
            CaptureGuideWindow(library, output, "04-collection-contents");
            library.Close();

            ShowSettings();
            var settings = settingsWindow!;
            // Device identifiers are local-machine details; select the real generic option.
            settings.AudioOutput.SelectedValue = "";
            await Task.Delay(500);
            CaptureGuideWindow(settings, output, "05-settings-top");
            var scroll = FindGuideChild<ScrollViewer>(settings) ?? throw new Exception("설정 스크롤을 찾지 못했습니다.");
            scroll.ScrollToEnd();
            await Task.Delay(200);
            CaptureGuideWindow(settings, output, "06-settings-bottom");
            settings.Close();

            var dialog = new ThemedDialog(this, "게임팩 삭제", "모음과 안의 모든 게임팩을 삭제할까요?", "삭제")
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,
                new Action(() => { CaptureGuideWindow(dialog, output, "08-delete-confirmation"); dialog.DialogResult = false; }));
            dialog.ShowDialog();

            OpenYouTubePage(this, new RoutedEventArgs());
            await Until(() => pageOpen && !openingPage, "가이드 브라우저");
            Browser.CoreWebView2.Navigate("https://www.youtube.com/?hl=ko");
            await Until(() => Browser.CoreWebView2.Source.Contains("youtube.com/?hl=ko"), "가이드 유튜브 홈");
            for (int attempt = 0; attempt < 50; attempt++)
            {
                if (await Browser.CoreWebView2.ExecuteScriptAsync("document.readyState === 'complete' && !!document.querySelector('ytd-masthead')") == "true") break;
                await Task.Delay(300);
            }
            await Task.Delay(1300);
            CaptureGuideWindow(this, output, "07-browser");
            File.WriteAllText(Path.Combine(output, "capture-result.txt"), "PASS: eight real application screens; fresh anonymous profile; sample library; 2x render resolution.");
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "capture-failure.txt"), error.ToString());
            Application.Current.Shutdown(1);
        }
    }

    private static T? FindGuideChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindGuideChild<T>(child) is T nested) return nested;
        }
        return null;
    }

    private static void CaptureGuideWindow(Window window, string output, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth * 2), (int)Math.Ceiling(window.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
}
