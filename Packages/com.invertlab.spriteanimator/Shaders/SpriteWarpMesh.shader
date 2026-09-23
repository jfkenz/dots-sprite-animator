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
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 col : COLOR;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = TransformWorldToHClip(v.vertex);
                o.uv = v.uv;
                o.col = v.color;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv) * i.col;
                clip(t.a - max(_Cutoff, 1e-6));
                return t;
            }
            ENDHLSL
        }
    }
}
