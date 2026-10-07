using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Forms = System.Windows.Forms;

namespace FamicomPlayer;

public partial class WidgetWindow : Window
{
    private sealed class Preferences
    {
        public string Url { get; set; } = "";
        public double Volume { get; set; } = 65;
        public double VideoOpacity { get; set; } = 100;
        public bool Captions { get; set; }
        public bool Pin { get; set; } = true;
        public bool Rotation { get; set; } = true;
        public bool Flicker { get; set; } = true;
        public bool ClickSound { get; set; } = true;
        public double CrtStrength { get; set; } = 68;
        public double CrtHue { get; set; } = -4;
        public double CrtSaturation { get; set; } = 86;
        public double CrtWarmth { get; set; } = 14;
        public int VisualRevision { get; set; }
        public string ActiveCartridgeId { get; set; } = "";
        public bool Effect { get; set; } = true;
        public bool Ambient { get; set; } = true;
        public double LightStrength { get; set; } = 75;
        public int Size { get; set; } = 1;
        public double Scale { get; set; }
        public double? Left { get; set; }
        public double? Top { get; set; }
        public string OutputDevice { get; set; } = "";
        public bool ObsEnabled { get; set; }
        public string ObsToken { get; set; } = "";
    }

    private readonly Preferences preferences;
    private readonly Forms.NotifyIcon tray;
    private readonly Stopwatch animationClock = Stopwatch.StartNew();
    private readonly DispatcherTimer visualTimer;
    private bool initialized, browserReady, playing, sampling, closing, videoAvailable;
    private bool ambientLayer;
    private int ambientFrames, ambientRevision;
    private bool pageOpen, openingPage, closePageRequested;
    private Rect widgetBounds;
    private string lastUrl = "", lastError = "";
    private string currentVideoId = "", currentVideoUrl = "";
    private readonly Stack<string> previousVideos = new();
    private bool navigatingBack, coverNeedsRefresh;
    public sealed record PlaylistEntry(string Number, string Title, string Url, string VideoId);
    private List<PlaylistEntry> playlist = new();
    private bool updatingPlaylist;
    private readonly System.Drawing.Icon appTrayIcon;
    private JsonElement lastState = JsonSerializer.SerializeToElement(new { });
    private readonly TaskCompletionSource browserInitialized = new();
    private string PreferencesPath => Path.Combine(Program.DataDirectory, "preferences.json");

