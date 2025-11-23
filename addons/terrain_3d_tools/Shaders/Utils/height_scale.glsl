#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform readonly image2D source_height;
layout(set = 0, binding = 1, r32f) uniform writeonly image2D target_height;

layout(push_constant) uniform PushConstants {
    float height_scale;
    int pad0;
    int pad1;
    int pad2;
} pc;

void main() {
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(source_height);
    
    if (coord.x >= size.x || coord.y >= size.y) {
        return;
    }
    
    // Read height value (in -1 to 1 range)
    float height = imageLoad(source_height, coord).r;
    
    // Apply height scale
    float scaled_height = height * pc.height_scale;
    
    // Store scaled value
    imageStore(target_height, coord, vec4(scaled_height, 0.0, 0.0, 1.0));
}