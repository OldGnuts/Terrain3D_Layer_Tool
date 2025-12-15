#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8) in;

// Define the structure for our push constants
layout(push_constant) uniform PushConstants {
    vec4 clear_color; // ADDED: The color to clear the texture with
} pc;

layout(r32f, binding = 0) uniform image2D regionTexture;

void main()
{
    ivec2 pix = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(regionTexture);
    
    if (pix.x >= size.x || pix.y >= size.y) {
        return;
    }
    
    // Clear to the color provided in the push constant
    imageStore(regionTexture, pix, pc.clear_color);
}