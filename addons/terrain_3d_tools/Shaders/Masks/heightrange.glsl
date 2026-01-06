#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D heightMap;
layout(set = 0, binding = 1, r32f) uniform image2D maskTex;

layout(push_constant, std430) uniform Params {
    float minH;
    float maxH;
    int invert;
} params;

void main() {
    ivec2 uv = ivec2(gl_GlobalInvocationID.xy);
    float h = imageLoad(heightMap, uv).r;

    float inside = (h >= params.minH && h <= params.maxH) ? 1.0 : 0.0;

    if (params.invert == 1) inside = 1.0 - inside;

    imageStore(maskTex, uv, vec4(inside));
}