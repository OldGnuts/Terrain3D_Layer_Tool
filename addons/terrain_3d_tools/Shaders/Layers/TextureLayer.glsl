// REFACTORED SHADER: res://addons/terrain_3d_tools/Shaders/Layers/TextureLayer.glsl
#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// BINDINGS (Simplified)
// The control map we will read from and write to.
layout(set = 0, binding = 0, r32f) uniform image2D control_map;

// The pre-calculated layer mask, which provides the final strength (0.0 to 1.0).
layout(set = 0, binding = 1) uniform sampler2D layer_mask_sampler;

// PUSH CONSTANTS (Simplified)
layout(push_constant, std430) uniform Params {
    ivec2 region_min_px;
    ivec2 mask_min_px;
    ivec2 layer_size_px;
    uint  base_texture_id;
} pc;

// --- CONTROL MAP BITPACKING UTILITIES (Unchanged and Correct) ---
const int BASE_ID_SHIFT    = 27; const uint BASE_ID_MASK    = 0x1Fu;
const int OVERLAY_ID_SHIFT = 22; const uint OVERLAY_ID_MASK = 0x1Fu;
const int BLEND_SHIFT      = 14; const uint BLEND_MASK      = 0xFFu;

void decode_base(uint packed, out uint base_id, out uint blend) {
    base_id = (packed >> BASE_ID_SHIFT) & BASE_ID_MASK;
    blend   = (packed >> BLEND_SHIFT)   & BLEND_MASK;
}

uint encode_blend(uint packed, uint new_base_id, uint new_blend) {
    uint clear_mask = ~((BASE_ID_MASK << BASE_ID_SHIFT) | (BLEND_MASK << BLEND_SHIFT));
    packed &= clear_mask;
    packed |= (new_base_id << BASE_ID_SHIFT) | (new_blend << BLEND_SHIFT);
    return packed;
}

// --- MAIN EXECUTION ---
void main() {
    ivec2 region_px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 region_size = imageSize(control_map);

    if (region_px.x >= region_size.x || region_px.y >= region_size.y) {
        return;
    }

    // 1. CALCULATE THE MASK STRENGTH FROM THE PRE-CALCULATED TEXTURE
    ivec2 mask_px = region_px - pc.region_min_px + pc.mask_min_px;
    vec2 mask_uv = (vec2(mask_px) + 0.5) / vec2(pc.layer_size_px);

    // If we are outside the layer's bounds, do nothing.
    if (mask_uv.x < 0.0 || mask_uv.x > 1.0 || mask_uv.y < 0.0 || mask_uv.y > 1.0) {
        return;
    }
    
    // Sample the final strength. This value is the result of all masks (stamps, slopes, etc.).
    float final_strength = texture(layer_mask_sampler, mask_uv).r;

    // If the effect has no strength here, we can exit early.
    if (final_strength <= 0.0) {
        return;
    }

    // 2. READ, MODIFY, AND WRITE THE CONTROL MAP
    uint packed_val = floatBitsToUint(imageLoad(control_map, region_px).r);
    
    uint current_base_id;
    uint current_blend;
    decode_base(packed_val, current_base_id, current_blend);

    // If we are painting onto a different base texture, the blend target is 255 (fully this texture).
    // If we are painting onto the same base texture, we respect the current blend value.
    uint target_blend = (current_base_id != pc.base_texture_id) ? 255u : current_blend;

    // Linearly interpolate the blend value based on the final strength from our mask.
    uint new_blend = uint(mix(float(current_blend), float(target_blend), final_strength));

    // Encode the new base ID and blend value back into a packed integer.
    uint new_packed_val = encode_blend(packed_val, pc.base_texture_id, new_blend);

    // Store the result back into the control map.
    imageStore(control_map, region_px, vec4(uintBitsToFloat(new_packed_val)));
}