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
            preferences.Scale=1;SetSize();
            preferences.ActiveCartridgeId = ""; preferences.VideoOpacity = 100; preferences.CrtStrength = 68; preferences.CrtHue=-4; preferences.CrtSaturation=86; preferences.CrtWarmth=14; preferences.Effect=true; preferences.Rotation = true; preferences.Flicker = true;
            ApplyOptions(); await Task.Delay(200); UpdateLayout(); Capture("idle");
            VerifyHardwarePalette();
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
            await InsertCartridge(cartridge);
            await Until(() => playing && currentVideoId == "2qfoSxRRCJc", "유튜브 영상 재생", 60000);
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
            Volume.Value = 23; await Until(() => lastState.GetProperty("volume").GetInt32()==23,"볼륨 적용"); Volume.Value=0;
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
            foreach(var control in new FrameworkElement[]{Previous,Next,Play,Volume,SettingsButton})
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
            File.WriteAllText(Path.Combine(output,"verification.json"),JsonSerializer.Serialize(new { success=true, engine="WPF + WebView2", playback=true, pauseResume=true, pausedCartridgeRaised=true, ejectLeverPlayback=true, width=Width,height=Height,cartridgeCrtEverywhere=true,hardwareHitTargets=true,volume=true, tvVolumeDial=true, automaticVideoTitle=true, controllerSettingsHitTarget=true, repeatedSettingsOpen=true, crtHue=true, playlistNavigation=true, savedCoverAcrossTracks=true, cartridgeTransitions, settings=true, browserRoundTrip=true, transparentSprites=true, thumbnail=true },new JsonSerializerOptions { WriteIndented=true }));
            Application.Current.Shutdown(0);
        }
        catch (Exception error)
        {
            Capture("failure");
            File.WriteAllText(Path.Combine(output,"failure.json"),JsonSerializer.Serialize(new {error=error.ToString(),lastState},new JsonSerializerOptions {WriteIndented=true}));
            Application.Current.Shutdown(1);
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
