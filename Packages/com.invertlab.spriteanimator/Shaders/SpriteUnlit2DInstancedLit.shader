// Invert Lab instanced sprite shader — ONE draw call for ALL sprites.
// Per-instance data comes from a StructuredBuffer packed by SpriteInstanceRenderSystem.
// Quad is built from SV_VertexID (6-vert triangle soup, mesh attributes ignored).
// Default: sprites lie flat on world XZ (soldier top-down, Euler 90,0,180).
// _LayoutXy > 0.5: sprites stand on world XY facing a 2D camera.
Shader "DOTS Sprite Animator/Sprite Unlit 2D Instanced Lit"
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
            Name "Unlit"
            Cull Off
            // ZWrite On: PosScale.w (world z in XY layout) becomes the real
            // per-instance depth, so SpriteSortDepth controls compositing.
            ZWrite On
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_0 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_1 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_2 __
            #pragma multi_compile USE_SHAPE_LIGHT_TYPE_3 __


            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct SpriteInstanceData
            {
                float4 PosScale;   // XY: xy=world xy, z=1, w=depth z
                               // XZ: xy=world xz, z=scale, w=height y
                float4 CropST;     // xy = cell scale, zw = cell origin (uv, bottom-left)
                float4 FrameTRS;   // xy = frame scale, z = rotation radians
                float4 Flip;       // xy = flip flags, zw = normalized pivot
                float4 Transform2; // xy = entity scale (world), z = entity rotation radians
                float4 Color;      // rgba tint
            };

            StructuredBuffer<SpriteInstanceData> _InstanceData;
            sampler2D _MainTex;
            float _Cutoff;
            float _LayoutXy;
            float _CellAspect;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float2 lightingUV : TEXCOORD2;
                float4 col : TEXCOORD1;
            };


            // ---- URP 2D lights (self-contained replica of Unity's
            // CombinedShapeLightShared with a constant white mask) ----
            half4 _HDREmulationScale;
#if USE_SHAPE_LIGHT_TYPE_0
            TEXTURE2D(_ShapeLightTexture0);
            SAMPLER(sampler_ShapeLightTexture0);
            half2 _ShapeLightBlendFactors0;
            half4 _ShapeLightMaskFilter0;
            half4 _ShapeLightInvertedFilter0;
#endif
#if USE_SHAPE_LIGHT_TYPE_1
            TEXTURE2D(_ShapeLightTexture1);
            SAMPLER(sampler_ShapeLightTexture1);
            half2 _ShapeLightBlendFactors1;
            half4 _ShapeLightMaskFilter1;
            half4 _ShapeLightInvertedFilter1;
#endif
#if USE_SHAPE_LIGHT_TYPE_2
            TEXTURE2D(_ShapeLightTexture2);
            SAMPLER(sampler_ShapeLightTexture2);
            half2 _ShapeLightBlendFactors2;
            half4 _ShapeLightMaskFilter2;
            half4 _ShapeLightInvertedFilter2;
#endif
#if USE_SHAPE_LIGHT_TYPE_3
            TEXTURE2D(_ShapeLightTexture3);
            SAMPLER(sampler_ShapeLightTexture3);
            half2 _ShapeLightBlendFactors3;
            half4 _ShapeLightMaskFilter3;
            half4 _ShapeLightInvertedFilter3;
