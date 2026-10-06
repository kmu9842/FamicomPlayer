using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;
// Transparent parts of a hardware layer must not intercept another device's controls.
public sealed class SpriteImage : Image
{
    private readonly byte[] pixel=new byte[4];
    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
    {
        if(Source is not BitmapSource source || ActualWidth<=0 || ActualHeight<=0)return null;
        var point=parameters.HitPoint;
        int x=(int)(point.X/ActualWidth*source.PixelWidth),y=(int)(point.Y/ActualHeight*source.PixelHeight);
        if(x<0 || y<0 || x>=source.PixelWidth || y>=source.PixelHeight)return null;
        source.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);
        return pixel[3]>20 ? new PointHitTestResult(this,point) : null;
    }
}
