// Invert Lab instanced sprite shader — ONE draw call for ALL sprites.
// Per-instance data comes from a StructuredBuffer packed by SpriteInstanceRenderSystem.
// Quad is built from SV_VertexID (6-vert triangle soup, mesh attributes ignored).
// Default: sprites lie flat on world XZ (soldier top-down, Euler 90,0,180).
// _LayoutXy > 0.5: sprites stand on world XY facing a 2D camera.
Shader "DOTS Sprite Animator/Sprite Unlit 2D Instanced"
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
            ZWrite Off
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct SpriteInstanceData
            {
                float4 PosScale;   // XZ: xy=world xz, z=scale, w=height y
                               // XY: xy=world xy, z=scale, w=depth z
                float4 CropST;     // xy = cell scale, zw = cell origin (uv, bottom-left)
                float4 FrameTRS;   // xy = frame scale, z = rotation radians
                float4 Flip;       // xy = flip flags, zw = normalized pivot
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
                float4 col : TEXCOORD1;
            };

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
                    // 2D camera: local x -> world x, local y -> world y
                    wpos.x = d.PosScale.x + rotated.x * d.PosScale.z;
                    wpos.y = d.PosScale.y + rotated.y * d.PosScale.z;
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
                o.uv = d.CropST.zw + uv * d.CropST.xy;
                o.col = d.Color;
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 t = tex2D(_MainTex, i.uv);
                clip(t.a - _Cutoff);
                return t * i.col;
            }
            ENDHLSL
        }
    }
}
