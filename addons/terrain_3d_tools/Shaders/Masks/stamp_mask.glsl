#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D target_mask;
layout(set = 0, binding = 1) uniform sampler2D stamp_texture;

layout(push_constant) uniform PushConstants {
    int blend_type;
    float layer_mix;
    // REFACTOR: Changed from bool to int to match C# push constants.
    int invert; 
    int sample_mode;
    int flip_x;
    int flip_y;
} pc;

void main() {
    ivec2 pixel_coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_mask);

    if (pixel_coords.x >= size.x || pixel_coords.y >= size.y) {
        return;
    }

    vec2 uv = (vec2(pixel_coords) + 0.5) / vec2(size);
    
    // REFACTOR: Comparison now checks if the int is not zero.
    if (pc.flip_x != 0) {
        uv.x = 1.0 - uv.x;
    }
    if (pc.flip_y != 0) {
        uv.y = 1.0 - uv.y;
    }

    vec4 stamp_color = texture(stamp_texture, uv);

    float stamp_value = 0.0;
    if (pc.sample_mode == 0) { // Luminance
        stamp_value = dot(stamp_color.rgb, vec3(0.299, 0.587, 0.114));
    } else if (pc.sample_mode == 1) { // Red
        stamp_value = stamp_color.r;
    } else if (pc.sample_mode == 2) { // Green
        stamp_value = stamp_color.g;
    } else if (pc.sample_mode == 3) { // Blue
        stamp_value = stamp_color.b;
    } else if (pc.sample_mode == 4) { // Alpha
        stamp_value = stamp_color.a;
    }

    // REFACTOR: Comparison now checks if the int is not zero.
    if (pc.invert != 0) {
        stamp_value = 1.0 - stamp_value;
    }

    float original_value = imageLoad(target_mask, pixel_coords).r;
    float blended_value = 0.0;
    
    if (pc.blend_type == 0) { // Mix
        blended_value = mix(original_value, stamp_value, pc.layer_mix);
    } else if (pc.blend_type == 1) { // Multiply
        // Note: The original_value is outside the mix for Multiply.
        blended_value = original_value * mix(1.0, stamp_value, pc.layer_mix);
    } else if (pc.blend_type == 2) { // Add
        blended_value = original_value + (stamp_value * pc.layer_mix);
    } else if (pc.blend_type == 3) { // Subtract
        blended_value = original_value - (stamp_value * pc.layer_mix);
    } else {
        // Default to Multiply if blend_type is unknown
        blended_value = original_value * mix(1.0, stamp_value, pc.layer_mix);
    }

    imageStore(target_mask, pixel_coords, vec4(blended_value, 0.0, 0.0, 0.0));
}