    public WidgetWindow()
    {
        try { preferences = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(PreferencesPath)) ?? new(); }
        catch { preferences = new(); }
        preferences.Volume = Math.Clamp(preferences.Volume, 0, 100);
        preferences.VideoOpacity = Math.Clamp(preferences.VideoOpacity, 0, 100);
        preferences.LightStrength = Math.Clamp(preferences.LightStrength, 0, 100);
        preferences.CrtStrength = Math.Clamp(preferences.CrtStrength, 0, 100);
        if(preferences.VisualRevision<2)
        {
            if(preferences.CrtStrength==55)preferences.CrtStrength=68;
            if(preferences.VideoOpacity==90)preferences.VideoOpacity=100;
            preferences.VisualRevision=2;
        }
        preferences.CrtHue=Math.Clamp(preferences.CrtHue,-30,30);
        preferences.CrtSaturation=Math.Clamp(preferences.CrtSaturation,40,140);
        preferences.CrtWarmth=Math.Clamp(preferences.CrtWarmth,0,40);
        preferences.Size = Math.Clamp(preferences.Size, 0, 2);
        if(preferences.VisualRevision<4){preferences.Size=1;preferences.VisualRevision=4;}
        if (!double.IsFinite(preferences.Scale) || preferences.Scale <= 0)
            preferences.Scale = new[] { .8, 1, 1.2 }[preferences.Size];
        preferences.Scale = Math.Clamp(preferences.Scale, .5, 3);
        InitializeComponent();
        if (Program.ObsSmoke) Title = "FamicomPlayer verification";
        InitializeCartridges();
        InitializePlaybackSounds();
        using (var iconStream = BundledAssets.Open("App.ico"))
            Icon = BitmapFrame.Create(iconStream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        using (var iconStream = BundledAssets.Open("App.ico"))
        using (var sourceIcon = new System.Drawing.Icon(iconStream, 32, 32))
            appTrayIcon = (System.Drawing.Icon)sourceIcon.Clone();
        Address.Text = preferences.Url;
        Volume.Value = Program.Smoke ? 0 : preferences.Volume;
        RefreshDial();
        CaptionsButton.IsChecked = preferences.Captions;
        initialized = true;
        ApplyOptions(); SetSize();
        if (Program.Smoke) { Left = -10000; Top = -10000; Topmost = false; }
        tray = new Forms.NotifyIcon { Text = "FamicomPlayer", Icon = appTrayIcon, Visible = true };
        tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowWidget);
        tray.ContextMenuStrip = new Forms.ContextMenuStrip();
        tray.ContextMenuStrip.Items.Add("위젯 표시", null, (_, _) => Dispatcher.Invoke(ShowWidget));
        tray.ContextMenuStrip.Items.Add("게임팩 보관함", null, (_, _) => Dispatcher.Invoke(() => OpenLibrary(this, new RoutedEventArgs())));
        tray.ContextMenuStrip.Items.Add("설정", null, (_, _) => Dispatcher.Invoke(ShowSettings));
        tray.ContextMenuStrip.Items.Add("종료", null, (_, _) => Dispatcher.Invoke(() => CloseWidget(this, new RoutedEventArgs())));
        CompositionTarget.Rendering += Animate;
        visualTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        visualTimer.Tick += async (_, _) => await ReflectVideo();
        visualTimer.Start();
        Loaded += async (_, _) =>
        {
            if (!Program.Smoke) Dock();
            if (!Program.Smoke && preferences.Left.HasValue && preferences.Top.HasValue)
            {
                Left = preferences.Left.Value; Top = preferences.Top.Value;
                var area = WorkArea();
                Left = Math.Clamp(Left, area.Left, Math.Max(area.Left, area.Right - Width));
                Top = Math.Clamp(Top, area.Top, Math.Max(area.Top, area.Bottom - Height));
            }
            await InitializeBrowser();
            if (Program.Arguments.Contains("--startup-smoke")) await StartupSmokeChecks();
            else if (Program.ObsSmoke) await ObsSmokeChecks();
            else if (Program.InteractionSmoke) await InteractionSmokeChecks();
            else if (Program.BrowserSmoke) await BrowserSmokeChecks();
            else if (Program.Smoke) await SmokeChecks();
            else
            {
                var link = Program.Arguments.FirstOrDefault(arg => arg.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
                if (link != null) { Address.Text = link; await LoadAddress(); }
            }
        };
        Closed += (_, _) =>
        {
            closing = true; Save(); visualTimer.Stop(); CompositionTarget.Rendering -= Animate;
            DisposePlaybackSounds(); CloseBrowserPopups();
            settingsWindow?.Close(); libraryWindow?.Close(); coverCancellation?.Cancel(); cartridgePlayer?.Dispose(); clickStream?.Dispose(); obsAudio?.Dispose(); Browser.Dispose(); tray.Dispose(); appTrayIcon.Dispose();
        };
        Closing += (_, e) => { if (pageOpen && !closing) { e.Cancel = true; CloseYouTubePage(this, new RoutedEventArgs()); } };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape && !pageOpen) { settingsWindow?.Close(); PlaylistPanel.Visibility = Visibility.Collapsed; } };
    }

    private async Task InitializeBrowser()
    {
        try
        {
            string browserArguments = "--autoplay-policy=no-user-gesture-required";
            var environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Program.DataDirectory, "WebView2"), new CoreWebView2EnvironmentOptions(browserArguments));
            await Browser.EnsureCoreWebView2Async(environment);
            var core = Browser.CoreWebView2;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.AreDevToolsEnabled = Program.Smoke;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            // Grant only output selection, scoped to YouTube; never microphone access.
            try
            {
                await core.CallDevToolsProtocolMethodAsync("Browser.setPermission", "{\"permission\":{\"name\":\"speaker-selection\"},\"setting\":\"granted\",\"origin\":\"https://www.youtube.com\"}");
                outputSelectionAvailable = true;
            }
            catch (Exception error) { LogPlayback("audio-permission", error.Message); }
            ConfigureBrowserNavigation(core);
            core.WebMessageReceived += ReceiveState;
            core.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess)
                {
                    if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled) return;
                    playing = false; StopPlaybackConnection();
                    string message = "유튜브 페이지를 열지 못했습니다: " + e.WebErrorStatus;
                    Notice(message); BrowserPageHeading.Text = message; return;
                }
                await Execute("window.famicomplayerNative?.setVolume(" + Volume.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "); window.famicomplayerNative?.setWidgetMode(" + (!pageOpen ? "true" : "false") + ")");
                await Execute("window.famicomplayerNative?.setCaptions(" + (preferences.Captions ? "true" : "false") + ")");
                ApplyCrtColor();
                await Execute("window.famicomplayerNative?.setAudioOutput(" + JsonSerializer.Serialize(preferences.OutputDevice) + ");window.famicomplayerNative?.listAudioOutputs()");
                if (!YouTubeAddress.IsYouTubePage(core.Source)) { StopPlaybackConnection(); Notice("설정의 ‘유튜브 페이지 보기’에서 로그인 또는 동의를 진행하세요."); }
            };
            core.ProcessFailed += (_, e) => { playing = false; StopPlaybackConnection(); CancelCartridgeInsertion(); Notice("재생 프로세스가 종료되었습니다. 링크를 다시 실행해 주세요."); };
            var bridge = BundledAssets.ReadText("YouTubeBridge.js");
            await core.AddScriptToExecuteOnDocumentCreatedAsync("window.__famicomplayerGatePlayback=true;window.__famicomplayerInitialVolume=" + Volume.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "; window.__famicomplayerInitialCaptions=" + (preferences.Captions ? "true" : "false") + ";window.__famicomplayerInitialOutputDevice=" + JsonSerializer.Serialize(preferences.OutputDevice) + ";\n" + bridge);
            browserReady = true; browserInitialized.TrySetResult();
            UpdateObsOutput();
        }
        catch (Exception error) { Notice("영상 엔진을 시작하지 못했습니다: " + error.Message); browserInitialized.TrySetException(error); }
    }

    private void ReceiveState(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!YouTubeAddress.IsYouTubePage(e.Source)) return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var state = doc.RootElement;
            string type = state.GetProperty("type").GetString() ?? "";
            if (type == "skip-input") { _ = SkipAdWithInput(); return; }
            if (type == "diagnostic" || type == "bridge-error") { LogPlayback(type, state.ToString()); return; }
            if (type.StartsWith("audio-", StringComparison.Ordinal)) { ReceiveAudioState(state); return; }
            if (type == "notice") { StopPlaybackConnection(); CancelCartridgeInsertion(); Notice(state.GetProperty("message").GetString() ?? ""); return; }
            if (type == "cartridge-ready") { _ = CompleteCartridgeInsertion(state); return; }
            if (type != "state") return;
            lastState = state.Clone();
            bool previouslyPlaying=playing;
            playing = state.GetProperty("playing").GetBoolean();
            UpdateCartridgeInsertionState(state);
            UpdatePlaybackConnection(state);
            if(previouslyPlaying!=playing)UpdateCartridgeSeating();
            bool hasVideo = state.GetProperty("hasVideo").GetBoolean();
            videoAvailable = hasVideo;
            ApplyVideoOpacity();
            Play.ToolTip = playing ? "일시정지" : "재생";
            UpdateCartridgeState(state);
            Previous.IsEnabled = previousVideos.Count > 0 || state.GetProperty("hasPrevious").GetBoolean();
            Next.IsEnabled = state.GetProperty("hasNext").GetBoolean();
            UpdatePlaylist(state);
            bool captionsAvailable = state.GetProperty("captionsAvailable").GetBoolean();
            bool captionsOn = state.GetProperty("captionsEnabled").GetBoolean();
            CaptionsButton.IsChecked = preferences.Captions;
            CaptionsButton.ToolTip = captionsAvailable ? (captionsOn ? "자막 끄기" : "자막 켜기") : "이 영상은 자막을 제공하지 않습니다";
            string error = state.GetProperty("error").GetString() ?? "";
            if (error.Length > 0) Notice(error.Length > 180 ? error[..180] : error);
            else if (hasVideo) Notice("");
            else if (state.GetProperty("needsPage").GetBoolean()) Notice("설정의 ‘유튜브 페이지 보기’에서 안내를 확인해 주세요.");
            string url = state.GetProperty("url").GetString() ?? "";
            string videoId = state.GetProperty("videoId").GetString() ?? "";
            if (hasVideo && videoId.Length == 11 && (videoId != currentVideoId || coverNeedsRefresh) && url.Contains("v=" + videoId))
            {
                if (currentVideoUrl.Length > 0 && !navigatingBack && currentVideoId != videoId) previousVideos.Push(currentVideoUrl);
                coverNeedsRefresh = false; navigatingBack = false; currentVideoId = videoId; currentVideoUrl = url;
                if (!CartridgeInsertionPending) _ = UpdateCartridgeCover(videoId, state.GetProperty("title").GetString() ?? "", false);
                Previous.IsEnabled = previousVideos.Count > 0 || state.GetProperty("hasPrevious").GetBoolean();
            }
            if (url != lastUrl && YouTubeAddress.IsYouTubePage(url))
            {
                lastUrl = url;
                try { preferences.Url = YouTubeAddress.Parse(url).AbsoluteUri; Save(); } catch (ArgumentException) { }
            }
        }
        catch (JsonException) { }
        catch (InvalidOperationException) { }
    }

    internal async Task PlayFromBrowser(string url)
    {
        var uri = YouTubeAddress.Parse(url);
        await browserInitialized.Task.WaitAsync(TimeSpan.FromSeconds(25));
        if (closing) throw new InvalidOperationException("FamicomPlayer가 종료 중입니다. 다시 시도해 주세요.");
        ShowWidget();
        Address.Text = uri.AbsoluteUri;
        preferences.ActiveCartridgeId = "";
        await LoadAddress(true);
    }

    private async Task LoadAddress(bool propagateErrors = false)
    {
        try
        {
            var uri = YouTubeAddress.Parse(Address.Text);
            BeginPlaybackConnection();
            Notice("유튜브를 불러오는 중…");
            await browserInitialized.Task;
            playing = false; videoAvailable = false; ApplyVideoOpacity(); Previous.IsEnabled = Next.IsEnabled = false;
            preferences.Url = uri.AbsoluteUri; coverNeedsRefresh = true; Save();
            Browser.CoreWebView2.Navigate(uri.AbsoluteUri);
            settingsWindow?.Close();
            PlaylistPanel.Visibility = Visibility.Collapsed;
        }
        catch (ArgumentException error) { StopPlaybackConnection(); Notice(error.Message); if (propagateErrors) throw; }
        catch (Exception error) { StopPlaybackConnection(); Notice("재생을 시작하지 못했습니다: " + error.Message); if (propagateErrors) throw; }
    }

    private async Task Execute(string script)
    {
        if (!browserReady || closing) return;
        try { await Browser.CoreWebView2.ExecuteScriptAsync(script); }
        catch (Exception error) when (error is InvalidOperationException || error is System.Runtime.InteropServices.COMException) { StopPlaybackConnection(); CancelCartridgeInsertion(); Notice("재생 페이지가 준비되면 다시 눌러 주세요."); }
    }
    private async void SubmitAddress(object sender, RoutedEventArgs e) { preferences.ActiveCartridgeId = ""; await LoadAddress(); }
    private async void AddressKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; preferences.ActiveCartridgeId = ""; await LoadAddress(); } }
    private async void TogglePlayback(object sender, RoutedEventArgs e)
    {
        if (e.RoutedEvent != null) e.Handled = true;
        if (string.IsNullOrEmpty(lastUrl)) { if (Address.Text.Length > 0) await LoadAddress(); return; }
        bool pause = playing || playbackConnectionPending || CartridgeInsertionPending || cartridgeSeatedForPlayback;
        if (pause) { StopPlaybackConnection(); CancelCartridgeInsertion(); } else BeginPlaybackConnection();
        await Execute("window.famicomplayerNative?." + (pause ? "pause" : "play") + "()");
    }
    private async void PreviousTrack(object sender, RoutedEventArgs e)
    {
        BeginPlaybackConnection();
        if (previousVideos.TryPop(out string? previous)) { navigatingBack = true; playing = false; Browser.CoreWebView2.Navigate(previous); }
        else await Execute("window.famicomplayerNative?.previous()");
    }
    private async void NextTrack(object sender, RoutedEventArgs e) { BeginPlaybackConnection(); await Execute("window.famicomplayerNative?.next()"); }
    private async void CaptionsChanged(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (!initialized) return;
        preferences.Captions = CaptionsButton.IsChecked == true; Save();
        await Execute("window.famicomplayerNative?.setCaptions(" + (preferences.Captions ? "true" : "false") + ")");
    }
    private void UpdatePlaylist(JsonElement state)
    {
        var entries = new List<PlaylistEntry>();
        int selected = -1;
        foreach (var track in state.GetProperty("tracks").EnumerateArray())
        {
            try
            {
                var url = YouTubeAddress.Parse(track.GetProperty("url").GetString() ?? "").AbsoluteUri;
                entries.Add(new PlaylistEntry(track.GetProperty("number").GetString() ?? "", track.GetProperty("title").GetString() ?? "", url, track.GetProperty("videoId").GetString() ?? ""));
                if (track.GetProperty("selected").GetBoolean()) selected = entries.Count - 1;
            }
            catch (ArgumentException) { }
        }
        if (selected < 0) selected = entries.FindIndex(track => track.VideoId == state.GetProperty("videoId").GetString());
        updatingPlaylist = true;
        try
        {
            if (!playlist.SequenceEqual(entries)) { playlist = entries; PlaylistTracks.ItemsSource = playlist; }
            PlaylistTracks.SelectedIndex = selected;
            PlaylistHeading.Text = "재생목록 · " + playlist.Count;
        }
        finally { updatingPlaylist = false; }
    }
    private void TogglePlaylist(object sender, RoutedEventArgs e)
    {
        if (e.RoutedEvent != null) e.Handled = true;
        settingsWindow?.Close();
        PlaylistPanel.Visibility = PlaylistPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (PlaylistPanel.Visibility == Visibility.Visible && PlaylistTracks.SelectedItem != null)
            PlaylistTracks.ScrollIntoView(PlaylistTracks.SelectedItem);
    }
    private async void SelectPlaylistTrack(object sender, SelectionChangedEventArgs e)
    {
        if (updatingPlaylist || PlaylistTracks.SelectedItem is not PlaylistEntry track) return;
        Address.Text = track.Url;
        await LoadAddress();
    }
    private async void ChangeVolume(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        RefreshDial();
        if (!initialized) return;
        preferences.Volume = Volume.Value; Save();
        await Execute("window.famicomplayerNative?.setVolume(" + Volume.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
    }
    private void Notice(string text)
    {
        lastError = text; StatusText.Text = text; StatusBox.Visibility = text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }
    private void Animate(object? sender, EventArgs e)
    {
        if (!IsVisible || pageOpen) return;
        double now = animationClock.Elapsed.TotalSeconds;
        if (now - lastVisualFrame < 1d / 30) return;
        lastVisualFrame = now;
        double shimmer = preferences.Flicker ? .94 + .012 * Math.Sin(now * 8.1) + .008 * Math.Sin(now * 31.7) : 1;
        if (preferences.Flicker && now % 8.7 > 8.52) shimmer *= .84;
        CartridgeLabel.Opacity = shimmer;
        ScanOffset.Y = (now * 19) % 270 - 25;
    }
    private async Task ReflectVideo()
    {
        if (!playing || !videoAvailable || !preferences.Ambient || sampling || !browserReady || !IsVisible || closing || pageOpen) return;
        sampling = true;
        int revision = ambientRevision;
        try
        {
            using var stream = new MemoryStream();
            await Browser.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            if (revision != ambientRevision || !playing || !videoAvailable || !preferences.Ambient || closing || pageOpen) return;
            stream.Position = 0;
            var image = new BitmapImage(); image.BeginInit(); image.StreamSource = stream; image.CacheOption = BitmapCacheOption.OnLoad; image.DecodePixelWidth = 80; image.EndInit(); image.Freeze();
            var front = ambientLayer ? AmbientA : AmbientB;
            var back = ambientLayer ? AmbientB : AmbientA;
            front.Source = image;
            front.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(300)));
            back.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)));
            ambientLayer = !ambientLayer; ambientFrames++;
        }
        catch (Exception) when (!closing) { }
        finally { sampling = false; }
    }
    private void ClearAmbientReflection()
    {
        ambientRevision++;
        foreach (var layer in new[] { AmbientA, AmbientB })
        {
            layer.BeginAnimation(OpacityProperty, null);
            layer.Opacity = 0;
            layer.Source = null;
        }
    }
    private void ApplyOptions() { Topmost = preferences.Pin; if (settingsWindow != null) settingsWindow.Topmost = preferences.Pin; Film.Visibility = preferences.Effect ? Visibility.Visible : Visibility.Collapsed; Film.Opacity = preferences.CrtStrength / 100; Ambient.Visibility = preferences.Ambient ? Visibility.Visible : Visibility.Collapsed; Ambient.Opacity = preferences.LightStrength / 100; CartridgeDisplay.Configure(preferences.Effect,preferences.Flicker);ApplyCrtColor(); }
    private void SetSize()
    {
        double scale = preferences.Scale;
        WidgetScale.Width = 550 * scale; WidgetScale.Height = 375 * scale;
        if (pageOpen) widgetBounds = new Rect(widgetBounds.Left, widgetBounds.Top, WidgetScale.Width, WidgetScale.Height);
        else { Width = WidgetScale.Width; Height = WidgetScale.Height; }
    }
    private void ChangeWidgetScale(double scale)
    {
        preferences.Scale = Math.Clamp(scale, .5, 3);
        SetSize();
        if (!Program.Smoke && !pageOpen)
        {
            var area = WorkArea();
            Left = Math.Max(area.Left, Math.Min(Left, area.Right - Width));
            Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
            preferences.Left = Left; preferences.Top = Top;
        }
        if (settingsWindow != null)
        {
            settingsWindow.WidgetSize.Value = preferences.Scale * 100;
            settingsWindow.SizeValue.Text = $"{preferences.Scale * 100:0}%";
        }
        Save();
    }
    private void ResizeWidget(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (pageOpen) return;
        ChangeWidgetScale(preferences.Scale + (e.HorizontalChange * 550 + e.VerticalChange * 375) / (550 * 550 + 375 * 375));
        e.Handled = true;
    }
    private void ScaleWithWheel(object sender, MouseWheelEventArgs e)
    {
        if (pageOpen || (Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        ChangeWidgetScale(preferences.Scale + e.Delta / 120d * .05);
        e.Handled = true;
    }
    private Rect WorkArea()
    {
        var display = Forms.Screen.FromHandle(new WindowInteropHelper(this).Handle).WorkingArea;
        var source = PresentationSource.FromVisual(this);
        var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        return new Rect(transform.Transform(new Point(display.Left, display.Top)), transform.Transform(new Point(display.Right, display.Bottom)));
    }
    private void Dock() { var area = WorkArea(); Left = area.Right - Width - 16; Top = Math.Max(area.Top, area.Bottom - Height - 16); }
    private void DockWidget(object sender, RoutedEventArgs e) { Dock(); preferences.Left = Left; preferences.Top = Top; Save(); }
    private void DragWidget(object sender, MouseButtonEventArgs e)
    {
        if (e.Handled || e.ChangedButton != MouseButton.Left) return;
        DependencyObject? target = e.OriginalSource as DependencyObject;
        while (target != null && target != Deck) { if (target is System.Windows.Controls.Primitives.ButtonBase || target is Slider) return; target = VisualTreeHelper.GetParent(target); }
        try { DragMove(); preferences.Left = Left; preferences.Top = Top; Save(); } catch (InvalidOperationException) { }
    }
    private void HideWidget(object sender, RoutedEventArgs e) { settingsWindow?.Close(); Hide(); }
    private void ShowWidget() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void CloseWidget(object sender, RoutedEventArgs e) { closing = true; Close(); }
    private void Save()
    {
        if (!initialized) return;
        try { File.WriteAllText(PreferencesPath + ".tmp", JsonSerializer.Serialize(preferences)); File.Move(PreferencesPath + ".tmp", PreferencesPath, true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    private async void OpenYouTubePage(object sender, RoutedEventArgs e)
    {
        if (e.RoutedEvent != null) e.Handled = true;
        StopPlaybackConnection();
        CancelCartridgeInsertion();
        if (pageOpen) { ShowWidget(); return; }
        if (openingPage || closing) return;
        openingPage = true;
        try
        {
            await browserInitialized.Task;
            if (closing) return;
            settingsWindow?.Close(); PlaylistPanel.Visibility = Visibility.Collapsed;
            widgetBounds = new Rect(Left, Top, Width, Height);
            // Keep the original HWND. WebView2CompositionControl does not reparent
            // its native input controller when moved to a different WPF Window.
            pageOpen = true;
            VideoViewbox.Child = null;
            Browser.Width = double.NaN; Browser.Height = double.NaN;
            Browser.IsHitTestVisible = true;
            BrowserContent.Content = Browser;
            WidgetScale.Visibility = Visibility.Collapsed;
            BrowserPanel.Visibility = Visibility.Visible;
            var area = WorkArea();
            Width = Math.Min(1100, area.Width - 24); Height = Math.Min(760, area.Height - 24);
            if (!Program.Smoke) { Left = area.Left + (area.Width - Width) / 2; Top = area.Top + (area.Height - Height) / 2; }
            ResizeMode = ResizeMode.CanResizeWithGrip; ShowInTaskbar = true;
            UpdateLayout();
            await Execute("window.famicomplayerNative?.setWidgetMode(false)");
            if (string.IsNullOrEmpty(Browser.CoreWebView2.Source) || Browser.CoreWebView2.Source == "about:blank")
                Browser.CoreWebView2.Navigate("https://www.youtube.com/");
            Browser.Focus();
        }
        catch (Exception error) { Notice("유튜브 브라우저를 열지 못했습니다: " + error.Message); }
        finally
        {
            openingPage = false;
            if (closePageRequested) { closePageRequested = false; CloseYouTubePage(this, new RoutedEventArgs()); }
        }
    }
    private async void CloseYouTubePage(object sender, RoutedEventArgs e)
    {
        if (e.RoutedEvent != null) e.Handled = true;
        if (!pageOpen) return;
        if (openingPage) { closePageRequested = true; return; }
        pageOpen = false;
        BrowserContent.Content = null;
        Browser.Width = 960; Browser.Height = 758; VideoViewbox.Child = Browser;
        BrowserPanel.Visibility = Visibility.Collapsed; WidgetScale.Visibility = Visibility.Visible;
        ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
        Left = widgetBounds.Left; Top = widgetBounds.Top; Width = widgetBounds.Width; Height = widgetBounds.Height;
        UpdateLayout();
        if (!closing)
            await Execute("window.famicomplayerNative?.setWidgetMode(true)");
    }
    private void OpenYouTubeHome(object sender, RoutedEventArgs e) { e.Handled = true; if (browserReady) Browser.CoreWebView2.Navigate("https://www.youtube.com/"); }
    private void DragBrowserHeader(object sender, MouseButtonEventArgs e) { if (!e.Handled && e.ChangedButton == MouseButton.Left && e.OriginalSource is not Button) { try { DragMove(); } catch (InvalidOperationException) { } } }

    private void Capture(string name)
    {
        var output = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../artifacts/native"));
        // Smoke output is explicitly supplied by the test runner, outside the user's profile.
        output = Environment.GetEnvironmentVariable("FAMICOMPLAYER_ARTIFACTS") ?? output;
        Directory.CreateDirectory(output);
        var image = new RenderTargetBitmap((int)Math.Ceiling(Width), (int)Math.Ceiling(Height), 96, 96, PixelFormats.Pbgra32);
        image.Render(this); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(Path.Combine(output, name + ".png")); encoder.Save(stream);
    }
    private async Task Until(Func<bool> condition, string description, int timeout = 45000)
    {
        var timer = Stopwatch.StartNew();
        while (!condition()) { if (timer.ElapsedMilliseconds > timeout) throw new Exception(description + ": " + lastError + "; " + lastState); await Task.Delay(300); }
    }
}
