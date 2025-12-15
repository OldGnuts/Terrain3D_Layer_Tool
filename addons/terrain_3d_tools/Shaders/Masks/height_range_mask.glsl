#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// BINDINGS (Now much simpler)
layout(set = 0, binding = 0, r32f) uniform image2D target_mask;
layout(set = 0, binding = 1) uniform sampler2D stitched_heightmap;

// PUSH CONSTANTS
layout(push_constant) uniform PushConstants {
    int blend_type;
    float layer_mix;
    int invert;
    float min_height;
    float max_height;
    float falloff_range;
} pc;

void main() {
    ivec2 pixel_coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_mask);

    if (pixel_coords.x >= size.x || pixel_coords.y >= size.y) {
        return;
    }
    
    vec2 uv = (vec2(pixel_coords) + 0.5) / vec2(size);
    float height = texture(stitched_heightmap, uv).r;

    // The rest of the logic is identical to before, but much cleaner!
    float lower_edge = pc.min_height - pc.falloff_range;
    float upper_edge = pc.max_height + pc.falloff_range;
    
    float val_min = smoothstep(lower_edge, pc.min_height, height);
    float val_max = 1.0 - smoothstep(pc.max_height, upper_edge, height);
    
    float mask_value = val_min * val_max;

    if (pc.invert != 0) {
        mask_value = 1.0 - mask_value;
    }

    float original_value = imageLoad(target_mask, pixel_coords).r;
    float blended_value;

    if (pc.blend_type == 0) { // Mix
        blended_value = mix(original_value, mask_value, pc.layer_mix);
    } else if (pc.blend_type == 1) { // Multiply
        blended_value = original_value * mix(1.0, mask_value, pc.layer_mix);
    } else if (pc.blend_type == 2) { // Add
        blended_value = original_value + (mask_value * pc.layer_mix);
    } else if (pc.blend_type == 3) { // Subtract
        blended_value = original_value - (mask_value * pc.layer_mix);
    } else {
        blended_value = original_value * mix(1.0, mask_value, pc.layer_mix);
    }
    
    imageStore(target_mask, pixel_coords, vec4(clamp(blended_value, 0.0, 1.0)));
}