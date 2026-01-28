// /Shaders/ManualEdit/apply_instance_exclusion.glsl
#[compute]
#version 450

/*
 * apply_instance_exclusion.glsl
 * 
 * Combines manual instance exclusion with existing exclusion map.
 * Uses max operation so any exclusion source blocks placement.
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Bindings
layout(set = 0, binding = 0, r32f) uniform image2D exclusion_map;        // In/Out: region exclusion
layout(set = 0, binding = 1, r32f) uniform readonly image2D manual_exclusion;  // In: manual exclusion

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
    
    // Read current exclusion value
    float current_exclusion = imageLoad(exclusion_map, px).r;
    
    // Read manual exclusion value
    float manual_value = imageLoad(manual_exclusion, px).r;
    
    // Combine with max - any exclusion blocks placement
    float combined = max(current_exclusion, manual_value);
    
    // Write result
    imageStore(exclusion_map, px, vec4(combined, 0.0, 0.0, 0.0));
}