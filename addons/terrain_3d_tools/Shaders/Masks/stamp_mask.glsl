// /Shaders/Masks/stamp_mask.glsl
#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D target_mask;
layout(set = 0, binding = 1) uniform sampler2D stamp_texture;

layout(push_constant) uniform PushConstants {
    // First 16 bytes
    int blend_type;     // 4 bytes
    float layer_mix;    // 4 bytes
    int invert;         // 4 bytes
    int sample_mode;    // 4 bytes
    
    // Second 16 bytes
    int flip_x;         // 4 bytes
    int flip_y;         // 4 bytes
    int _padding0;      // 4 bytes
    int _padding1;      // 4 bytes
} pc;

void main() {
    ivec2 pixel_coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_mask);

    if (pixel_coords.x >= size.x || pixel_coords.y >= size.y) {
        return;
    }

    vec2 uv = (vec2(pixel_coords) + 0.5) / vec2(size);
    
    if (pc.flip_x != 0) {
        uv.x = 1.0 - uv.x;
    }
    if (pc.flip_y != 0) {
        uv.y = 1.0 - uv.y;
    }

    vec4 stamp_color = texture(stamp_texture, uv);

    float stamp_value = 0.0;
    switch (pc.sample_mode) {
        case 0: // Luminance
            stamp_value = dot(stamp_color.rgb, vec3(0.299, 0.587, 0.114));
            break;
        case 1: // Red
            stamp_value = stamp_color.r;
            break;
        case 2: // Green
            stamp_value = stamp_color.g;
            break;
        case 3: // Blue
            stamp_value = stamp_color.b;
            break;
        case 4: // Alpha
            stamp_value = stamp_color.a;
            break;
        default:
            stamp_value = stamp_color.r;
            break;
    }

    if (pc.invert != 0) {
        stamp_value = 1.0 - stamp_value;
    }

    float original_value = imageLoad(target_mask, pixel_coords).r;
    float blended_value;
    
    switch (pc.blend_type) {
        case 0: // Mix
            blended_value = mix(original_value, stamp_value, pc.layer_mix);
            break;
        case 1: // Multiply
            blended_value = original_value * mix(1.0, stamp_value, pc.layer_mix);
            break;
        case 2: // Add
            blended_value = original_value + (stamp_value * pc.layer_mix);
            break;
        case 3: // Subtract
            blended_value = original_value - (stamp_value * pc.layer_mix);
            break;
        default:
            blended_value = original_value * mix(1.0, stamp_value, pc.layer_mix);
            break;
    }

    imageStore(target_mask, pixel_coords, vec4(clamp(blended_value, 0.0, 1.0), 0.0, 0.0, 0.0));
}