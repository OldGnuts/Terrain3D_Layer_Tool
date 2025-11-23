#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Region control map (read/write)
layout(set = 0, binding = 0, rgba8) uniform restrict image2D region_controlmap;

// Layer mask (read-only, sampled)
layout(set = 0, binding = 1) uniform sampler2D layer_mask;

layout(push_constant) uniform PushConstants {
    ivec2 region_min;
    ivec2 region_max;
    ivec2 mask_min;
    ivec2 mask_max;
    int mask_width;
    int mask_height;
    uint center_texture_id;
    uint embankment_texture_id;
    float texture_influence;
    float padding1;
    float padding2;
    float padding3;
} pc;

void main() {
    ivec2 region_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 region_size = imageSize(region_controlmap);
    
    if (region_coord.x >= region_size.x || region_coord.y >= region_size.y) {
        return;
    }
    
    // Check if within overlap bounds
    if (region_coord.x < pc.region_min.x || region_coord.x >= pc.region_max.x ||
        region_coord.y < pc.region_min.y || region_coord.y >= pc.region_max.y) {
        return;
    }
    
    // Map from region space to mask space
    vec2 region_to_mask = vec2(region_coord - pc.region_min) / vec2(pc.region_max - pc.region_min);
    vec2 mask_coord = mix(vec2(pc.mask_min), vec2(pc.mask_max), region_to_mask);
    
    // Normalize to UV coordinates
    vec2 uv = mask_coord / vec2(pc.mask_width, pc.mask_height);
    
    // Sample the path mask (contains influence/distance information)
    float path_influence = texture(layer_mask, uv).r;
    
    // Only apply texture if there's path influence
    if (path_influence > 0.0001) {
        vec4 current_control = imageLoad(region_controlmap, region_coord);
        
        // Simple texture application - can be enhanced based on texture mode
        // For now, just blend the center texture
        vec4 new_control = current_control;
        
        // Terrain3D uses specific control map encoding
        // This is a simplified version - adjust based on your Terrain3D setup
        new_control.r = float(pc.center_texture_id) / 31.0;
        new_control.a = path_influence * pc.texture_influence;
        
        // Blend with existing control data
        vec4 final_control = mix(current_control, new_control, path_influence * pc.texture_influence);
        
        imageStore(region_controlmap, region_coord, final_control);
    }
}