// BallForge instanced sprite shader — ONE draw call for ALL sprites.
// Per-instance data comes from a StructuredBuffer packed by SpriteInstanceRenderSystem.
// Quad is built from SV_VertexID (6-vert triangle soup, mesh attributes ignored).
// Sprites lie flat (world XZ plane); head points toward -Z to match the demo's
// top-down camera (Euler 90,0,180).
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
                float4 PosScale;   // xy = world xz, z = scale, w = world height (y)
                float4 CropST;     // xy = cell scale, zw = cell origin (uv, bottom-left)
                float4 FrameTRS;   // xy = frame scale, z = rotation radians
                float4 Flip;       // x/y = uv flip flags
                float4 Color;      // rgba tint
            };

            StructuredBuffer<SpriteInstanceData> _InstanceData;
            sampler2D _MainTex;
            float _Cutoff;

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
                float2 local = float2(quad.x * d.FrameTRS.x, quad.y * d.FrameTRS.y);
                float cs = cos(d.FrameTRS.z);
                float sn = sin(d.FrameTRS.z);
                float2 rotated = float2(
                    local.x * cs - local.y * sn,
                    local.x * sn + local.y * cs);

                // flat-lay: local x -> world x, local y -> world -z (head to -Z)
                float3 wpos;
                wpos.x = d.PosScale.x + rotated.x * d.PosScale.z;
                wpos.y = d.PosScale.w;
                wpos.z = d.PosScale.y - rotated.y * d.PosScale.z;

                float2 uv = quad + 0.5;
                uv.x = lerp(uv.x, 1.0 - uv.x, saturate(d.Flip.x));
                uv.y = lerp(uv.y, 1.0 - uv.y, saturate(d.Flip.y));

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
