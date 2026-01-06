#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8) in;

// Region heightmap (target)
layout(r32f, binding = 0) uniform image2D regionHeightmap;

// Mask texture (source)
layout(r32f, binding = 1) uniform image2D maskTex;

layout(push_constant) uniform Push {
    int regionXMin;
    int regionYMin;
    int regionXMax;
    int regionYMax;

    int maskXMin;
    int maskYMin;
    int maskXMax;
    int maskYMax;

    int op;
    float strength;
} pc;

void main()
{
    ivec2 pix = ivec2(gl_GlobalInvocationID.xy);

    // Only operate inside overlap (region-local coords)
    if (pix.x < pc.regionXMin || pix.x >= pc.regionXMax ||
        pix.y < pc.regionYMin || pix.y >= pc.regionYMax) {
        return;
    }

    int dx = pix.x - pc.regionXMin;
    int dy = pix.y - pc.regionYMin;

    int mx = pc.maskXMin + dx;
    int my = pc.maskYMin + dy;

    float maskVal  = imageLoad(maskTex, ivec2(mx, my)).r;
    vec4 heightVal = imageLoad(regionHeightmap, pix);

    if (pc.op == 0) {            // Add
        heightVal.r += maskVal * pc.strength;
    } else if (pc.op == 1) {     // Subtract
        heightVal.r -= maskVal * pc.strength;
    } else if (pc.op == 2) {     // Multiply
        heightVal.r *= (1.0 + maskVal * pc.strength);
    } else if (pc.op == 3) {     // Replace
        heightVal.r = maskVal * pc.strength;
    }

    imageStore(regionHeightmap, pix, heightVal);
}