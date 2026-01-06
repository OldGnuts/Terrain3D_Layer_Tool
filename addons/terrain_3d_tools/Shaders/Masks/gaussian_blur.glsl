// Shaders/Masks/gaussian_blur.glsl
#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D target_image;
layout(set = 0, binding = 1) uniform sampler2D source_texture;

layout(push_constant) uniform PushConstants {
    int direction_x;        // 4 bytes
    int direction_y;        // 4 bytes
    float sample_distance;  // 4 bytes (multiplier for texel offset)
    float _padding;         // 4 bytes (16-byte alignment)
} pc;

// 7-tap Gaussian weights (sigma ~1.6)
const float weights[4] = float[](0.38774, 0.24477, 0.06136, 0.00598);

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_image);
    
    if (p.x >= size.x || p.y >= size.y) {
        return;
    }
    
    vec2 uv = (vec2(p) + 0.5) / vec2(size);
    vec2 texel_size = 1.0 / vec2(size);
    vec2 direction = vec2(float(pc.direction_x), float(pc.direction_y));
    
    // Sample distance scales the blur radius
    vec2 step = direction * texel_size * pc.sample_distance;
    
    float result = texture(source_texture, uv).r * weights[0];
    
    for (int i = 1; i < 4; ++i) {
        result += texture(source_texture, uv + step * float(i)).r * weights[i];
        result += texture(source_texture, uv - step * float(i)).r * weights[i];
    }
    
    imageStore(target_image, p, vec4(result, 0.0, 0.0, 1.0));
}