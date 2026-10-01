// Two-tone gradient skybox: pale near-white at the horizon line, light blue
// climbing up. The game camera looks down over the field, so the visible sky
// is the band just around/below the horizon - it fades from light blue at the
// top of frame to near-white at the fence line, like a real daytime sky.
Shader "SnakeGame/SkyGradient"
{
    Properties
    {
        _HorizonColor ("Horizon Color (light blue)", Color) = (0.76, 0.87, 0.98, 1)
        _ZenithColor  ("Zenith Color (blue)", Color) = (0.45, 0.68, 0.97, 1)
        _PaleColor    ("Below-Horizon Color (near white)", Color) = (0.94, 0.97, 1.0, 1)
        _GradientPower ("Gradient Power", Range(0.1, 3)) = 0.8
    }
    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" }
        Cull Off
        ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _HorizonColor, _ZenithColor, _PaleColor;
            float _GradientPower;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert(appdata_base v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.dir = v.vertex.xyz;   // skybox vertices ARE directions
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);

                // above the horizon: light blue -> deeper blue toward zenith
                float t = pow(saturate((d.y + 0.30) / 1.30), _GradientPower);
                fixed4 col = lerp(_HorizonColor, _ZenithColor, t);

                // around/below the horizon: fade to near-white (this is the
                // band the game camera actually sees past the fence)
                float b = saturate((-0.30 - d.y) / 0.40);
                col = lerp(col, _PaleColor, b);

                return col;
            }
            ENDCG
        }
    }
    Fallback Off
}
