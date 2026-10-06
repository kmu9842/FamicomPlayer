using System;
using System.IO;
using System.Media;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;
public partial class WidgetWindow
{
    private CartridgeStore cartridges = null!;
    private LibraryWindow? libraryWindow;
    private CancellationTokenSource? coverCancellation;
    private double lastVisualFrame;
    private int cartridgeTransitions;
    private SoundPlayer? cartridgePlayer;
    private MemoryStream? clickStream;
    private string currentTitle = "";
    private bool cartridgeChanging;

    private void InitializeCartridges()
    {
        cartridges = new CartridgeStore(Program.DataDirectory);
        HardwareImage.Source = SpriteAssets.Load("console.png");
        TvImage.Source = SpriteAssets.Load("tv.png");
        PadImage.Source = SpriteAssets.Load("pad.png",true);
        VolumeKnobImage.Source = SpriteAssets.Load("volume-knob.png",true);
        CartridgeShell.Source = SpriteAssets.Load("cartridge.png");
        HardwareImage.Effect = new HardwareFinishEffect { Gain = .975, Lift = -.021 };
        ConsoleLip.Effect = new HardwareFinishEffect { Gain = .975, Lift = -.021 };
        PadImage.Effect = new HardwareFinishEffect { Gain = .967, Lift = .016 };
        TvImage.Effect = new HardwareFinishEffect();
        CartridgeShell.Effect = new HardwareFinishEffect();
        VolumeKnobImage.Effect = new HardwareFinishEffect();
        CartridgeImage.Source = LibraryWindow.DefaultCover();
        ResetCartridgeAnimation();
        var saved = cartridges.Entries.Find(e => e.Id == preferences.ActiveCartridgeId);
        if (saved != null) { CartridgeTitle.Text = saved.Name; _ = UpdateCartridgeCover(YouTubeMetadata.VideoId(saved.Url), saved.Name, false); }
        clickStream = MakeClick(); cartridgePlayer = new SoundPlayer(clickStream); cartridgePlayer.Load();
        PreviewKeyDown += (_, e) =>
        {
            if (pageOpen || KeyboardFocusInEditor()) return;
            if (e.Key == System.Windows.Input.Key.Space) { TogglePlayback(this, new RoutedEventArgs()); e.Handled = true; }
            if (e.Key == System.Windows.Input.Key.B) { OpenLibrary(this, new RoutedEventArgs()); e.Handled = true; }
        };
    }
    private bool KeyboardFocusInEditor() => System.Windows.Input.Keyboard.FocusedElement is System.Windows.Controls.TextBox;
    private void VolumeUp(object sender, RoutedEventArgs e) { e.Handled = true; Volume.Value = Math.Min(100, Volume.Value + 5); }
    private void VolumeDown(object sender, RoutedEventArgs e) { e.Handled = true; Volume.Value = Math.Max(0, Volume.Value - 5); }
    private void OpenLibrary(object sender, RoutedEventArgs e)
    {
        if (e.RoutedEvent != null) e.Handled = true;
        if (libraryWindow != null) { libraryWindow.Activate(); return; }
        settingsWindow?.Close(); PlaylistPanel.Visibility = Visibility.Collapsed;
        libraryWindow = new LibraryWindow(cartridges, entry => _ = InsertCartridge(entry), RefreshSavedCartridge, Address.Text, currentTitle) { Owner = this, Icon = Icon, Topmost = Topmost };
        var area = WorkArea(); libraryWindow.Height = Math.Min(libraryWindow.Height, area.Height - 30);
        libraryWindow.Closed += (_, _) => libraryWindow = null;
        if (Program.Smoke) { libraryWindow.WindowStartupLocation = WindowStartupLocation.Manual; libraryWindow.Left = -10000; libraryWindow.Top = -10000; }
        libraryWindow.Show();
    }
    private void RefreshSavedCartridge()
    {
        if (!cartridges.Entries.Exists(e => e.Id == preferences.ActiveCartridgeId)) preferences.ActiveCartridgeId = "";
        Save(); _ = UpdateCartridgeCover(currentVideoId, currentTitle, false);
    }
    private async Task InsertCartridge(CartridgeEntry entry)
    {
        preferences.ActiveCartridgeId = entry.Id; Save(); Address.Text = entry.Url;
        // Show the chosen label immediately even if YouTube is still loading.
        _ = UpdateCartridgeCover(YouTubeMetadata.VideoId(entry.Url), entry.Name);
        await LoadAddress();
    }
    private void UpdateCartridgeState(JsonElement state)
    {
        currentTitle = state.GetProperty("title").GetString() ?? "";
        NowPlaying.Text = currentTitle;
        IdleScreen.Visibility = videoAvailable ? Visibility.Collapsed : Visibility.Visible;
        double time = state.GetProperty("time").GetDouble(), duration = state.GetProperty("duration").GetDouble();
        static string Clock(double v) { var t = TimeSpan.FromSeconds(Math.Max(0, double.IsFinite(v) ? v : 0)); return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss"); }
        TimeDisplay.Text = (playing ? "PLAY" : "PAUSE") + " / " + Clock(time) + " — " + Clock(duration);
        if (state.GetProperty("adPlaying").GetBoolean()) TimeDisplay.Text = "AD / " + Clock(time);
    }
    private async Task UpdateCartridgeCover(string videoId, string title, bool animate = true)
    {
        coverCancellation?.Cancel(); coverCancellation?.Dispose();
        coverCancellation = new CancellationTokenSource(); var token = coverCancellation.Token;
        var saved = cartridges.Entries.Find(e => e.Id == preferences.ActiveCartridgeId);
        BitmapSource image = LibraryWindow.DefaultCover();
        try
        {
            string? custom = saved == null ? null : cartridges.ImagePath(saved);
            if (custom != null && File.Exists(custom))
            {
                using var stream = File.OpenRead(custom); image = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad); image.Freeze();
            }
            else if (videoId.Length == 11)
            {
                try
                {
                    image = await YouTubeMetadata.Thumbnail(videoId, token) ?? image;
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException or NotSupportedException or FileFormatException)
                { token.ThrowIfCancellationRequested(); /* Missing thumbnails keep the local illustrated cover. */ }
            }
            token.ThrowIfCancellationRequested();
            if (animate && preferences.Rotation)
            {
                cartridgeChanging = true;
                var crackle = new DoubleAnimationUsingKeyFrames();
                foreach (var point in new[] { (0, .2), (55, .8), (105, .05), (155, .65), (230, .1), (330, .85), (430, .1), (680, 0d) })
                    crackle.KeyFrames.Add(new DiscreteDoubleKeyFrame(point.Item2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(point.Item1))));
                CartridgeStatic.BeginAnimation(OpacityProperty, crackle);
                TvStatic.BeginAnimation(OpacityProperty, new DoubleAnimation(.23, 0, TimeSpan.FromMilliseconds(650)));
                var lift = new DoubleAnimationUsingKeyFrames();
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(CartridgeLift.Y, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(-22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(220)), new CubicEase { EasingMode = EasingMode.EaseOut }));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(-22, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(365))));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(3, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(465)), new CubicEase { EasingMode = EasingMode.EaseIn }));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(-2, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(515))));
                lift.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(610))));
                CartridgeLift.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, lift);
                await Task.Delay(240, token);
            }
            else ResetCartridgeAnimation();
            token.ThrowIfCancellationRequested(); CartridgeImage.Source = image;
            if(settingsWindow!=null)settingsWindow.PackPreview.Source=image;
            CartridgeTitle.Text = saved?.Name ?? title;
            Cartridge.ToolTip = CartridgeTitle.Text;
            if (animate && preferences.Rotation)
            {
                await Task.Delay(225, token);
                if (preferences.ClickSound && !Program.Smoke && Volume.Value > 0) cartridgePlayer?.Play();
                await Task.Delay(215, token); cartridgeTransitions++; ResetCartridgeAnimation();
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception error) when (error is IOException or NotSupportedException or FileFormatException or InvalidOperationException)
        { if (!token.IsCancellationRequested) { ResetCartridgeAnimation(); CartridgeImage.Source = LibraryWindow.DefaultCover(); Notice("커버 이미지를 읽지 못했습니다. 보관함에서 다시 선택해 주세요."); } }
    }
    private void ResetCartridgeAnimation()
    {
        CartridgeLift.BeginAnimation(TranslateTransform.YProperty, null); CartridgeLift.Y = RestingCartridgeY;
        CartridgeStatic.BeginAnimation(OpacityProperty, null); CartridgeStatic.Opacity = 0;
        TvStatic.BeginAnimation(OpacityProperty, null); TvStatic.Opacity = 0; cartridgeChanging = false;
    }
    private double RestingCartridgeY => playing ? 0 : -14;
    private void UpdateCartridgeSeating()
    {
        if(cartridgeChanging)return;
        double from=CartridgeLift.Y, to=RestingCartridgeY;
        CartridgeLift.BeginAnimation(TranslateTransform.YProperty,null);CartridgeLift.Y=to;
        if(preferences.Rotation && Math.Abs(from-to)>.01)
            CartridgeLift.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(230)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop});
    }
    private static MemoryStream MakeClick()
    {
        const int rate = 22050, count = 2866;
        var stream = new MemoryStream(); using (var writer = new BinaryWriter(stream, System.Text.Encoding.ASCII, true))
        {
            writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + count * 2); writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate*2); writer.Write((short)2); writer.Write((short)16); writer.Write(System.Text.Encoding.ASCII.GetBytes("data")); writer.Write(count*2);
            var random = new Random(83);
            for (int i=0;i<count;i++) { double t=(double)i/rate; double pulse = Math.Exp(-t*85) + (t>.037 ? .7*Math.Exp(-(t-.037)*115) : 0); writer.Write((short)(3100*pulse*(.7*(random.NextDouble()*2-1)+.3*Math.Sin(t*2*Math.PI*165)))); }
        }
        stream.Position = 0; return stream;
    }
}
