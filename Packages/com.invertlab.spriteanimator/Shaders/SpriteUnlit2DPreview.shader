// Invert Lab - Scene-view Quad preview only.
// No DOTS_INSTANCING_ON: Entities Graphics can force that keyword globally, which
// makes MeshRenderer ignore material/MPB _CropST and show the full sheet (default 1,1,0,0).
// _CropST / _Flip live OUTSIDE UnityPerMaterial so MaterialPropertyBlock + SetVector
// always reach the vertex stage under URP SRP Batcher.
Shader "DOTS Sprite Animator/Sprite Unlit 2D Preview"
{
    Properties
    {
        _MainTex     ("Sprite Atlas", 2D) = "white" {}
        _Color       ("Tint", Color) = (1, 1, 1, 1)
        _CropST      ("Crop ST (scale.xy, offset.zw)", Vector) = (1, 1, 0, 0)
        _Flip        ("Flip XY (0/1 each)", Vector) = (0, 0, 0, 0)
        _AlphaCutoff ("Alpha Cutoff (0 = off)", Range(0, 1)) = 0
        [HideInInspector] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SpriteUnlit2DPreview"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 3.0
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float  _AlphaCutoff;
            CBUFFER_END

            // Outside UnityPerMaterial: SRP Batcher will not pack these; MPB/SetVector
            // overrides reliably reach Vert (batcher-safe crop was ignoring MPB).
            float4 _CropST;
            float4 _Flip;

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);

                // When ApplyQuadPreview UV-bakes the cell, mesh UVs are already the
                // cell rect and _CropST is (1,1,0,0); flip is also baked into UVs.
                // Keep crop/flip here as a second line of defense for non-baked paths.
                float2 uv = IN.uv - 0.5;
                uv.x = lerp(uv.x, -uv.x, saturate(_Flip.x));
                uv.y = lerp(uv.y, -uv.y, saturate(_Flip.y));
                uv += 0.5;

                OUT.uv = uv * _CropST.xy + _CropST.zw;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 col = _Color;
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                col *= tex;
                clip(col.a - max(_AlphaCutoff, -1e-4));
                return col;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
