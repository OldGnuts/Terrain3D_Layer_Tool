// Create new file: gaussian_blur.glsl
#[compute]
#version 450
layout(local_size_x = 256, local_size_y = 1, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D target_image;
layout(set = 0, binding = 1) uniform sampler2D source_texture;

layout(push_constant) uniform PushConstants {
    ivec2 direction; // (1,0) for horizontal, (0,1) for vertical
} pc;

// 7-tap Gaussian weights
const float weights[4] = float[](0.38774, 0.24477, 0.06136, 0.00598);

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_image);
    if (p.x >= size.x || p.y >= size.y) {
        return;
    }
    
    vec2 uv = (vec2(p) + 0.5) / vec2(size);
    vec2 texel_size = 1.0 / vec2(size);
    
    float result = texture(source_texture, uv).r * weights[0];
    
    for (int i = 1; i < 4; ++i) {
        result += texture(source_texture, uv + vec2(pc.direction) * float(i) * texel_size).r * weights[i];
        result += texture(source_texture, uv - vec2(pc.direction) * float(i) * texel_size).r * weights[i];
    }
    
    // MODIFIED: Store the float result in the .r channel of the vec4.
    imageStore(target_image, p, vec4(result, 0.0, 0.0, 1.0));
}