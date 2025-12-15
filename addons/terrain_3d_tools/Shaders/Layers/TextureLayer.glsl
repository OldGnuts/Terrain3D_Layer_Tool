// res://addons/terrain_3d_tools/Shaders/Layers/TextureLayer.glsl
#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// === BINDINGS ===
// The control map we will read from and write to.
layout(set = 0, binding = 0, r32f) uniform image2D control_map;

// The pre-calculated layer mask, which provides the final strength (0.0 to 1.0).
layout(set = 0, binding = 1) uniform sampler2D layer_mask_sampler;

// Exclusion list buffer
layout(set = 0, binding = 2, std430) restrict readonly buffer ExclusionBuffer {
    uint excluded_ids[];
} exclusions;

// === PUSH CONSTANTS ===
layout(push_constant, std430) uniform Params {
    ivec2 region_min_px;
    ivec2 mask_min_px;
    ivec2 layer_size_px;
    uint  texture_id;
    uint  exclusion_count;
} pc;

// === CONTROL MAP BITPACKING ===
const int BASE_ID_SHIFT    = 27;
const uint BASE_ID_MASK    = 0x1Fu;
const int OVERLAY_ID_SHIFT = 22;
const uint OVERLAY_ID_MASK = 0x1Fu;
const int BLEND_SHIFT      = 14;
const uint BLEND_MASK      = 0xFFu;

// Decode all relevant fields
void decode_control(uint packed, out uint base_id, out uint overlay_id, out uint blend) {
    base_id    = (packed >> BASE_ID_SHIFT)    & BASE_ID_MASK;
    overlay_id = (packed >> OVERLAY_ID_SHIFT) & OVERLAY_ID_MASK;
    blend      = (packed >> BLEND_SHIFT)      & BLEND_MASK;
}

// Encode base_id, overlay_id, and blend back into packed value (preserving other bits)
uint encode_control(uint packed, uint base_id, uint overlay_id, uint blend) {
    // Clear the bits we're modifying
    uint clear_mask = ~(
        (BASE_ID_MASK << BASE_ID_SHIFT) | 
        (OVERLAY_ID_MASK << OVERLAY_ID_SHIFT) | 
        (BLEND_MASK << BLEND_SHIFT)
    );
    packed &= clear_mask;
    
    // Set new values
    packed |= (base_id & BASE_ID_MASK) << BASE_ID_SHIFT;
    packed |= (overlay_id & OVERLAY_ID_MASK) << OVERLAY_ID_SHIFT;
    packed |= (blend & BLEND_MASK) << BLEND_SHIFT;
    
    return packed;
}

// Check if a texture ID is in the exclusion list
bool is_excluded(uint id) {
    for (uint i = 0u; i < pc.exclusion_count; i++) {
        if (exclusions.excluded_ids[i] == id) {
            return true;
        }
    }
    return false;
}

// === MAIN ===
void main() {
    ivec2 region_px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 region_size = imageSize(control_map);

    if (region_px.x >= region_size.x || region_px.y >= region_size.y) {
        return;
    }

    // Calculate mask UV
    ivec2 mask_px = region_px - pc.region_min_px + pc.mask_min_px;
    vec2 mask_uv = (vec2(mask_px) + 0.5) / vec2(pc.layer_size_px);

    // Outside layer bounds - do nothing
    if (mask_uv.x < 0.0 || mask_uv.x > 1.0 || mask_uv.y < 0.0 || mask_uv.y > 1.0) {
        return;
    }
    
    // Sample mask strength
    float strength = texture(layer_mask_sampler, mask_uv).r;

    // No effect - early exit
    if (strength <= 0.0) {
        return;
    }

    // Read current control map value
    uint packed = floatBitsToUint(imageLoad(control_map, region_px).r);
    
    uint base_id, overlay_id, blend;
    decode_control(packed, base_id, overlay_id, blend);

    uint T = pc.texture_id;
    uint new_base_id = base_id;
    uint new_overlay_id = overlay_id;
    uint new_blend = blend;

    // === CASE 1: Layer's texture matches current base_id ===
    if (T == base_id) {
        // Push blend toward 0 (strengthen base)
        float target_blend = 0.0;
        new_blend = uint(mix(float(blend), target_blend, strength));
    }
    // === CASE 2: Layer's texture matches current overlay_id ===
    else if (T == overlay_id) {
        // Push blend toward 255 (strengthen overlay)
        float target_blend = 255.0;
        new_blend = uint(mix(float(blend), target_blend, strength));
        
        // If blend crosses above 128, swap base and overlay
        if (new_blend >= 128u && blend < 128u) {
            // Check exclusion before swapping
            if (is_excluded(base_id)) {
                return; // Would overwrite excluded base_id
            }
            new_base_id = overlay_id;
            new_overlay_id = base_id;
            new_blend = 255u - new_blend;
        }
    }
    // === CASE 3: Layer's texture is different from both ===
    else if (T != base_id && T != overlay_id) {
        // Determine what we're about to overwrite
        if (blend < 128u) {
            // Base is dominant - overlay_id will become new base, T becomes overlay
            // Check if we're overwriting an excluded texture
            if (is_excluded(overlay_id)) {
                return; // Would overwrite excluded overlay (which becomes base)
            }
            if (is_excluded(base_id) && overlay_id != base_id) {
                // base_id would be lost entirely - check if that's okay
                // Actually, base_id is being replaced by overlay_id, not T
                // So we check if overlay_id becoming base overwrites base_id
            }
            
            new_base_id = overlay_id;
            new_overlay_id = T;
            new_blend = uint(strength * 255.0);
        }
        else {
            // Overlay is dominant - overlay becomes base, T becomes overlay
            if (is_excluded(base_id)) {
                return; // base_id would be lost
            }
            
            new_base_id = overlay_id;
            new_overlay_id = T;
            new_blend = uint(strength * 255.0);
        }
    }
    // === CASE 4: Empty pixel (handled implicitly) ===
    // If base=0, overlay=0, blend=0, and T != 0:
    // Falls into Case 3, overlay(0) becomes base, T becomes overlay
    // blend = strength * 255

    // Final exclusion check: make sure we're not setting an excluded ID
    if (is_excluded(new_base_id) && new_base_id != base_id) {
        return;
    }
    if (is_excluded(new_overlay_id) && new_overlay_id != overlay_id) {
        return;
    }

    // Encode and store
    uint new_packed = encode_control(packed, new_base_id, new_overlay_id, new_blend);
    imageStore(control_map, region_px, vec4(uintBitsToFloat(new_packed)));
}