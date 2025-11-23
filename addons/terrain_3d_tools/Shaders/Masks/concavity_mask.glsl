#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// BINDINGS
layout(set = 0, binding = 0, r32f) uniform image2D target_mask;
layout(set = 0, binding = 1) uniform sampler2D stitched_heightmap;

// PUSH CONSTANTS
layout(push_constant) uniform PushConstants {
    int blend_type;
    float layer_mix;
    int invert;
    int mode; // 0: Concave, 1: Convex, 2: Both
    int radius;
    float strength;
} pc;

void main() {
    ivec2 pixel_coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_mask);

    if (pixel_coords.x >= size.x || pixel_coords.y >= size.y) {
        return;
    }

    // textureOffset is an efficient way to sample neighbors.
    float center_height = texelFetch(stitched_heightmap, pixel_coords, 0).r;

    float avg_height = 0.0;
    float sample_count = 0.0;
    for (int y = -pc.radius; y <= pc.radius; y++) {
        for (int x = -pc.radius; x <= pc.radius; x++) {
            if (x == 0 && y == 0) continue;
            avg_height += texelFetch(stitched_heightmap, pixel_coords + ivec2(x, y), 0).r;
            sample_count += 1.0;
        }
    }
    avg_height /= max(1.0, sample_count);

    float concavity = (avg_height - center_height) * pc.strength;

    float mask_value = 0.0;
    if (pc.mode == 0) { // Concave
        mask_value = clamp(concavity, 0.0, 1.0);
    } else if (pc.mode == 1) { // Convex
        mask_value = clamp(-concavity, 0.0, 1.0);
    } else { // Both
        mask_value = clamp(abs(concavity), 0.0, 1.0);
    }

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