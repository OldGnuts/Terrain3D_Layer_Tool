#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// BINDINGS (Unchanged)
layout(set = 0, binding = 0, r32f) uniform image2D target_mask;
layout(set = 0, binding = 1) uniform sampler2D source_texture;

// PUSH CONSTANTS (Unchanged)
layout(push_constant) uniform PushConstants {
    int blend_type;
    float layer_mix;
    int invert;
    int output_mode; // 0: ErodedHeight, 1: DepositionMask, 2: FlowMask
    float remap_min;
    float remap_max;
    int pad0;
    int pad1;
} pc;

void main() {
    ivec2 pixel_coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_mask);

    if (pixel_coords.x >= size.x || pixel_coords.y >= size.y) {
        return;
    }

    vec2 uv = (vec2(pixel_coords) + 0.5) / vec2(size);
    float output_value = texture(source_texture, uv).r;

    // --- CHANGED: Apply remapping to ALL output modes ---
    // This is crucial for correctly visualizing your -1 to 1 heightmap data.
    float range = pc.remap_max - pc.remap_min;
    if (range > 0.00001) {
        output_value = (output_value - pc.remap_min) / range;
    } else {
        output_value = 0.0; // Default to black if range is zero
    }
    // Clamp the remapped value to a 0-1 range before blending.
    output_value = clamp(output_value, 0.0, 1.0);
    
    
    if (pc.invert != 0) {
        output_value = 1.0 - output_value;
    }

    float original_value = imageLoad(target_mask, pixel_coords).r;
    float blended_value;
    
    // Blend logic (Unchanged)
    if (pc.blend_type == 0) { // Mix
        blended_value = mix(original_value, output_value, pc.layer_mix);
    } else if (pc.blend_type == 1) { // Multiply
        blended_value = original_value * mix(1.0, output_value, pc.layer_mix);
    } else if (pc.blend_type == 2) { // Add
        blended_value = original_value + (output_value * pc.layer_mix);
    } else if (pc.blend_type == 3) { // Subtract
        blended_value = original_value - (output_value * pc.layer_mix);
    } else {
        blended_value = mix(original_value, output_value, pc.layer_mix);
    }

    // Final value should be clamped for a standard mask texture.
    blended_value = clamp(blended_value, 0.0, 1.0);
    
    imageStore(target_mask, pixel_coords, vec4(blended_value));
}