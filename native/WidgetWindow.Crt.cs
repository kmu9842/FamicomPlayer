using System;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace FamicomPlayer;
public partial class WidgetWindow
{
    private bool turningVolume;
    private void VolumeDialDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left) return;
        turningVolume=true; Volume.CaptureMouse(); Volume.Focus(); SetDialPosition(e.GetPosition(Volume)); e.Handled=true;
    }
    private void VolumeDialMove(object sender, MouseEventArgs e) { if(turningVolume){SetDialPosition(e.GetPosition(Volume));e.Handled=true;} }
    private void VolumeDialUp(object sender, MouseButtonEventArgs e) { if(turningVolume){turningVolume=false;Volume.ReleaseMouseCapture();e.Handled=true;} }
    private void VolumeDialLostCapture(object sender, MouseEventArgs e) => turningVolume=false;
    private void VolumeDialWheel(object sender, MouseWheelEventArgs e) { Volume.Value=Math.Clamp(Volume.Value+Math.Sign(e.Delta)*3,0,100);e.Handled=true; }
    private void SetDialPosition(Point point)
    {
        double angle=Math.Atan2(point.X-Volume.ActualWidth/2,-(point.Y-Volume.ActualHeight/2))*180/Math.PI;
        if(angle < -150)angle+=360;
        Volume.Value=Math.Round(Math.Clamp((angle+75)/210*100,0,100));
    }
    private void RefreshDial()
    {
        if(VolumeNeedle!=null && Volume!=null)
        {
            double from=VolumeNeedle.Angle,to=-75+Volume.Value*2.1;
            VolumeNeedle.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,null);VolumeNeedle.Angle=to;
            if(initialized)VolumeNeedle.BeginAnimation(System.Windows.Media.RotateTransform.AngleProperty,new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(65)){FillBehavior=FillBehavior.Stop});
        }
        if(Volume!=null)Volume.ToolTip=$"음량 {Volume.Value:0}% · 드래그 / 휠";
    }
    private async void SeekForward(object sender,RoutedEventArgs e) { if(e.RoutedEvent!=null)e.Handled=true;await Execute("window.famicomplayerNative?.seek(10)"); }
    private async void SeekBack(object sender,RoutedEventArgs e) { if(e.RoutedEvent!=null)e.Handled=true;await Execute("window.famicomplayerNative?.seek(-10)"); }
    private string CrtSettingsJson() => JsonSerializer.Serialize(new { enabled=preferences.Effect,strength=preferences.CrtStrength,hue=preferences.CrtHue,saturation=preferences.CrtSaturation,warmth=preferences.CrtWarmth });
    private void ApplyCrtColor() { if(browserReady && !closing) _=Execute("window.famicomplayerNative?.setCrt("+CrtSettingsJson()+")"); }
}
