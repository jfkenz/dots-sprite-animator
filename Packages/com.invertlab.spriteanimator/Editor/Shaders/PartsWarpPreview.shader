// Polygon deform preview inside the parts canvas.
// IMGUI is gamma. A linear tex2D sample is converted back so the pixels match GUI.DrawTexture.
Shader "Hidden/InvertLab/Parts Warp Preview"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        ZTest Always
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            // Cell in the sheet (xy = min, zw = size). TEXCOORD0 is cell space: past 0..1 is empty.
            float4 _CellRect;
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                clip(min(min(i.uv.x, i.uv.y), min(1.0 - i.uv.x, 1.0 - i.uv.y)));
                fixed4 c = tex2D(_MainTex, _CellRect.xy + saturate(i.uv) * _CellRect.zw);
                #ifndef UNITY_COLORSPACE_GAMMA
                c.rgb = LinearToGammaSpace(c.rgb);
                #endif
                c *= i.color;
                return c;
            }
            ENDCG
        }
        Pass
        {
            Blend Zero Zero
            ZWrite Off
            ZTest Always
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata
            {
                float4 vertex : POSITION;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
            };
            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                return o;
            }
            fixed4 frag(v2f i) : SV_Target
            {
                return 0;
            }
            ENDCG
        }
    }
}
