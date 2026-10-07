using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace FamicomPlayer;
public partial class CartridgeDisplay : UserControl
{
    public static readonly DependencyProperty SourceProperty=DependencyProperty.Register(nameof(Source),typeof(ImageSource),typeof(CartridgeDisplay),new PropertyMetadata(null,SourceChanged));
    public ImageSource? Source { get=>(ImageSource?)GetValue(SourceProperty);set=>SetValue(SourceProperty,value); }
    private static void SourceChanged(DependencyObject d,DependencyPropertyChangedEventArgs e){var view=(CartridgeDisplay)d;if(view.Picture!=null)view.Picture.ImageSource=(ImageSource?)e.NewValue??CreateNoiseCover();}
    private static readonly Stopwatch clock=Stopwatch.StartNew();
    private static readonly BitmapSource noiseCover=BuildNoise(false);
    private static readonly BitmapSource flashNoise=BuildNoise(true);
    private static int nextPhase;
    private static bool enabled=true,flicker=true;
    private static event Action? AppearanceChanged;
    private readonly CartridgeCrtEffect crt=new();
    private readonly double noisePhase=(nextPhase++%11)*.37;
    private double lastFrame,lastSize;
    public bool HasCrtEffect => Glass.Effect is CartridgeCrtEffect;
    // A shared, frozen black/noise frame is also used before a thumbnail arrives.
    internal static BitmapSource CreateNoiseCover()=>noiseCover;
    private static BitmapSource BuildNoise(bool bright)
    {
        const int width=240,height=100;
        var pixels=new byte[width*height*4];
        var random=new Random(bright?913:527);
        for(int i=0;i<pixels.Length;i+=4)
        {
            byte value=(byte)(bright?random.Next(40,220):random.Next(2,31));
            pixels[i]=pixels[i+1]=pixels[i+2]=value;pixels[i+3]=255;
        }
        var frame=BitmapSource.Create(width,height,96,96,PixelFormats.Bgra32,null,pixels,width*4);
        frame.Freeze();return frame;
    }
    public static void Configure(bool effectEnabled,bool animate)
    {
        if(enabled==effectEnabled && flicker==animate)return;
        enabled=effectEnabled;flicker=animate;AppearanceChanged?.Invoke();
    }
    public CartridgeDisplay()
    {
        InitializeComponent();
        Picture.ImageSource=Source??CreateNoiseCover();
        var flashBrush=new ImageBrush(flashNoise){Stretch=Stretch.Fill};flashBrush.Freeze();NoiseFlash.Fill=flashBrush;
        SizeChanged+=(_,_)=>UpdateResolution();
        Loaded+=(_,_)=>{AppearanceChanged+=ApplyAppearance;CompositionTarget.Rendering+=Render;ApplyAppearance();UpdateResolution();};
        Unloaded+=(_,_)=>{AppearanceChanged-=ApplyAppearance;CompositionTarget.Rendering-=Render;};
    }
    private void ApplyAppearance()
    {
        Glass.Effect=enabled?crt:null;
        if(!flicker)crt.Time=0;
        if(!enabled || !flicker){NoiseFlash.Opacity=0;WhiteSweep.Opacity=0;}
    }
    private void UpdateResolution()
    {
        if(Glass.ActualWidth<1 || Glass.ActualHeight<1)return;
        Glass.Clip=new RectangleGeometry(new Rect(0,0,Glass.ActualWidth,Glass.ActualHeight),2,2);
        var size=new Size(Glass.ActualWidth,Glass.ActualHeight);
        var window=Window.GetWindow(this);
        if(window!=null && IsLoaded)
        {
            var bounds=Glass.TransformToAncestor(window).TransformBounds(new Rect(size));
            var dpi=VisualTreeHelper.GetDpi(this);size=new Size(bounds.Width*dpi.DpiScaleX,bounds.Height*dpi.DpiScaleY);
        }
        crt.Resolution=new Point(Math.Max(12,size.Width),Math.Max(12,size.Height));
    }
    private void Render(object? sender,EventArgs e)
    {
        if(!IsVisible || !enabled)return;
        double now=clock.Elapsed.TotalSeconds;
        if(now-lastFrame<1d/24)return;lastFrame=now;
        if(flicker)
        {
            crt.Time=now;
            // Alternate a brief double flicker with a thin white tracking line.
            // Both patterns remain subtle enough for thumbnail text to stay readable.
            double phase=(now+noisePhase)%4.8;
            NoiseFlash.Opacity=phase<.10?.17:phase>=.20 && phase<.28?.09:0;
            double sweep=(phase-2)/1.15;
            WhiteSweep.Opacity=sweep>=0 && sweep<=1?.42:0;
            SweepPosition.Y=-3+Math.Clamp(sweep,0,1)*(Glass.ActualHeight+6);
        }
        if(now-lastSize>1){lastSize=now;UpdateResolution();}
    }
}
