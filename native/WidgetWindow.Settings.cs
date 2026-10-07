using System;
using System.Windows;

namespace FamicomPlayer;

public partial class WidgetWindow
{
    private SettingsWindow? settingsWindow;
    private void ApplyVideoOpacity()
    {
        VideoViewbox.Opacity = videoAvailable ? preferences.VideoOpacity / 100 : 0;
        if (!videoAvailable) ClearAmbientReflection();
    }

    private void OpenSettings(object sender, RoutedEventArgs e) { if(e.RoutedEvent!=null)e.Handled=true; ShowSettings(); }

    private void ShowSettings()
    {
        if (settingsWindow != null) { settingsWindow.Activate(); return; }
        libraryWindow?.Close();
        PlaylistPanel.Visibility = Visibility.Collapsed;
        var window = new SettingsWindow { Owner = this, Icon = Icon, Topmost = Topmost };
        window.PackShell.Source=SpriteAssets.Load("library-shell.png",true);
        window.PackPreview.Source=CartridgeImage.Source;
        var area = WorkArea();
        window.Height = Math.Min(window.Height, Math.Max(250, area.Height - 24));
        window.Left = Program.Smoke ? -10000 : area.Left + (area.Width - window.Width) / 2;
        window.Top = Program.Smoke ? -10000 : area.Top + (area.Height - window.Height) / 2;
        window.PinOption.IsChecked = preferences.Pin;
        window.FlickerOption.IsChecked = preferences.Flicker;
        window.ClickSoundOption.IsChecked = preferences.ClickSound;
        window.CrtStrength.Value = preferences.CrtStrength;
        window.CrtValue.Text = $"{preferences.CrtStrength:0}%";
        window.CrtHue.Value=preferences.CrtHue;
        window.CrtSaturation.Value=preferences.CrtSaturation;
        window.CrtWarmth.Value=preferences.CrtWarmth;
        window.HueValue.Text=$"{preferences.CrtHue:+0;-0;0}°";
        window.SaturationValue.Text=$"{preferences.CrtSaturation:0}%";
        window.WarmthValue.Text=$"{preferences.CrtWarmth:0}%";
        window.LibraryButton.Click += OpenLibrary;
        window.RotationOption.IsChecked = preferences.Rotation;
        window.EffectOption.IsChecked = preferences.Effect;
        window.AmbientOption.IsChecked = preferences.Ambient;
        window.VideoOpacity.Value = preferences.VideoOpacity;
        window.OpacityValue.Text = $"{preferences.VideoOpacity:0}%";
        window.LightStrength.Value = preferences.LightStrength;
        window.LightValue.Text = $"{preferences.LightStrength:0}%";
        window.WidgetSize.Value = preferences.Scale * 100;
        window.SizeValue.Text = $"{preferences.Scale * 100:0}%";

        foreach (var toggle in new[] { window.PinOption, window.RotationOption, window.EffectOption, window.AmbientOption, window.FlickerOption, window.ClickSoundOption })
            toggle.Click += (_, _) =>
            {
                preferences.Flicker = window.FlickerOption.IsChecked == true;
                preferences.ClickSound = window.ClickSoundOption.IsChecked == true;
                preferences.Pin = window.PinOption.IsChecked == true;
                preferences.Rotation = window.RotationOption.IsChecked == true;
                preferences.Effect = window.EffectOption.IsChecked == true;
                preferences.Ambient = window.AmbientOption.IsChecked == true;
                ApplyOptions(); Save();
            };
        window.CrtStrength.ValueChanged += (_, _) => { preferences.CrtStrength = Math.Round(window.CrtStrength.Value); window.CrtValue.Text = $"{preferences.CrtStrength:0}%"; ApplyOptions(); Save(); };
        window.CrtHue.ValueChanged+=(_,_)=>{preferences.CrtHue=Math.Round(window.CrtHue.Value);window.HueValue.Text=$"{preferences.CrtHue:+0;-0;0}°";ApplyCrtColor();Save();};
        window.CrtSaturation.ValueChanged+=(_,_)=>{preferences.CrtSaturation=Math.Round(window.CrtSaturation.Value);window.SaturationValue.Text=$"{preferences.CrtSaturation:0}%";ApplyCrtColor();Save();};
        window.CrtWarmth.ValueChanged+=(_,_)=>{preferences.CrtWarmth=Math.Round(window.CrtWarmth.Value);window.WarmthValue.Text=$"{preferences.CrtWarmth:0}%";ApplyCrtColor();Save();};
        window.VideoOpacity.ValueChanged += (_, _) =>
        {
            preferences.VideoOpacity = Math.Round(window.VideoOpacity.Value);
            window.OpacityValue.Text = $"{preferences.VideoOpacity:0}%";
            ApplyVideoOpacity(); Save();
        };
        window.LightStrength.ValueChanged += (_, _) =>
        {
            preferences.LightStrength = Math.Round(window.LightStrength.Value);
            window.LightValue.Text = $"{preferences.LightStrength:0}%";
            Ambient.Opacity = preferences.LightStrength / 100; Save();
        };
        window.WidgetSize.ValueChanged += (_, _) => ChangeWidgetScale(window.WidgetSize.Value / 100);
        window.ResetSize.Click += (_, _) => ChangeWidgetScale(1);
        window.YouTubePageButton.Click += OpenYouTubePage;
        window.DockButton.Click += DockWidget;
        window.HideButton.Click += HideWidget;
        window.ExitButton.Click += CloseWidget;
        window.Closed += (_, _) => settingsWindow = null;
        settingsWindow = window;
        window.ObsOption.IsChecked = preferences.ObsEnabled;
        window.ObsOption.Click += (_, _) => { preferences.ObsEnabled = window.ObsOption.IsChecked == true; Save(); UpdateObsOutput(); };
        window.CopyObsAddress.Click += (_, _) => CopyObsAddress();
        RefreshObsSettings();
        window.AudioOutput.SelectionChanged += async (_, _) =>
        {
            if (updatingOutputs || window.AudioOutput.SelectedItem is not AudioOutputDevice device) return;
            window.AudioOutputStatus.Text = "출력 장치를 적용하는 중…";
            await Execute("window.famicomplayerNative?.setAudioOutput(" + System.Text.Json.JsonSerializer.Serialize(device.Id) + ")");
        };
        window.RefreshOutputsButton.Click += async (_, _) => await RefreshAudioOutputs();
        UpdateAudioOutputList();
        window.Show();
        _ = RefreshAudioOutputs();
        window.VideoOpacity.Focus();
    }
}
