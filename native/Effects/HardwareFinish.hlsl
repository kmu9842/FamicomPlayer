sampler2D input : register(s0);
float gain : register(c0);
float lift : register(c1);
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 pixel = tex2D(input, uv);
    float shade = clamp(dot(pixel.rgb, float3(.2126, .7152, .0722)) * gain + lift * pixel.a, 0, pixel.a);
    return float4(shade, shade, shade, pixel.a);
}
