Shader "SnakeGame/GrassBlades"
{
    Properties
    {
        _ColorBottom ("Bottom Color", Color) = (0.10, 0.30, 0.08, 1)
        _ColorTop ("Tip Color", Color) = (0.34, 0.66, 0.20, 1)
        _ColorPressed ("Pressed Color", Color) = (0.72, 0.66, 0.30, 1)
        _Influence ("Influence Map", 2D) = "black" {}
        _MapExtent ("Map Half Extent", Float) = 15
        _WindDir ("Wind Direction", Vector) = (1, 0, 0.4, 0)
        _WindStrength ("Wind Strength", Float) = 0.07
        _BendAway ("Bend Away Strength", Float) = 0.9
        _Flatten ("Flatten Strength", Float) = 0.7
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Tags { "LightMode" = "ForwardBase" }
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            #include "Lighting.cginc"

            sampler2D _Influence;
            float4 _Influence_TexelSize;
            fixed4 _ColorBottom, _ColorTop, _ColorPressed;
            float _MapExtent, _WindStrength, _BendAway, _Flatten;
            float4 _WindDir;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float val : TEXCOORD1;
                fixed4 color : COLOR;
                UNITY_FOG_COORDS(2)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                float t = saturate(v.uv.y);
                float4 w = mul(unity_ObjectToWorld, v.vertex);

                // ---- snake influence trail: bend away, flatten, spring back ----
                float2 iuv = w.xz / (2.0 * _MapExtent) + 0.5;
                float val = tex2Dlod(_Influence, float4(iuv, 0, 0)).r;
                float texel = _Influence_TexelSize.x;
                float gx = tex2Dlod(_Influence, float4(iuv + float2(texel, 0), 0, 0)).r
                         - tex2Dlod(_Influence, float4(iuv - float2(texel, 0), 0, 0)).r;
                float gz = tex2Dlod(_Influence, float4(iuv + float2(0, texel), 0, 0)).r
                         - tex2Dlod(_Influence, float4(iuv - float2(0, texel), 0, 0)).r;
                // gradient points toward the snake -> subtract to part outward
                w.xz -= float2(gx, gz) * _BendAway * (0.35 + val);
                w.y -= t * t * _Flatten * val;

                // ---- wind sway (per-blade phase packed in vertex color alpha) ----
                float ph = v.color.a * 6.28318;
                float sway = sin(_Time.y * 2.1 + ph + w.x * 0.33 + w.z * 0.21);
                w.xz += normalize(_WindDir.xz + 0.0001) * (sway * _WindStrength * t * t);

                o.pos = mul(UNITY_MATRIX_VP, w);
                o.uv = v.uv;
                o.val = val;
                o.color = v.color;
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float t = saturate(i.uv.y);
                // base gradient bottom->tip with per-blade jitter in vertex color
                fixed3 albedo = lerp(_ColorBottom.rgb, _ColorTop.rgb, t) * i.color.rgb;
                // pressed / parting grass shifts toward a flattened yellow-green
                albedo = lerp(albedo, _ColorPressed.rgb * i.color.rgb, saturate(i.val * 0.9));

                // stylized lighting: half-lambert from the sun + sky ambient
                half ndl = dot(fixed3(0, 1, 0), _WorldSpaceLightPos0.xyz) * 0.5 + 0.5;
                fixed3 ambient = ShadeSH9(fixed4(0, 1, 0, 1));
                fixed3 col = albedo * (_LightColor0.rgb * ndl * 0.9 + ambient);

                fixed4 outC = fixed4(col, 1);
                UNITY_APPLY_FOG(i.fogCoord, outC);
                return outC;
            }
            ENDCG
        }
    }
    Fallback "VertexLit"
}
