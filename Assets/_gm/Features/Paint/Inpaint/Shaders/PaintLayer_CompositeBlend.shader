Shader "Unlit/PaintLayer_CompositeBlend"
{
    Properties
    {
        _Background("Background (Texture2DArray)", 2DArray) = "" {}
        _Opacity("Layer opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        ZWrite Off
        ZTest Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.5
            #pragma require geometry
            #pragma require setrtarrayindexfromanyshader

            #pragma vertex vert
            #pragma geometry geom
            #pragma fragment frag

            #pragma multi_compile  NUM_SLICES_UPTO_8  NUM_SLICES_UPTO_16  NUM_SLICES_UPTO_24

            #define USING_TEXTURE_ARRAY
            #include "UnityCG.cginc"
            #include "Assets/_gm/_Core/Shader_Includes/TextureArrays_AutoDefines.cginc"

            UNITY_DECLARE_TEX2DARRAY(_Background);
            UNITY_DECLARE_TEX2DARRAY(_MainTex);
            float _Opacity;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2g
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            struct g2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                uint slice : SV_RenderTargetArrayIndex;
            };

            v2g vert (appdata v)
            {
                v2g o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            #include "Assets/_gm/_Core/Shader_Includes/TextureArrays_GeomFunc_ForBlitSlices.cginc"

            fixed4 frag (g2f i) : SV_Target
            {
                float3 uv_slice = float3(i.uv, i.slice);
                float4 bg = UNITY_SAMPLE_TEX2DARRAY(_Background, uv_slice);
                // _MainTex is set by Graphics.Blit (foreground layer)
                float4 fg = UNITY_SAMPLE_TEX2DARRAY(_MainTex, uv_slice);
                float t = fg.a * _Opacity;
                return lerp(bg, fg, t);
            }
            ENDCG
        }
    }
}
