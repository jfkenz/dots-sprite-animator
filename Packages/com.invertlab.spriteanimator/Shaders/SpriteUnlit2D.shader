// Invert Lab DOTS 2D sprite — unlit, transparent, per-instance everything.
// Per-instance properties ride Entities Graphics material property overrides
// ([MaterialProperty]-tagged IComponentData), NOT MaterialPropertyBlock
// (which does not exist for entities). All props are DOTS-instanced so
// hundreds of animated sprites stay in one draw call.
Shader "DOTS Sprite Animator/Sprite Unlit 2D"
{
    Properties
    {
        _MainTex     ("Sprite Atlas", 2D) = "white" {}
        _Color       ("Tint", Color) = (1, 1, 1, 1)
        _CropST      ("Crop ST (scale.xy, offset.zw)", Vector) = (1, 1, 0, 0)
        _Flip        ("Flip XY + Pivot UV", Vector) = (0, 0, 0.5, 0.5)
        _ZOrder      ("Z Order (depth bias)", Float) = 0
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
            Name "SpriteUnlit2D"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            // DOTS instancing: same pattern as URP's DOTS.hlsl — the
            // conditional "#pragma target 4.5 DOTS_INSTANCING_ON" makes the
            // DOTS_INSTANCING_ON variant the DEFAULT at runtime (plain
            // multi_compile leaves it off, so per-entity props never bind).
            #pragma multi_compile _ DOTS_INSTANCING_ON
            #pragma target 4.5 DOTS_INSTANCING_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // Canonical URP layout: all material props live in
            // UnityPerMaterial (SRP-Batcher compatible — required for
            // Entities Graphics to upload material-level values), and the
            // DOTS instancing block shadows them per-entity when the
            // DOTS_INSTANCING_ON variant is active.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _CropST;
                float4 _Flip;
                float  _ZOrder;
                float  _AlphaCutoff;
            CBUFFER_END

            #ifdef UNITY_DOTS_INSTANCING_ENABLED
            // NOTE: _MainTex_ST deliberately NOT instanced — Unity reserves
            // _ST-suffixed props for texture tiling/offset and an instanced
            // _ST entry breaks the whole DOTS property block (all props
            // read zero). Crop rides the custom _CropST prop below.
            UNITY_DOTS_INSTANCING_START(MaterialProps)
                UNITY_DOTS_INSTANCED_PROP(float4, _CropST)
                UNITY_DOTS_INSTANCED_PROP(float4, _Color)
                UNITY_DOTS_INSTANCED_PROP(float4, _Flip)
                UNITY_DOTS_INSTANCED_PROP(float,  _ZOrder)
                UNITY_DOTS_INSTANCED_PROP(float,  _AlphaCutoff)
            UNITY_DOTS_INSTANCING_END(MaterialProps)

            #define PFLOAT(name)  UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float,  name)
            #define PFLOAT4(name) UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, name)
            #define PCROP(name)   UNITY_ACCESS_DOTS_INSTANCED_PROP_WITH_DEFAULT(float4, name)
            #else
            #define PFLOAT(name)  name
            #define PFLOAT4(name) name
            #define PCROP(name)   name
            #endif

            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR0;   // reserved (vertex tint), unused v1
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings Vert(Attributes IN)
            {
                // Required for DOTS instancing: resolves unity_InstanceID
                // from SV_InstanceID so per-entity property lookups address
                // THIS entity's slab in unity_DOTSInstanceData.
                UNITY_SETUP_INSTANCE_ID(IN);

                Varyings OUT;
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                // Flip mirrors the QUAD around the authored pivot (_Flip.zw,
                // default 0.5 = cell center) in object space; UVs stay attached to
                // their vertices. A single mirror only — also mirroring UVs would
                // cancel the flip, and UV-mirroring around a non-center pivot would
                // sample outside the cell and bleed the neighboring frame.
                float4 flip = PFLOAT4(_Flip);
                float2 pivot = flip.zw;
                if (pivot.x == 0.0h && pivot.y == 0.0h)
                    pivot = float2(0.5h, 0.5h);
                float3 posOS = IN.positionOS;
                posOS.x = lerp(posOS.x, 2.0h * (pivot.x - 0.5h) - posOS.x, saturate(flip.x));
                posOS.y = lerp(posOS.y, 2.0h * (pivot.y - 0.5h) - posOS.y, saturate(flip.y));

                float4 posCS = TransformWorldToHClip(
                    TransformObjectToWorld(posOS));

                // Z-order WITHOUT touching the transform: nudge clip-space Z.
                // Small factor keeps the bias sub-depth-buffer-step.
                posCS.z += PFLOAT(_ZOrder) * 0.0005h;
                OUT.positionCS = posCS;

                // UVs unchanged (still the full cell, always inside it).
                float4 st = PCROP(_CropST);
                OUT.uv = IN.uv * st.xy + st.zw;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                half4 col = PFLOAT4(_Color);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                col *= tex;

                // Alpha cutoff doubles as the clipping toggle: <=0 keeps all.
                float cutoff = PFLOAT(_AlphaCutoff);
                clip(col.a - max(cutoff, -1e-4h));

                return col;
            }
            ENDHLSL
        }
    }

    Fallback "Universal Render Pipeline/Unlit"
}
