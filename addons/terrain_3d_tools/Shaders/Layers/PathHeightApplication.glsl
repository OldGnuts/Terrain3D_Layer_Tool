#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform restrict image2D region_heightmap;
layout(set = 0, binding = 1) uniform sampler2D layer_mask;        // Influence
layout(set = 0, binding = 2) uniform sampler2D layer_height_data; // Path heights

layout(push_constant) uniform PushConstants {
    ivec2 region_min;
    ivec2 region_max;
    ivec2 mask_min;
    ivec2 mask_max;
    int mask_width;
    int mask_height;
    float carve_strength;
    float path_elevation_offset;
    float terrain_conformance;
    float embankment_height;
} pc;

void main() {
    ivec2 region_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 region_size = imageSize(region_heightmap);
    
    if (region_coord.x >= region_size.x || region_coord.y >= region_size.y) {
        return;
    }
    
    if (region_coord.x < pc.region_min.x || region_coord.x >= pc.region_max.x ||
        region_coord.y < pc.region_min.y || region_coord.y >= pc.region_max.y) {
        return;
    }
    
    vec2 region_to_mask = vec2(region_coord - pc.region_min) / vec2(pc.region_max - pc.region_min);
    vec2 mask_coord = mix(vec2(pc.mask_min), vec2(pc.mask_max), region_to_mask);
    vec2 uv = mask_coord / vec2(pc.mask_width, pc.mask_height);
    
    float path_influence = texture(layer_mask, uv).r;
    float path_height = texture(layer_height_data, uv).r;
    
    if (abs(path_influence) > 0.001) {
        float current_height = imageLoad(region_heightmap, region_coord).r;
        float final_height = current_height;
        
        if (path_influence > 0.0) {
            float target_height = path_height + pc.path_elevation_offset;
            target_height = mix(current_height, target_height, pc.carve_strength);
            final_height = mix(current_height, target_height, path_influence);
            final_height = mix(final_height, current_height, pc.terrain_conformance);
        }
        else {
            float raise_amount = abs(path_influence) * pc.embankment_height;
            final_height = current_height + raise_amount;
        }
        
        imageStore(region_heightmap, region_coord, vec4(final_height));
    }
}