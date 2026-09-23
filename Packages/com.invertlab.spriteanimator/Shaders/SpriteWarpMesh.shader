Shader "DOTS Sprite Animator/Sprite Warp Mesh"
{
    Properties
    {
        _MainTex ("Sheet", 2D) = "white" {}
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
    }
    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }
        Pass
        {
            Cull Off
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            sampler2D _MainTex;
            float _Cutoff;
            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD0;    // cell space: 0..1 is the image, vertices may sit past it
                float4 crop : TEXCOORD1;  // xy = cell size, zw = cell origin in the sheet
                float2 inset : TEXCOORD2; // half a texel in cell space, keeps bilinear inside the cell
                float4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 crop : TEXCOORD1;
                float2 inset : TEXCOORD2;
                float4 col : COLOR;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformWorldToHClip(v.vertex);
                o.uv = v.uv;
                o.crop = v.crop;
                o.inset = v.inset;
                o.col = v.color;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                // Mesh area outside the image is empty, never the neighbouring cell.
                clip(min(min(i.uv.x, i.uv.y), min(1.0 - i.uv.x, 1.0 - i.uv.y)));
                float2 cell = clamp(i.uv, i.inset, 1.0 - i.inset);
                float4 t = tex2D(_MainTex, i.crop.zw + cell * i.crop.xy) * i.col;
                clip(t.a - max(_Cutoff, 1e-6));
                return t;
            }
            ENDHLSL
        }
    }
}
