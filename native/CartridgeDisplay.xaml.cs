using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FamicomPlayer;
public partial class CartridgeDisplay : UserControl
{
    public static readonly DependencyProperty SourceProperty=DependencyProperty.Register(nameof(Source),typeof(ImageSource),typeof(CartridgeDisplay),new PropertyMetadata(null,SourceChanged));
    public ImageSource? Source { get=>(ImageSource?)GetValue(SourceProperty);set=>SetValue(SourceProperty,value); }
    private static void SourceChanged(DependencyObject d,DependencyPropertyChangedEventArgs e){var view=(CartridgeDisplay)d;if(view.Picture!=null)view.Picture.Source=(ImageSource?)e.NewValue;}
    private static readonly Stopwatch clock=Stopwatch.StartNew();
    private static bool enabled=true,flicker=true;
    private static event Action? AppearanceChanged;
    private readonly CartridgeCrtEffect crt=new();
    private double lastFrame,lastSize;
    public bool HasCrtEffect => Glass.Effect is CartridgeCrtEffect;
    public static void Configure(bool effectEnabled,bool animate)
    {
        if(enabled==effectEnabled && flicker==animate)return;
        enabled=effectEnabled;flicker=animate;AppearanceChanged?.Invoke();
    }
    public CartridgeDisplay()
    {
        InitializeComponent();
        SizeChanged+=(_,_)=>UpdateResolution();
        Loaded+=(_,_)=>{AppearanceChanged+=ApplyAppearance;CompositionTarget.Rendering+=Render;ApplyAppearance();UpdateResolution();};
        Unloaded+=(_,_)=>{AppearanceChanged-=ApplyAppearance;CompositionTarget.Rendering-=Render;};
    }
    private void ApplyAppearance(){Glass.Effect=enabled?crt:null;if(!flicker)crt.Time=0;}
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
        if(flicker)crt.Time=now;
        if(now-lastSize>1){lastSize=now;UpdateResolution();}
    }
}