#endif

            half4 Apply2DLights(half4 color, float2 lightingUV)
            {
                const half4 mask = half4(1, 1, 1, 1);
                half4 modulate = 0;
                half4 additive = 0;
#if USE_SHAPE_LIGHT_TYPE_0
                {
                    half4 l = SAMPLE_TEXTURE2D(_ShapeLightTexture0, sampler_ShapeLightTexture0, lightingUV);
                    if (any(_ShapeLightMaskFilter0))
                        l *= dot((1 - _ShapeLightInvertedFilter0) * mask
                               + _ShapeLightInvertedFilter0 * (1 - mask), _ShapeLightMaskFilter0);
                    modulate += l * _ShapeLightBlendFactors0.x;
                    additive += l * _ShapeLightBlendFactors0.y;
                }
#endif
#if USE_SHAPE_LIGHT_TYPE_1
                {
                    half4 l = SAMPLE_TEXTURE2D(_ShapeLightTexture1, sampler_ShapeLightTexture1, lightingUV);
                    if (any(_ShapeLightMaskFilter1))
                        l *= dot((1 - _ShapeLightInvertedFilter1) * mask
                               + _ShapeLightInvertedFilter1 * (1 - mask), _ShapeLightMaskFilter1);
                    modulate += l * _ShapeLightBlendFactors1.x;
                    additive += l * _ShapeLightBlendFactors1.y;
                }
#endif
#if USE_SHAPE_LIGHT_TYPE_2
                {
                    half4 l = SAMPLE_TEXTURE2D(_ShapeLightTexture2, sampler_ShapeLightTexture2, lightingUV);
                    if (any(_ShapeLightMaskFilter2))
                        l *= dot((1 - _ShapeLightInvertedFilter2) * mask
                               + _ShapeLightInvertedFilter2 * (1 - mask), _ShapeLightMaskFilter2);
                    modulate += l * _ShapeLightBlendFactors2.x;
                    additive += l * _ShapeLightBlendFactors2.y;
                }
#endif
#if USE_SHAPE_LIGHT_TYPE_3
                {
                    half4 l = SAMPLE_TEXTURE2D(_ShapeLightTexture3, sampler_ShapeLightTexture3, lightingUV);
                    if (any(_ShapeLightMaskFilter3))
                        l *= dot((1 - _ShapeLightInvertedFilter3) * mask
                               + _ShapeLightInvertedFilter3 * (1 - mask), _ShapeLightMaskFilter3);
                    modulate += l * _ShapeLightBlendFactors3.x;
                    additive += l * _ShapeLightBlendFactors3.y;
                }
#endif
#if !USE_SHAPE_LIGHT_TYPE_0 && !USE_SHAPE_LIGHT_TYPE_1 && !USE_SHAPE_LIGHT_TYPE_2 && !USE_SHAPE_LIGHT_TYPE_3
                return color;
#else
                half4 finalOutput = _HDREmulationScale * (color * modulate + additive);
                finalOutput.a = color.a;
                return max(0, finalOutput);
#endif
            }

            v2f vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                // two triangles covering [-0.5, 0.5]^2, uv 0..1 (v up)
                static const float2 QUAD[6] =
                {
                    float2(-0.5, -0.5), float2(-0.5, 0.5), float2(0.5, -0.5),
                    float2(0.5, -0.5),  float2(-0.5, 0.5), float2(0.5, 0.5)
                };

                SpriteInstanceData d = _InstanceData[iid];
                float2 quad = QUAD[vid];

                // Mirror the QUAD around the authored pivot (geometry); UVs stay
                // attached to their vertices. A single mirror only — also mirroring
                // UVs around the cell center would cancel the flip (double mirror),
                // and mirroring UVs around a non-center pivot would sample outside
                // the cell and bleed the neighboring frame.
                float2 pivot = d.Flip.zw;
                if (pivot.x == 0.0 && pivot.y == 0.0)
                    pivot = float2(0.5, 0.5);
                float2 posed = quad;
                posed.x = lerp(quad.x, 2.0 * (pivot.x - 0.5) - quad.x, saturate(d.Flip.x));
                posed.y = lerp(quad.y, 2.0 * (pivot.y - 0.5) - quad.y, saturate(d.Flip.y));

                float aspect = _CellAspect > 0.001 ? _CellAspect : 1.0;
                float2 local = float2(posed.x * d.FrameTRS.x * aspect, posed.y * d.FrameTRS.y);
                float cs = cos(d.FrameTRS.z);
                float sn = sin(d.FrameTRS.z);
                float2 rotated = float2(
                    local.x * cs - local.y * sn,
                    local.x * sn + local.y * cs);

                float3 wpos;
                if (_LayoutXy > 0.5)
                {
                    // 2D camera: local x -> world x, local y -> world y.
                    // Entity scale + rotation come from LocalToWorld (fresh
                    // every frame), applied after the frame transform.
                    float2 scaled = rotated * d.Transform2.xy;
                    float cs2 = cos(d.Transform2.z);
                    float sn2 = sin(d.Transform2.z);
                    float2 entityRotated = float2(
                        scaled.x * cs2 - scaled.y * sn2,
                        scaled.x * sn2 + scaled.y * cs2);
                    wpos.x = d.PosScale.x + entityRotated.x;
                    wpos.y = d.PosScale.y + entityRotated.y;
                    wpos.z = d.PosScale.w;
                }
                else
                {
                    // flat-lay: local x -> world x, local y -> world -z (head to -Z)
                    wpos.x = d.PosScale.x + rotated.x * d.PosScale.z;
                    wpos.y = d.PosScale.w;
                    wpos.z = d.PosScale.y - rotated.y * d.PosScale.z;
                }

                // UVs unchanged (still the full cell, always inside it).
                float2 uv = quad + 0.5;

                v2f o;
                o.pos = TransformObjectToHClip(wpos); // identity object->world; data already world
                o.lightingUV = ComputeScreenPos(o.pos / o.pos.w).xy;
                o.uv = d.CropST.zw + uv * d.CropST.xy;
                o.col = d.Color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                clip(t.a - _Cutoff);
                return Apply2DLights(t * i.col, i.lightingUV);
            }
            ENDHLSL
        }
    }
}
