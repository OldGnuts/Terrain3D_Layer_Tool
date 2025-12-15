#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// BINDINGS (Unchanged)
layout(set = 0, binding = 0, r32f) uniform image2D target_mask;
layout(set = 0, binding = 1) uniform sampler2D stitched_heightmap;

// PUSH CONSTANTS
layout(push_constant) uniform PushConstants {
    int blend_type;
    float layer_mix;
    int invert;
    float min_slope;
    float max_slope;
    // REPLACED: The single falloff is now two separate values.
    float min_slope_falloff;
    float max_slope_falloff;
    float height_scale;
    float vertex_spacing;
    int padding0;
    int padding1;
    int padding2;
} pc;

void main() {
    ivec2 pixel_coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_mask);

    if (pixel_coords.x >= size.x || pixel_coords.y >= size.y) {
        return;
    }

    // Slope calculation logic remains the same.
    float h_tl = texelFetch(stitched_heightmap, pixel_coords + ivec2(-1, -1), 0).r;
    float h_t  = texelFetch(stitched_heightmap, pixel_coords + ivec2( 0, -1), 0).r;
    float h_tr = texelFetch(stitched_heightmap, pixel_coords + ivec2( 1, -1), 0).r;
    float h_l  = texelFetch(stitched_heightmap, pixel_coords + ivec2(-1,  0), 0).r;
    float h_r  = texelFetch(stitched_heightmap, pixel_coords + ivec2( 1,  0), 0).r;
    float h_bl = texelFetch(stitched_heightmap, pixel_coords + ivec2(-1,  1), 0).r;
    float h_b  = texelFetch(stitched_heightmap, pixel_coords + ivec2( 0,  1), 0).r;
    float h_br = texelFetch(stitched_heightmap, pixel_coords + ivec2( 1,  1), 0).r;

    float dz_dx_normalized = (h_tr + 2.0 * h_r + h_br) - (h_tl + 2.0 * h_l + h_bl);
    float dz_dy_normalized = (h_bl + 2.0 * h_b + h_br) - (h_tl + 2.0 * h_t + h_tr);
    float normalized_rise = length(vec2(dz_dx_normalized, dz_dy_normalized));

    float world_rise = normalized_rise * pc.height_scale;
    float world_run = 8.0 * pc.vertex_spacing;

    float slope = degrees(atan(world_rise, world_run));

    // --- NEW GRANULAR FALLOFF LOGIC ---

    // 1. Define the lower transition zone using the minimum falloff.
    float min_edge = pc.min_slope - pc.min_slope_falloff;
    // 2. Define the upper transition zone using the maximum falloff.
    float max_edge = pc.max_slope + pc.max_slope_falloff;
    
    // 3. Calculate the "fade-in" from the lower edge. Value goes from 0 to 1.
    float lower_blend = smoothstep(min_edge, pc.min_slope, slope);
    // 4. Calculate the "fade-out" from the upper edge. Value goes from 1 to 0.
    float upper_blend = 1.0 - smoothstep(pc.max_slope, max_edge, slope);
    
    // 5. The final mask value is the product of both blends.
    // This ensures it's only 1.0 when *both* conditions are fully met.
    float slope_filter = lower_blend * upper_blend;

    if (pc.invert != 0) {
        slope_filter = 1.0 - slope_filter;
    }
    
    // Blending logic remains the same.
    float original_value = imageLoad(target_mask, pixel_coords).r;
    float blended_value;
    
    if (pc.blend_type == 0) { // Mix
        blended_value = mix(original_value, slope_filter, pc.layer_mix);
    } else if (pc.blend_type == 1) { // Multiply
        blended_value = original_value * mix(1.0, slope_filter, pc.layer_mix);
    } else if (pc.blend_type == 2) { // Add
        blended_value = original_value + (slope_filter * pc.layer_mix);
    } else if (pc.blend_type == 3) { // Subtract
        blended_value = original_value - (slope_filter * pc.layer_mix);
    } else {
        blended_value = original_value * mix(1.0, slope_filter, pc.layer_mix);
    }
    
    imageStore(target_mask, pixel_coords, vec4(clamp(blended_value, 0.0, 1.0)));
}