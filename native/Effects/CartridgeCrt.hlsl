sampler2D Input : register(s0);
float Time : register(c0);
float2 Resolution : register(c1);
float Strength : register(c2);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float2 p=uv*2-1;
    float radius=dot(p,p);
    float2 q=uv+p*radius*.006;
    q.x+=(frac(Time*.73)-.5)*.002;
    float2 shift=float2(1.15/Resolution.x,0)*Strength;
    float4 center=tex2D(Input,saturate(q));
    float3 rgb=float3(tex2D(Input,saturate(q+shift)).r,center.g,tex2D(Input,saturate(q-shift)).b);
    float lum=dot(rgb,float3(.299,.587,.114));
    rgb=lerp(lum.xxx,rgb,.84)*float3(1.02,1.05,.92);
    rgb=(rgb-.5)*1.14+.5;
    float scan=.54+.46*saturate(frac(q.y*Resolution.y*.5)*3);
    float grain=frac((q.x*127.1+q.y*311.7+frac(Time*17))*.173);
    grain=frac(grain*grain*57.11)-.5;
    rgb=rgb*lerp(1,scan,Strength)+grain*.07;
    float roll=saturate(1-abs(frac(q.y-Time*.075)-.5)*13);
    float shade=(1-roll*.14)*saturate(1-radius*.20);
    rgb*=shade*(.965+frac(Time*2.37)*.035);
    return float4(saturate(rgb)*center.a,center.a);
}
