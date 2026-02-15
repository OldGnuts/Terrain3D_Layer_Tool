#[compute]
#version 450

/*
 * apply_texture_edit.glsl
 * 
 * Unified texture edit application - IDEMPOTENT design.
 * 
 * The intent shader handles competition by reducing opponent weights.
 * This shader simply applies the winning weight to blend.
 * 
 * Intent format (packed uint32):
 *   Bits 0-4:   Desired overlay ID (0-31)
 *   Bit 5:      Overlay edit active
 *   Bits 6-13:  Overlay weight (0-255)
 *   Bits 14-18: Desired base ID (0-31)
 *   Bit 19:     Base edit active
 *   Bits 20-27: Base weight (0-255)
 *   Bits 28-31: Reserved
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(r32f, set = 0, binding = 0) uniform image2D u_control_map;
layout(r32f, set = 0, binding = 1) uniform readonly image2D u_texture_edit;

layout(push_constant, std430) uniform Params {
    int u_region_size;                  // 0
    int overlay_min_visible_blend;      // 4
    int base_max_visible_blend;         // 8
    int base_override_threshold;        // 12  (unused now, kept for compatibility)
    int overlay_override_threshold;     // 16  (unused now, kept for compatibility)
    float blend_reduction_rate;         // 20  (unused now, kept for compatibility)
    int _pad0;                          // 24
    int _pad1;                          // 28
    // Total: 32 bytes
} pc;

// Intent format bit layout
const int  OVERLAY_ID_SHIFT      = 0;
const uint OVERLAY_ID_MASK       = 0x1Fu;
const int  OVERLAY_ACTIVE_SHIFT  = 5;
const int  OVERLAY_WEIGHT_SHIFT  = 6;
const uint OVERLAY_WEIGHT_MASK   = 0xFFu;
const int  BASE_ID_SHIFT         = 14;
const uint BASE_ID_MASK          = 0x1Fu;
const int  BASE_ACTIVE_SHIFT     = 19;
const int  BASE_WEIGHT_SHIFT     = 20;
const uint BASE_WEIGHT_MASK      = 0xFFu;

// Terrain3D control map bit layout
const int  CTRL_BASE_ID_SHIFT    = 27;
const uint CTRL_BASE_ID_MASK     = 0x1Fu;
const int  CTRL_OVERLAY_ID_SHIFT = 22;
const uint CTRL_OVERLAY_ID_MASK  = 0x1Fu;
const int  CTRL_BLEND_SHIFT      = 14;
const uint CTRL_BLEND_MASK       = 0xFFu;

// Minimum weight to apply any changes
const uint MIN_WEIGHT_THRESHOLD = 16u;

void decode_control(uint packed, out uint base_id, out uint overlay_id, out uint blend) {
    base_id    = (packed >> CTRL_BASE_ID_SHIFT)    & CTRL_BASE_ID_MASK;
    overlay_id = (packed >> CTRL_OVERLAY_ID_SHIFT) & CTRL_OVERLAY_ID_MASK;
    blend      = (packed >> CTRL_BLEND_SHIFT)      & CTRL_BLEND_MASK;
}

uint encode_control(uint packed, uint base_id, uint overlay_id, uint blend) {
    uint clear_mask = ~(
        (CTRL_BASE_ID_MASK << CTRL_BASE_ID_SHIFT) | 
        (CTRL_OVERLAY_ID_MASK << CTRL_OVERLAY_ID_SHIFT) | 
        (CTRL_BLEND_MASK << CTRL_BLEND_SHIFT)
    );
    packed &= clear_mask;
    packed |= (base_id & CTRL_BASE_ID_MASK) << CTRL_BASE_ID_SHIFT;
    packed |= (overlay_id & CTRL_OVERLAY_ID_MASK) << CTRL_OVERLAY_ID_SHIFT;
    packed |= (blend & CTRL_BLEND_MASK) << CTRL_BLEND_SHIFT;
    return packed;
}

void main() {
    ivec2 px = ivec2(gl_GlobalInvocationID.xy);
    
    if (px.x >= pc.u_region_size || px.y >= pc.u_region_size) {
        return;
    }
    
    // Read intent
    uint intent = floatBitsToUint(imageLoad(u_texture_edit, px).r);
    
    if (intent == 0u) {
        return;
    }
    
    // Decode intent
    uint desired_overlay    = (intent >> OVERLAY_ID_SHIFT) & OVERLAY_ID_MASK;
    bool overlay_active     = ((intent >> OVERLAY_ACTIVE_SHIFT) & 1u) != 0u;
    uint overlay_weight     = (intent >> OVERLAY_WEIGHT_SHIFT) & OVERLAY_WEIGHT_MASK;
    uint desired_base       = (intent >> BASE_ID_SHIFT) & BASE_ID_MASK;
    bool base_active        = ((intent >> BASE_ACTIVE_SHIFT) & 1u) != 0u;
    uint base_weight        = (intent >> BASE_WEIGHT_SHIFT) & BASE_WEIGHT_MASK;
    
    // Check for adjust_blend mode
    bool is_adjust_blend = !overlay_active && !base_active && base_weight == 255u;
    
    if (!overlay_active && !base_active && !is_adjust_blend) {
        return;
    }
    
    // Below minimum threshold: no changes
    uint max_weight = max(overlay_weight, base_weight);
    if (max_weight < MIN_WEIGHT_THRESHOLD && !is_adjust_blend) {
        return;
    }
    
    // Read current control map
    uint ctrl_packed = floatBitsToUint(imageLoad(u_control_map, px).r);
    uint current_base, current_overlay, current_blend;
    decode_control(ctrl_packed, current_base, current_overlay, current_blend);
    
    uint new_base = current_base;
    uint new_overlay = current_overlay;
    uint new_blend = current_blend;
    
    // ==========================================================================
    // ADJUST_BLEND mode
    // ==========================================================================
    if (is_adjust_blend) {
        new_blend = overlay_weight;
    }
    // ==========================================================================
    // BOTH overlay and base active - winner determined by weight (competition already happened)
    // ==========================================================================
    else if (overlay_active && base_active) {
        // Higher weight wins
        if (overlay_weight >= base_weight) {
            // Overlay wins
            new_blend = overlay_weight;
            
            if (current_overlay != desired_overlay && overlay_weight >= uint(pc.overlay_min_visible_blend)) {
                new_overlay = desired_overlay;
            }
        } else {
            // Base wins
            new_blend = 255u - base_weight;
            
            if (current_base != desired_base && (255u - base_weight) <= uint(pc.base_max_visible_blend)) {
                new_overlay = current_base;
                new_base = desired_base;
            }
        }
    }
    // ==========================================================================
    // OVERLAY ONLY
    // ==========================================================================
    else if (overlay_active && overlay_weight >= MIN_WEIGHT_THRESHOLD) {
        new_blend = overlay_weight;
        
        if (current_base == desired_overlay) {
            // Desired is current base - decrease blend
            new_blend = 255u - overlay_weight;
        }
        else if (current_overlay != desired_overlay && overlay_weight >= uint(pc.overlay_min_visible_blend)) {
            new_overlay = desired_overlay;
        }
    }
    // ==========================================================================
    // BASE ONLY
    // ==========================================================================
    else if (base_active && base_weight >= MIN_WEIGHT_THRESHOLD) {
        new_blend = 255u - base_weight;
        
        if (current_overlay == desired_base) {
            // Desired is current overlay - increase blend
            new_blend = base_weight;
        }
        else if (current_base != desired_base && (255u - base_weight) <= uint(pc.base_max_visible_blend)) {
            new_overlay = current_base;
            new_base = desired_base;
        }
    }
    
    // Clamp
    new_blend = clamp(new_blend, 0u, 255u);
    
    // Write result
    uint new_packed = encode_control(ctrl_packed, new_base, new_overlay, new_blend);
    imageStore(u_control_map, px, vec4(uintBitsToFloat(new_packed), 0.0, 0.0, 1.0));
}