using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace FamicomPlayer;

// Match the neutral plastic palette at composition time; preserve the private Forge originals.
public sealed class HardwareFinishEffect : ShaderEffect
{
    private static readonly PixelShader shader = LoadShader();
    private static PixelShader LoadShader()
    {
        var result = new PixelShader();
        using var stream = BundledAssets.Open("HardwareFinish.ps");
        result.SetStreamSource(stream); result.Freeze(); return result;
    }
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty(nameof(Input), typeof(HardwareFinishEffect), 0);
    public static readonly DependencyProperty GainProperty = DependencyProperty.Register(nameof(Gain), typeof(double), typeof(HardwareFinishEffect), new UIPropertyMetadata(1d, PixelShaderConstantCallback(0)));
    public static readonly DependencyProperty LiftProperty = DependencyProperty.Register(nameof(Lift), typeof(double), typeof(HardwareFinishEffect), new UIPropertyMetadata(0d, PixelShaderConstantCallback(1)));
    public Brush Input { get => (Brush)GetValue(InputProperty); set => SetValue(InputProperty, value); }
    public double Gain { get => (double)GetValue(GainProperty); set => SetValue(GainProperty, value); }
    public double Lift { get => (double)GetValue(LiftProperty); set => SetValue(LiftProperty, value); }
    public HardwareFinishEffect()
    {
        PixelShader = shader;
        UpdateShaderValue(InputProperty); UpdateShaderValue(GainProperty); UpdateShaderValue(LiftProperty);
    }
}
