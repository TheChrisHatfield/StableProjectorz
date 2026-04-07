#ifndef TEXARR_GEOM_FUNC_FOR_BLIT_SLICES_RANGED
#define TEXARR_GEOM_FUNC_FOR_BLIT_SLICES_RANGED
#include "Assets/_gm/_Core/Shader_Includes/TextureArrays_AutoDefines.cginc"

// _SliceCompositeEnd == 0 is treated as “to _NumSlices” so unset/new materials still composite all slices once _NumSlices is set (PaintLayerStack_MGR also sets range on every Blit).
uint _SliceCompositeBegin;
uint _SliceCompositeEnd;

#if defined(NUM_SLICES_UPTO_24)
    [maxvertexcount(24*4)]
#elif defined(NUM_SLICES_UPTO_16)
    [maxvertexcount(16*4)]
#else
    [maxvertexcount(8*4)]
#endif
void geom(point v2g input[1], inout TriangleStream<g2f> outStream) {
    #if !defined(USING_TEXTURE_ARRAY)
        _NumSlices = 1;
    #endif

    uint sb = _SliceCompositeBegin;
    uint se = _SliceCompositeEnd;
    if (se == 0u)
        se = (uint)_NumSlices;

    float4 vertices[4] = {
        float4(-1, 1, 0, 1), float4(1, 1, 0, 1), float4(-1,-1, 0, 1), float4( 1,-1, 0, 1)
    };

    #if UNITY_UV_STARTS_AT_TOP
        float2 uvs[4] = { float2(0,0), float2(1,0), float2(0,1), float2(1,1) };
    #else
        float2 uvs[4] = { float2(0,1), float2(1,1), float2(0,0), float2(1,0) };
    #endif

    g2f output;
    for (uint slice = sb; slice < se; ++slice) {
        output.slice = slice;

        output.vertex = vertices[0];
        output.uv = uvs[0];
        outStream.Append(output);

        output.vertex = vertices[1];
        output.uv = uvs[1];
        outStream.Append(output);

        output.vertex = vertices[2];
        output.uv = uvs[2];
        outStream.Append(output);

        output.vertex = vertices[3];
        output.uv = uvs[3];
        outStream.Append(output);

        outStream.RestartStrip();
    }
}
#endif
