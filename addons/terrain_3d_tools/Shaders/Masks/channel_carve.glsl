#[compute]
#version 450
#extension GL_EXT_shader_atomic_float : require
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D heightmap;
layout(set = 0, binding = 1, r32f) uniform readonly image2D flow_map;

layout(push_constant) uniform PushConstants {
    float strength;
} pc;

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(heightmap);
    if (p.x >= size.x || p.y >= size.y) {
        return;
    }

    float flow = imageLoad(flow_map, p).r;
    
    // Carve based on the flow amount. Using sqrt or pow helps
    // to emphasize the high-flow areas more strongly.
    float carve_amount = pow(flow / float(size.x), 0.8) * pc.strength;

    if (carve_amount > 0.0) {
        imageAtomicAdd(heightmap, p, -carve_amount);
    }
}