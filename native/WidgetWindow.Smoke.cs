using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;
public partial class WidgetWindow
{
    private async Task StartupSmokeChecks()
    {
        string output = Environment.GetEnvironmentVariable("FAMICOMPLAYER_ARTIFACTS") ?? Path.Combine(Program.DataDirectory,"artifacts");
        Directory.CreateDirectory(output);
        try
        {
            Address.Text = "";
            VerifyConnectionSoundState();
            VerifyThemedDialogs(output);
            TogglePlayback(this,new RoutedEventArgs()); // Keyboard/tray calls do not have a RoutedEvent.
            TogglePlaylist(this,new RoutedEventArgs()); TogglePlaylist(this,new RoutedEventArgs());
            OpenLibrary(this,new RoutedEventArgs()); await Task.Delay(150);
            if (libraryWindow?.IsVisible != true) throw new Exception("보관함 열기 실패");
            libraryWindow.Close(); OpenSettings(this,new RoutedEventArgs()); await Task.Delay(150);
            if (settingsWindow?.IsVisible != true) throw new Exception("설정 열기 실패");
            settingsWindow.Close();
            File.WriteAllText(Path.Combine(output,"startup-verification.json"),JsonSerializer.Serialize(new {success=true,browserReady,library=true,settings=true,keyboardAndTrayEventRouting=true}));
            Application.Current.Shutdown(0);
        }
        catch (Exception error) { File.WriteAllText(Path.Combine(output,"startup-failure.txt"),error.ToString()); Application.Current.Shutdown(1); }
    }
    private async Task SmokeChecks()
    {
        string output = Environment.GetEnvironmentVariable("FAMICOMPLAYER_ARTIFACTS") ?? Path.Combine(Program.DataDirectory, "artifacts");
        Directory.CreateDirectory(output);
        try
        {
            File.Delete(Path.Combine(output,"failure.json"));
            await browserInitialized.Task;
            VerifyConnectionSoundState();
            VerifyThemedDialogs(output);
            preferences.Scale=1;SetSize();
            preferences.ActiveCartridgeId = ""; preferences.VideoOpacity = 100; preferences.CrtStrength = 68; preferences.CrtHue=-4; preferences.CrtSaturation=86; preferences.CrtWarmth=14; preferences.Effect=true; preferences.Rotation = true; preferences.Flicker = true;
            ApplyOptions(); await Task.Delay(200); UpdateLayout(); Capture("idle");
            VerifyHardwarePalette();
            await VerifyCollectionsUi(output);
            Background = new SolidColorBrush(Color.FromRgb(36,42,36)); await Task.Delay(100); Capture("idle-on-background"); Background = Brushes.Transparent;
            string customPath = Path.Combine(Program.DataDirectory,"smoke-cover.png");
            using (var input = BundledAssets.Open("default-cover.png")) using (var target = File.Create(customPath)) input.CopyTo(target);
            var metadata=await YouTubeMetadata.Resolve("https://www.youtube.com/watch?v=2qfoSxRRCJc");
            var cartridge = cartridges.Save(null,metadata.Title, "https://www.youtube.com/watch?v=2qfoSxRRCJc&list=RD2qfoSxRRCJc", customPath,false);
            OpenLibrary(this,new RoutedEventArgs());
            libraryWindow!.UrlInput.Text=metadata.Url;
            await Until(()=>libraryWindow?.VideoTitle.Text==metadata.Title && libraryWindow.SaveButton.IsEnabled,"게임팩 제목 자동 가져오기",20000);
            await Task.Delay(600);
            if(!libraryWindow.CoverPreview.HasCrtEffect)throw new Exception("보관함 게임팩 CRT 필터 누락");
            CaptureWindow(libraryWindow!,Path.Combine(output,"library.png")); libraryWindow!.Close();
            int insertionsBeforePlayback = completedPlaybackInsertions;
            await InsertCartridge(cartridge);
            await Until(() => CartridgeInsertionPending, "영상 준비 후 삽입 대기", 60000);
            if (await Browser.CoreWebView2.ExecuteScriptAsync("document.querySelector('video')?.paused") != "true")
                throw new Exception("철컥 완료 전에 영상이 재생됩니다.");
            await Until(() => playing && currentVideoId == "2qfoSxRRCJc", "유튜브 영상 재생", 60000);
            if (completedPlaybackInsertions <= insertionsBeforePlayback) throw new Exception("삽입 완료 승인 없이 영상이 재생됩니다.");
            double time = lastState.GetProperty("time").GetDouble(); await Task.Delay(2200);
            if (lastState.GetProperty("time").GetDouble() < time + .5) throw new Exception("영상 시간이 진행되지 않습니다.");
            await Until(() => cartridgeTransitions > 0 && !cartridgeChanging,"게임팩 교체 완료",15000);
            if (CartridgeTitle.Text != cartridge.Name || CartridgeLift.Y != 0) throw new Exception("저장된 게임팩 적용 실패");
            Capture("playing");
            VerifyCartridgeFilter();
            await WaitScript("getComputedStyle(document.querySelector('video')).filter.includes('hue-rotate')");
            var motion=UpdateCartridgeCover(currentVideoId,currentTitle);
            await Task.Delay(180);Capture("cartridge-lifting");
            await Task.Delay(175);Capture("cartridge-raised");
            await motion;
            Play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(() => !playing,"배출 레버 일시정지");
            await Until(()=>!cartridgeChanging && Math.Abs(CartridgeLift.Y+14)<.01,"정지 상태 게임팩 높이");
            await Task.Delay(700);
            if(Math.Abs(CartridgeLift.Y+14)>.01)throw new Exception("정지 중 게임팩 높이가 유지되지 않음");
            Capture("paused");
            double dialRadius=Volume.ActualHeight*.4;
            SetDialPosition(new Point(Volume.ActualWidth/2+dialRadius*.5,Volume.ActualHeight/2-dialRadius*Math.Sqrt(3)/2));
            await Until(()=>lastState.GetProperty("volume").GetInt32()==50,"TV 음량 다이얼");
            await Task.Delay(100);
            if(Math.Abs(VolumeNeedle.Angle-30)>.001)throw new Exception("음량 다이얼 위치 오류");
            Volume.Value = 23; await Until(() => lastState.GetProperty("volume").GetInt32()==23,"볼륨 적용");
            PadVolumeUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => lastState.GetProperty("volume").GetInt32()==28,"패드 위 방향키 볼륨");
            PadVolumeDown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(() => lastState.GetProperty("volume").GetInt32()==23,"패드 아래 방향키 볼륨"); Volume.Value=0;
            Play.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); await Until(()=>playing && Math.Abs(CartridgeLift.Y)<.01,"레버 재생 및 게임팩 삽입");
            await Until(()=>playlist.Count > 1 && Next.IsEnabled,"재생목록",30000);
            var nextId = currentVideoId; int transitions = cartridgeTransitions;
            NextTrack(this,new RoutedEventArgs());
            await Until(()=>playing && currentVideoId != nextId,"다음 영상",45000);
            await Until(()=>cartridgeTransitions>transitions && !cartridgeChanging,"다음 영상 게임팩 애니메이션",15000);
            if (CartridgeTitle.Text != cartridge.Name) throw new Exception("사용자 커버가 다음 영상에서 손실되었습니다.");
            PreviousTrack(this,new RoutedEventArgs()); await Until(()=>playing && currentVideoId==nextId,"이전 영상",45000);
            var hit=InputHitTest(SettingsButton.TranslatePoint(new Point(SettingsButton.ActualWidth/2,SettingsButton.ActualHeight/2),this)) as DependencyObject;
            while(hit!=null && hit!=SettingsButton)hit=VisualTreeHelper.GetParent(hit);
            if(hit!=SettingsButton)throw new Exception("설정 버튼을 다른 레이어가 가립니다.");
            SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(()=>settingsWindow?.IsVisible==true,"패드 설정 버튼");
            if(!settingsWindow!.PackPreview.HasCrtEffect || !CartridgeImage.HasCrtEffect)throw new Exception("게임팩 CRT 필터 누락");
            foreach(var control in new FrameworkElement[]{Previous,Next,Play,Volume,SettingsButton,PadVolumeUp,PadVolumeDown,CaptionsButton})
            {
                bool available=control.IsEnabled;
                try
                {
                    control.IsEnabled=true;
                    var clicked=InputHitTest(control.TranslatePoint(new Point(control.ActualWidth/2,control.ActualHeight/2),this)) as DependencyObject;
                    while(clicked!=null && clicked!=control)clicked=VisualTreeHelper.GetParent(clicked);
                    if(clicked!=control)throw new Exception("조작 영역 겹침: "+control.Name);
                }
                finally{control.IsEnabled=available;}
            }
            settingsWindow!.CrtStrength.Value=25;
            if (Math.Abs(Film.Opacity-.25)>.001) throw new Exception("CRT 강도가 반영되지 않았습니다.");
            settingsWindow.CrtStrength.Value=68;settingsWindow.CrtHue.Value=12;
            await WaitScript("getComputedStyle(document.querySelector('video')).filter.includes('hue-rotate(8.16deg)')");
            settingsWindow.CrtHue.Value=-4;
            settingsWindow.WidgetSize.Value=137;
            await Task.Delay(120);UpdateLayout();
            // HWND sizes round to physical pixels at the current display DPI.
            if(Math.Abs(Width-753.5)>1 || Math.Abs(Height-513.75)>1)throw new Exception($"연속 크기 조절 오류: {Width} × {Height}");
            Capture("resized");
            settingsWindow.Close();ShowSettings();
            if(Math.Abs(settingsWindow!.WidgetSize.Value-137)>.01)throw new Exception("설정 다시 열 때 크기 손실");
            settingsWindow.ResetSize.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(120);UpdateLayout();
            if(Math.Abs(Width-550)>1 || Math.Abs(Height-375)>1)throw new Exception("기본 크기 복원 실패");
            settingsWindow.UpdateLayout();
            var resetBounds = settingsWindow.ResetSize.TransformToAncestor(settingsWindow).TransformBounds(new Rect(settingsWindow.ResetSize.RenderSize));
            if (resetBounds.Top < 0 || resetBounds.Bottom > settingsWindow.ActualHeight || settingsWindow.ResetSize.ActualHeight < 30)
                throw new Exception("설정 기본 크기 버튼이 잘립니다.");
            CaptureWindow(settingsWindow,Path.Combine(output,"settings.png")); settingsWindow.Close();
            SettingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Until(()=>settingsWindow?.IsVisible==true,"설정 다시 열기");settingsWindow!.Close();
            OpenYouTubePage(this,new RoutedEventArgs()); await Until(()=>pageOpen && !openingPage,"브라우저 열기");
            CloseYouTubePage(this,new RoutedEventArgs()); await Until(()=>!pageOpen && VideoViewbox.Child==Browser,"위젯 복귀");
            await WaitScript("document.documentElement.classList.contains('tt-widget') && document.querySelector('video').getBoundingClientRect().width>=innerWidth-2");
            await Task.Delay(1000);
            if (!AllowsTransparency || WindowStyle != WindowStyle.None || ShowInTaskbar) throw new Exception("위젯 창 속성 실패");
            cartridges.Delete(cartridge.Id);
            preferences.ActiveCartridgeId="";
            await UpdateCartridgeCover(currentVideoId,currentTitle);
            Capture("automatic-thumbnail");
            File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new { success=true, engine="WPF + WebView2", playback=true, pauseResume=true, pausedCartridgeRaised=true, ejectLeverPlayback=true, width=Width,height=Height,cartridgeCrtEverywhere=true,collectionsUi=true,collectionUnpackPreservesPacks=true,bufferingSoundState=true,playbackAfterInsertion=true,completedPlaybackInsertions,themedConfirmations=true,hardwareHitTargets=true,padVolume=true,volume=true, tvVolumeDial=true, automaticVideoTitle=true, controllerSettingsHitTarget=true, repeatedSettingsOpen=true, crtHue=true, playlistNavigation=true, savedCoverAcrossTracks=true, cartridgeTransitions, settings=true, browserRoundTrip=true, transparentSprites=true, thumbnail=true },new JsonSerializerOptions { WriteIndented=true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            Capture("failure");
            File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new {error=error.ToString(),lastState},new JsonSerializerOptions {WriteIndented=true}));
            Application.Current.Shutdown(1);
        }
    }
    private void VerifyConnectionSoundState()
    {
        using var buffering = JsonDocument.Parse("{\"playbackRequested\":true,\"buffering\":true,\"error\":\"\"}");
        using var paused = JsonDocument.Parse("{\"playbackRequested\":false,\"buffering\":false,\"error\":\"\"}");
        using var failed = JsonDocument.Parse("{\"playbackRequested\":true,\"buffering\":true,\"error\":\"unavailable\"}");
        bool wasPlaying = playing;
        try
        {
            playing = false;
            UpdatePlaybackConnection(buffering.RootElement);
            if (!playbackConnectionPending || !connectionSoundTimer.IsEnabled) throw new Exception("Buffering sound was not scheduled.");
            UpdatePlaybackConnection(paused.RootElement);
            if (playbackConnectionPending || connectionSoundTimer.IsEnabled) throw new Exception("Pause did not cancel buffering sound.");
            BeginPlaybackConnection(); UpdatePlaybackConnection(failed.RootElement);
            if (playbackConnectionPending) throw new Exception("Playback failure did not cancel buffering sound.");
            BeginPlaybackConnection(); playing = true; UpdatePlaybackConnection(buffering.RootElement);
            if (playbackConnectionPending) throw new Exception("Playback did not cancel buffering sound.");
        }
        finally { playing = wasPlaying; StopPlaybackConnection(); }
    }
    private void VerifyThemedDialogs(string output)
    {
        foreach (string action in new[] { "cancel", "close", "confirm", "escape" })
        {
            var dialog = new ThemedDialog(this, "게임팩 삭제", "모음과 안의 모든 게임팩을 삭제할까요?", "삭제")
            { WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000, ShowActivated = false };
            Exception? failure = null;
            dialog.Loaded += (_, _) => dialog.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                try
                {
                    if (!dialog.CancelButton.IsDefault) throw new Exception("삭제 확인창의 기본 선택이 취소가 아닙니다.");
                    if (action == "confirm") CaptureWindow(dialog, Path.Combine(output, "delete-confirmation.png"));
                    if (action == "escape")
                        dialog.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice, PresentationSource.FromVisual(dialog), 0, System.Windows.Input.Key.Escape)
                        { RoutedEvent = System.Windows.Input.Keyboard.PreviewKeyDownEvent });
                    else (action == "confirm" ? dialog.ConfirmButton : action == "cancel" ? dialog.CancelButton : dialog.CloseButton).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception error) { failure = error; dialog.DialogResult = false; }
            }));
            bool confirmed = dialog.ShowDialog() == true;
            if (failure != null) throw failure;
            if (confirmed != (action == "confirm")) throw new Exception("삭제 확인창의 승인/취소 동작 오류: " + action);
        }
    }
    private async Task VerifyCollectionsUi(string output)
    {
        LibraryWindow? window = null;
        string marker = Guid.NewGuid().ToString("N");
        var loose = cartridges.Save(null, "개별 게임팩", "https://www.youtube.com/watch?v=2qfoSxRRCJc", null, true);
        var collection = cartridges.ImportCollection(new PlaylistMetadata("게임팩 모음 확인", "https://www.youtube.com/playlist?list=PL" + marker,
            new[] { new VideoMetadata("첫 번째 게임팩", "https://www.youtube.com/watch?v=2qfoSxRRCJc", "2qfoSxRRCJc"), new VideoMetadata("두 번째 게임팩", "https://www.youtube.com/watch?v=jNQXAC9IVRw", "jNQXAC9IVRw") }, 0));
        var members = cartridges.Entries.Where(entry => entry.CollectionId == collection.Id).Select(entry => entry.Id).ToArray();
        try
        {
            window = new LibraryWindow(cartridges, _ => { }, () => { }, "", "") { Owner = this, Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual };
            window.Show(); await Task.Delay(180); window.UpdateLayout();
            int topCount = cartridges.Collections.Count + cartridges.Entries.Count(entry => entry.CollectionId == null);
            if (window.Cards.Items.Count != topCount) throw new Exception("전체 보관함에서 모음과 개별 팩이 함께 표시되지 않음");
            window.Cards.SelectedIndex = cartridges.Collections.FindIndex(item => item.Id == collection.Id);
            window.UpdateLayout();
            if (window.PreviewBackOne.Visibility != Visibility.Visible || window.PreviewBackTwo.Visibility != Visibility.Visible) throw new Exception("모음의 겹쳐진 팩 미리보기 누락");
            CaptureWindow(window, Path.Combine(output, "collections.png"));
            window.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            if (window.Cards.Items.Count != 2 || window.BackButton.Visibility != Visibility.Visible) throw new Exception("모음 열기 실패");
            CaptureWindow(window, Path.Combine(output, "collection-contents.png"));
            window.BackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (window.Cards.Items.Count != topCount) throw new Exception("모음에서 전체 보관함으로 복귀 실패");
            window.Cards.SelectedIndex = cartridges.Collections.FindIndex(item => item.Id == collection.Id);
            window.UnpackButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (cartridges.Collections.Any(item => item.Id == collection.Id) || members.Any(id => !cartridges.Entries.Any(entry => entry.Id == id && entry.CollectionId == null))) throw new Exception("모음 풀기가 게임팩을 보존하지 않음");
        }
        finally
        {
            window?.Close();
            cartridges.DeleteCollection(collection.Id);
            foreach (string id in members) cartridges.Delete(id);
            cartridges.Delete(loose.Id);
        }
    }
    private static void CaptureWindow(Window window,string path)
    {
        window.UpdateLayout(); var bmp=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(window);
        var png=new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bmp)); using var stream=File.Create(path);png.Save(stream);
    }
    private void VerifyCartridgeFilter()
    {
        byte[] Pixels()
        {
            int w=(int)Math.Ceiling(CartridgeImage.ActualWidth),h=(int)Math.Ceiling(CartridgeImage.ActualHeight);
            var bitmap=new RenderTargetBitmap(w,h,96,96,PixelFormats.Pbgra32);bitmap.Render(CartridgeImage);
            var pixels=new byte[w*h*4];bitmap.CopyPixels(pixels,w*4,0);return pixels;
        }
        try
        {
            CartridgeDisplay.Configure(false,false);var plain=Pixels();
            CartridgeDisplay.Configure(true,false);var filtered=Pixels();
            double change=plain.Zip(filtered,(a,b)=>Math.Abs(a-b)).Average();
            if(change<3)throw new Exception("게임팩 CRT 필터가 실제 픽셀에 반영되지 않음: "+change);
        }
        finally{CartridgeDisplay.Configure(preferences.Effect,preferences.Flicker);}
    }
    private void VerifyHardwarePalette()
    {
        double Sample(Image view, Int32Rect patch)
        {
            int width=(int)view.ActualWidth,height=(int)view.ActualHeight;
            var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(view);
            var pixels=new byte[patch.Width*patch.Height*4];bitmap.CopyPixels(patch,pixels,patch.Width*4,0);
            double total=0;
            for(int i=0;i<pixels.Length;i+=4)
            {
                if(Math.Abs(pixels[i]-pixels[i+1])>1 || Math.Abs(pixels[i+1]-pixels[i+2])>1)
                    throw new Exception("하드웨어 회색에 색조가 남아 있음");
                total+=pixels[i];
            }
            return total/(pixels.Length/4);
        }
        double console=Sample(HardwareImage,new Int32Rect(299,287,76,23));
        double pad=Sample(PadImage,new Int32Rect(119,56,122,15));
        if(Math.Abs(console-pad)>6)throw new Exception($"본체/패드 회색 불일치: {console:0.0} / {pad:0.0}");
    }
}
