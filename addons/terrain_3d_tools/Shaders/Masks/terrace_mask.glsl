#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// BINDINGS
layout(set = 0, binding = 0, r32f) uniform image2D target_mask;
layout(set = 0, binding = 1) uniform sampler2D height_source;

// PUSH CONSTANTS
layout(push_constant) uniform PushConstants {
    int blend_type;
    float layer_mix;
    int invert;
    int mode; // 0: OutputTerracedHeight, 1: MaskTreads, 2: MaskRisers
    int terrace_count;
    float sharpness;
} pc;

void main() {
    ivec2 pixel_coords = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(target_mask);

    if (pixel_coords.x >= size.x || pixel_coords.y >= size.y) {
        return;
    }

    vec2 uv = (vec2(pixel_coords) + 0.5) / vec2(size);
    float input_height = texture(height_source, uv).r;

    // --- TERRACE CALCULATION ---
    float h = input_height;
    
    // Scale height by the number of terraces to get a value where each integer is a step
    float scaled_h = h * float(pc.terrace_count);
    
    // Get the fractional part, which represents the progress within the current step (0.0 to 1.0)
    float step_progress = fract(scaled_h);
    
    // Get the base of the current step
    float step_base = floor(scaled_h);

    // --- SHARPNESS & TRANSITION ---
    // Calculate the start of the riser based on sharpness.
    // At sharpness = 1.0, the riser starts at 1.0 (instant transition).
    // At sharpness = 0.0, the riser starts at 0.0 (smooth linear slope).
    float riser_start = pc.sharpness;
    
    // 'blend_factor' determines how far we are into the riser transition.
    // It's 0.0 on the tread and ramps up to 1.0 on the riser.
    float blend_factor = smoothstep(riser_start, 1.0, step_progress);
    
    // --- MODE LOGIC ---
    float output_value = 0.0;
    
    if (pc.mode == 0) { // OutputTerracedHeight
        // The final height is a blend between the current step and the next step,
        // controlled by our sharpness-derived blend_factor.
        float terraced_height = (step_base + blend_factor) / float(pc.terrace_count);
        output_value = terraced_height;
    } else if (pc.mode == 1) { // MaskTreads (the flat parts)
        // The tread is where the blend_factor is 0. We invert it to get a 1.0 value.
        output_value = 1.0 - blend_factor;
    } else { // MaskRisers (the steep parts)
        // The riser is where the blend_factor is 1.
        output_value = blend_factor;
    }

    if (pc.invert != 0) {
        output_value = 1.0 - output_value;
    }

    // --- STANDARD BLENDING ---
    float original_value = imageLoad(target_mask, pixel_coords).r;
    float blended_value;
    
    if (pc.blend_type == 0) { // Mix
        blended_value = mix(original_value, output_value, pc.layer_mix);
    } else if (pc.blend_type == 1) { // Multiply
        // For OutputTerracedHeight, multiply doesn't make sense as a primary blend,
        // but we keep it for consistency. It will scale the existing height.
        blended_value = original_value * mix(1.0, output_value, pc.layer_mix);
    } else if (pc.blend_type == 2) { // Add
        blended_value = original_value + (output_value * pc.layer_mix);
    } else if (pc.blend_type == 3) { // Subtract
        blended_value = original_value - (output_value * pc.layer_mix);
    } else {
        blended_value = original_value * mix(1.0, output_value, pc.layer_mix);
    }
    
    // When outputting height, we don't necessarily want to clamp to 1.0
    if (pc.mode == 0) {
        imageStore(target_mask, pixel_coords, vec4(blended_value));
    } else {
        imageStore(target_mask, pixel_coords, vec4(clamp(blended_value, 0.0, 1.0)));
    }
}
