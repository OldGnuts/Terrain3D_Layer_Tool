#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Target mask texture (R32F)
layout(set = 0, binding = 0, r32f) uniform image2D maskTex;

void main() {
    ivec2 uv = ivec2(gl_GlobalInvocationID.xy);

    imageStore(maskTex, uv, vec4(0.0));
}