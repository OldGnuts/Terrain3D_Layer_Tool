#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Binding 0: The texture we are reading from
layout(set = 0, binding = 0) uniform sampler2D source_texture;

// Binding 1: The texture we are writing to
layout(set = 0, binding = 1, r32f) uniform image2D destination_image;

void main() {
    ivec2 p = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(destination_image);
    if (p.x >= size.x || p.y >= size.y) {
        return;
    }

    // Read a pixel from the source
    vec2 uv = (vec2(p) + 0.5) / vec2(size);
    float value = texture(source_texture, uv).r;

    // Write it to the destination
    imageStore(destination_image, p, vec4(value, 0.0, 0.0, 1.0));
}