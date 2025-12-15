#[compute]
#version 450
#extension GL_EXT_shader_atomic_float : require
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform readonly image2D heightmap;
layout(set = 0, binding = 1, r32f) uniform image2D flow_map;

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(heightmap);
    if (p.x >= size.x || p.y >= size.y) {
        return;
    }

    float h_center = imageLoad(heightmap, p).r;
    
    // Find the lowest neighbor to send our water to.
    float h_min = h_center;
    ivec2 lowest_neighbor = p;

    for (int y = -1; y <= 1; ++y) {
        for (int x = -1; x <= 1; ++x) {
            if (x == 0 && y == 0) continue;
            
            ivec2 neighbor_p = clamp(p + ivec2(x, y), ivec2(0), size - 1);
            float h_neighbor = imageLoad(heightmap, neighbor_p).r;

            if (h_neighbor < h_min) {
                h_min = h_neighbor;
                lowest_neighbor = neighbor_p;
            }
        }
    }

    // Add our current flow amount (plus 1 for "rain") to the lowest neighbor.
    // If we are the lowest point, the water stays here.
    float my_flow = imageLoad(flow_map, p).r + 1.0;
    imageAtomicAdd(flow_map, lowest_neighbor, my_flow);
}