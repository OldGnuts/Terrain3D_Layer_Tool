#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0) uniform sampler2D u_source;
layout(set = 0, binding = 1, r32f) uniform restrict writeonly image2D u_destination;

void main() {
    ivec2 pos = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(u_destination);
    
    if (pos.x >= size.x || pos.y >= size.y) return;
    
    vec2 uv = (vec2(pos) + 0.5) / vec2(size);
    float value = texture(u_source, uv).r;
    
    imageStore(u_destination, pos, vec4(value, 0.0, 0.0, 0.0));
}