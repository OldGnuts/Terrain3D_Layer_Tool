// /Shaders/ManualEdit/apply_height_edit.glsl
#[compute]
#version 450

/*
 * apply_height_edit.glsl
 * 
 * Applies additive height modifications from manual edits.
 * Height delta values range from -1 to +1.
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Bindings
layout(set = 0, binding = 0, r32f) uniform image2D height_map;      // In/Out
layout(set = 0, binding = 1, r32f) uniform readonly image2D height_delta;  // In

// Push constants
layout(push_constant, std430) uniform Params {
    int u_region_size;
    int _pad0;
    int _pad1;
    int _pad2;
} pc;

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    
    if (px.x >= pc.u_region_size || px.y >= pc.u_region_size) {
        return;
    }
    
    // Read current composited height
    float current_height = imageLoad(height_map, px).r;
    
    // Read height delta (-1 to +1)
    float delta = imageLoad(height_delta, px).r;
    
    // Skip if no edit (delta is exactly 0)
    if (abs(delta) < 0.0001) {
        return;
    }
    
    // Apply additive modification
    float new_height = current_height + delta;
    
    // Write result
    imageStore(height_map, px, vec4(new_height, 0.0, 0.0, 0.0));
}