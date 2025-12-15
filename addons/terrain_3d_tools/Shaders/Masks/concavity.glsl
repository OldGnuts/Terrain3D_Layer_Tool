#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D heightMap;
layout(set = 0, binding = 1, r32f) uniform image2D maskTex;

layout(push_constant, std430) uniform Params {
    int radius;
    float strength;
    int invert;
    float layer_mix;
    int blend_type;
    int _pad0;
       
} params;

void main() {
    ivec2 uv = ivec2(gl_GlobalInvocationID.xy);
    ivec2 dims = imageSize(heightMap);

    if (uv.x < params.radius || uv.y < params.radius 
     || uv.x >= dims.x - params.radius || uv.y >= dims.y - params.radius) {
        return; // don't overwrite edges
    }

    float center = imageLoad(heightMap, uv).r;
    float sum = 0.0;
    int count = 0;


    // Sample neighborhood to estimate curvature
    for (int x = -params.radius; x <= params.radius; x++) {
        for (int y = -params.radius; y <= params.radius; y++) {
            if (x == 0 && y == 0) continue;
            sum += imageLoad(heightMap, uv + ivec2(x,y)).r;
            count++;
        }
    }

    float avg = sum / float(count);
    float diff = (avg - center) * params.strength;

    // Valleys positive white, Ridges negative black
    float maskVal = (diff + 1.0) * 0.5;
    maskVal = clamp(maskVal, 0.0, 1.0);

    if (params.invert == 1) {
        maskVal = 1.0 - maskVal;
    }

    // Load the current value in maskTex
    float base = imageLoad(maskTex, uv).r;

    // Blend according to blend_type and layer_mix
    float blended;
    if (params.blend_type == 0) { 
        // Mix/Lerp
        blended = mix(base, maskVal, params.layer_mix);
    }
    else if (params.blend_type == 1) { 
        // Multiply
        blended = mix(base, base * maskVal, params.layer_mix);
    }
    else if (params.blend_type == 2) { 
        // Add
        blended = mix(base, base + maskVal, params.layer_mix);
    }
    else if (params.blend_type == 3) { 
        // Subtract
        blended = mix(base, base - maskVal, params.layer_mix);
    }

    blended = clamp(blended, 0.0, 1.0);
    imageStore(maskTex, uv, vec4(blended));
}