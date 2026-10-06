using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FamicomPlayer;
public sealed class CartridgeCrtEffect : ShaderEffect
{
    private static readonly PixelShader shader=LoadShader();
    private static PixelShader LoadShader()
    {
        var value=new PixelShader();
        using var stream=BundledAssets.Open("CartridgeCrt.ps");
        value.SetStreamSource(stream);value.Freeze();return value;
    }
    public static readonly DependencyProperty InputProperty=RegisterPixelShaderSamplerProperty(nameof(Input),typeof(CartridgeCrtEffect),0);
    public static readonly DependencyProperty TimeProperty=DependencyProperty.Register(nameof(Time),typeof(double),typeof(CartridgeCrtEffect),new UIPropertyMetadata(0d,PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty ResolutionProperty=DependencyProperty.Register(nameof(Resolution),typeof(Point),typeof(CartridgeCrtEffect),new UIPropertyMetadata(new Point(200,110),PixelShaderConstantCallback(1)));
    public static readonly DependencyProperty StrengthProperty=DependencyProperty.Register(nameof(Strength),typeof(double),typeof(CartridgeCrtEffect),new UIPropertyMetadata(1d,PixelShaderConstantCallback(2)));
    public Brush Input { get=>(Brush)GetValue(InputProperty);set=>SetValue(InputProperty,value); }
    public double Time { get=>(double)GetValue(TimeProperty);set=>SetValue(TimeProperty,value); }
    public Point Resolution { get=>(Point)GetValue(ResolutionProperty);set=>SetValue(ResolutionProperty,value); }
    public double Strength { get=>(double)GetValue(StrengthProperty);set=>SetValue(StrengthProperty,value); }
    public CartridgeCrtEffect(){PixelShader=shader;UpdateShaderValue(InputProperty);UpdateShaderValue(TimeProperty);UpdateShaderValue(ResolutionProperty);UpdateShaderValue(StrengthProperty);}
}
