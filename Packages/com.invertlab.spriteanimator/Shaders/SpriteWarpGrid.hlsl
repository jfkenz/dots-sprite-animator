// Mesh warp. _WarpResolution == 0 draws the classic 6-vert quad.
// _WarpResolution >= 3 draws that grid. Offsets are _WarpOffsets[iid * 25 + index].
// SpriteInstanceData.WarpMeta.x > 2 means this instance is drawn by the warp pass.
#ifndef SPRITE_WARP_GRID_INCLUDED
#define SPRITE_WARP_GRID_INCLUDED

#define SPRITE_WARP_MAX 25

StructuredBuffer<float2> _WarpOffsets;
float _WarpResolution;

void SpriteClassicQuad(uint vid, out float2 quad, out float2 uv)
{
    float2 q = float2(-0.5, -0.5);
    if (vid == 1u || vid == 4u) q = float2(-0.5, 0.5);
    else if (vid == 2u || vid == 3u) q = float2(0.5, -0.5);
    else if (vid == 5u) q = float2(0.5, 0.5);
    quad = q;
    uv = q + 0.5;
}

void SpriteWarpGrid(uint vid, uint iid, out float2 quad, out float2 uv)
{
    uint res = (uint)max(_WarpResolution, 3.0);
    uint cells = res - 1u;
    uint cell = vid / 6u;
    uint k = vid - cell * 6u;
    uint cx = cell % cells;
    uint cy = cell / cells;
    uint gx = cx;
    uint gy = cy;
    if (k == 2u || k == 3u || k == 5u) gx = cx + 1u;
    if (k == 1u || k == 4u || k == 5u) gy = cy + 1u;
    float step = 1.0 / cells;
    float2 base = float2(gx * step - 0.5, gy * step - 0.5);
    quad = base + _WarpOffsets[iid * SPRITE_WARP_MAX + gx + gy * res];
    uv = base + 0.5;
}

#endif
