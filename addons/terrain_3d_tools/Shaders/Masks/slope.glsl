#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D heightMap;
layout(set = 0, binding = 1, r32f) uniform image2D maskTex;

layout(push_constant, std430) uniform Params {
    float targetAngle;   // angle in degrees
    float falloff;       // slope falloff amount
    int invert;          // invert output (1/0)
    int pad0;
} params;

void main() {
    ivec2 uv = ivec2(gl_GlobalInvocationID.xy);
    ivec2 dims = imageSize(heightMap);

    // Bounds check
    if (uv.x <= 0 || uv.y <= 0 || uv.x >= dims.x-1 || uv.y >= dims.y-1) {
        imageStore(maskTex, uv, vec4(0.0));
        return;
    }

    float h = imageLoad(heightMap, uv).r;
    float hx = imageLoad(heightMap, uv + ivec2(1,0)).r - imageLoad(heightMap, uv + ivec2(-1,0)).r;
    float hy = imageLoad(heightMap, uv + ivec2(0,1)).r - imageLoad(heightMap, uv + ivec2(0,-1)).r;

    // Gradient magnitude = slope
    float slope = degrees(atan(length(vec2(hx, hy)))); // slope angle in degrees

    // Falloff: stronger effect near targetAngle
    float val = 1.0 - clamp(abs(slope - params.targetAngle) / max(0.001, params.falloff), 0.0, 1.0);

    // Invert if requested
    if (params.invert == 1) val = 1.0 - val;

    imageStore(maskTex, uv, vec4(val));
}