// Shaders/Masks/blur_blend.glsl
#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D target_image;
layout(set = 0, binding = 1) uniform sampler2D source_texture;

layout(push_constant) uniform PushConstants {
    int blend_type;     // 0=Mix, 1=Multiply, 2=Add, 3=Subtract
    float mix_amount;
    int invert;
    int _padding;
} pc;

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_image);
    
    if (p.x >= size.x || p.y >= size.y) return;
    
    vec2 uv = (vec2(p) + 0.5) / vec2(size);
    
    float blurred = texture(source_texture, uv).r;
    float existing = imageLoad(target_image, p).r;
    
    // Apply invert
    if (pc.invert != 0) {
        blurred = 1.0 - blurred;
    }
    
    // Apply blend
    float result;
    switch (pc.blend_type) {
        case 0: // Mix
            result = mix(existing, blurred, pc.mix_amount);
            break;
        case 1: // Multiply
            result = existing * mix(1.0, blurred, pc.mix_amount);
            break;
        case 2: // Add
            result = existing + blurred * pc.mix_amount;
            break;
        case 3: // Subtract
            result = existing - blurred * pc.mix_amount;
            break;
        default:
            result = blurred;
            break;
    }
    
    imageStore(target_image, p, vec4(clamp(result, 0.0, 1.0), 0.0, 0.0, 1.0));
}