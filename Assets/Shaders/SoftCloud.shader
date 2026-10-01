// Unlit transparent double-sided shader for the drifting cloud quads.
// Alpha comes from the procedurally generated cloud texture; the material
// color alpha is used for fade-in/out at the edges of the visible sky band.
Shader "SnakeGame/SoftCloud"
{
    Properties
    {
        _MainTex ("Cloud Texture", 2D) = "white" {}
        _Color ("Tint (alpha = fade)", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Cull Off            // visible from either side, no orientation worries
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;

            // explicit inputs/outputs - no reliance on Unity's built-in
            // appdata structs (their UV member names differ per struct)
            struct a2v
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(a2v v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                return tex2D(_MainTex, i.uv) * _Color;
            }
            ENDCG
        }
    }
    Fallback Off
}
