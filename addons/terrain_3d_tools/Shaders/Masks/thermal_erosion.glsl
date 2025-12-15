// thermal_erosion.glsl
#[compute]
#version 450
#extension GL_EXT_shader_atomic_float : require
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D heightmap;

layout(push_constant) uniform PushConstants {
    float talus_angle; // The angle of repose. Slopes steeper than this will collapse.
    float strength;    // How much material to move per pass.
    int map_width;
    int map_height;
} pc;

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(heightmap);
    if (p.x >= size.x || p.y >= size.y) {
        return;
    }

    float h_center = imageLoad(heightmap, p).r;
    float h_max_diff = 0.0;
    ivec2 steepest_neighbor = ivec2(0);

    // Find the steepest downhill neighbor in a 3x3 grid
    for (int y = -1; y <= 1; ++y) {
        for (int x = -1; x <= 1; ++x) {
            if (x == 0 && y == 0) continue;
            
            ivec2 neighbor_p = p + ivec2(x, y);
            if(neighbor_p.x < 0 || neighbor_p.x >= pc.map_width || neighbor_p.y < 0 || neighbor_p.y >= pc.map_height) continue;

            float h_neighbor = imageLoad(heightmap, neighbor_p).r;
            float diff = h_center - h_neighbor;
            if (diff > h_max_diff) {
                h_max_diff = diff;
                steepest_neighbor = ivec2(x, y);
            }
        }
    }

    // If the steepest slope exceeds the talus angle, move material
    if (h_max_diff > pc.talus_angle) {
        // Amount to move is half the difference above the threshold, scaled by strength
        float amount_to_move = (h_max_diff - pc.talus_angle) * 0.5 * pc.strength;
        
        // Use atomics to avoid race conditions with neighbors
        imageAtomicAdd(heightmap, p, -amount_to_move);
        imageAtomicAdd(heightmap, p + steepest_neighbor, amount_to_move);
    }
}