// Invert Lab GPU-animated sprite shader — the SHADER picks the displayed frame
// from a global clock (_Now), so animating 1M sprites costs the CPU nothing.
// Per-instance data (position, clip rate, start time, wrap, tint) is static:
// uploaded only when a unit spawns/moves/changes clip, never per-frame.
Shader "DOTS Sprite Animator/Sprite Unlit 2D GPU Anim"
{
    Properties
    {
        _MainTex ("Sheet", 2D) = "white" {}
        _Cutoff   ("Alpha Cutoff", Range(0,1)) = 0.5
        _Now      ("Global Clock", Float) = 0
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

            struct SpriteGpuInstanceData
            {
                float4 PosScale; // XZ: xy=world xz, z=scale, w=height y
                               // XY: xy=world xy, z=scale, w=depth z
                float4 Cell;     // xy = cell size uv, zw = first-cell origin uv
                float4 Anim;     // x = start time, y = rate fps, z = frames, w = wrap(1/0)
                float4 Flip;     // xy = flip flags, zw = normalized pivot
                float4 Color;    // rgba tint
            };

            StructuredBuffer<SpriteGpuInstanceData> _InstanceData;
            sampler2D _MainTex;
            float _Cutoff;
            float _Now;
            float _LayoutXy;
            float _UseSharedClip;
            float _CellAspect;
            float4 _SharedCell;
            float4 _SharedAnim;

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                float4 col : TEXCOORD1;
            };

            float2 FrameUvOrigin(float4 cell, float4 anim)
            {
                int n = (int)anim.z;
                int cols = max(1, (int)round(1.0 / cell.x));
                int f;
                if (anim.y <= 0.0)
                    f = 0;                                   // frozen (paused)
                else
                {
                    float t = _Now - anim.x;                 // seconds since start
                    f = (int)floor(t * anim.y);
                    f = anim.w > 0.5 ? (f % n) : min(f, n - 1);
                    f = clamp(f, 0, n - 1);
                    if (f < 0) f += n;                       // negative-time safety
                }
                int col = f % cols;
                int row = f / cols;
                // walk DOWN in v for later rows (origin is bottom-left of cell 0)
                return cell.zw + float2(col, -row) * cell.xy;
            }

            v2f vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                static const float2 QUAD[6] =
                {
                    float2(-0.5, -0.5), float2(-0.5, 0.5), float2(0.5, -0.5),
                    float2(0.5, -0.5),  float2(-0.5, 0.5), float2(0.5, 0.5)
                };

                SpriteGpuInstanceData d = _InstanceData[iid];
                float4 cell = _UseSharedClip > 0.5 ? _SharedCell : d.Cell;
                float4 anim = _UseSharedClip > 0.5 ? _SharedAnim : d.Anim;
                float2 c = QUAD[vid];
                float aspect = _CellAspect > 0.001 ? _CellAspect : 1.0;

                float3 wpos;
                if (_LayoutXy > 0.5)
                {
                    wpos.x = d.PosScale.x + c.x * d.PosScale.z * aspect;
                    wpos.y = d.PosScale.y + c.y * d.PosScale.z;
                    wpos.z = d.PosScale.w;
                }
                else
                {
                    wpos.x = d.PosScale.x + c.x * d.PosScale.z * aspect;
                    wpos.y = d.PosScale.w;
                    wpos.z = d.PosScale.y - c.y * d.PosScale.z;
                }

                float2 origin = FrameUvOrigin(cell, anim);

                float2 uv = c + 0.5;
                float2 pivot = d.Flip.zw;
                if (pivot.x == 0.0 && pivot.y == 0.0)
                    pivot = float2(0.5, 0.5);
                uv.x = lerp(uv.x, 2.0 * pivot.x - uv.x, saturate(d.Flip.x));
                uv.y = lerp(uv.y, 2.0 * pivot.y - uv.y, saturate(d.Flip.y));

                v2f o;
                o.pos = TransformObjectToHClip(wpos);
                o.uv = origin + uv * cell.xy;
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
