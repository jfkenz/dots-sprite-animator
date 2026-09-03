// Invert Lab - Scene-view Quad preview only.
// No DOTS_INSTANCING_ON: Entities Graphics can force that keyword globally, which
// makes MeshRenderer ignore material/MPB crop and show the full sheet.
// ApplyQuadPreview UV-bakes the cell into mesh UVs and sets _CropST/_Flip to
// identity, so crop no longer depends on MaterialPropertyBlock overrides.
// All material props MUST live in UnityPerMaterial for SRP Batcher +
// Entities Graphics / BatchRendererGroup compatibility (props outside that
// cbuffer trigger: "Material property is found in another cbuffer than
// UnityPerMaterial").
Shader "DOTS Sprite Animator/Sprite Unlit 2D Preview"
{
    Properties
    {
        _MainTex     ("Sprite Atlas", 2D) = "white" {}
        _Color       ("Tint", Color) = (1, 1, 1, 1)
        _CropST      ("Crop ST (scale.xy, offset.zw)", Vector) = (1, 1, 0, 0)
        _Flip        ("Flip XY + Pivot UV", Vector) = (0, 0, 0.5, 0.5)
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

            // Match runtime Sprite Unlit 2D UnityPerMaterial layout (minus _ZOrder):
            // SRP Batcher / BRG require every material property in this cbuffer.
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _Color;
                float4 _CropST;
                float4 _Flip;
                float  _AlphaCutoff;
            CBUFFER_END

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
                float2 pivot = _Flip.zw;
                if (pivot.x == 0.0 && pivot.y == 0.0)
                    pivot = float2(0.5, 0.5);
                float2 uv = IN.uv;
                uv.x = lerp(uv.x, 2.0 * pivot.x - uv.x, saturate(_Flip.x));
                uv.y = lerp(uv.y, 2.0 * pivot.y - uv.y, saturate(_Flip.y));

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
