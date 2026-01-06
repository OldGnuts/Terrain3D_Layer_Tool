#[compute]
#version 450

/*
 * TextureLayerPass2.glsl
 * 
 * Enhanced smoothing pass for zone-based system.
 * 
 * Focuses on:
 * - Smoothing blend values among same-pair neighbors
 * - Extra smoothing at zone boundaries and falloff edges
 * - Creating softer transitions
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// =============================================================================
// BINDINGS
// =============================================================================

layout(set = 0, binding = 0, r32f) uniform image2D control_map;
layout(set = 0, binding = 1, r32ui) readonly uniform uimage2D metadata_map;

// =============================================================================
// PUSH CONSTANTS
// =============================================================================

layout(push_constant, std430) uniform SmoothingParams {
    float blend_smoothing;
    float boundary_smoothing;
    float falloff_edge_smoothing;
    float _reserved;
    int   window_size;
    int   region_size;
    uint  _pad0;
    uint  _pad1;
} params;

// =============================================================================
// CONSTANTS
// =============================================================================

const int  BASE_ID_SHIFT    = 27;
const uint BASE_ID_MASK     = 0x1Fu;
const int  OVERLAY_ID_SHIFT = 22;
const uint OVERLAY_ID_MASK  = 0x1Fu;
const int  BLEND_SHIFT      = 14;
const uint BLEND_MASK       = 0xFFu;

const int  ZONE_SHIFT          = 0;
const int  ZONE_T_SHIFT        = 4;
const int  BLEND_VALUE_SHIFT   = 12;
const int  FLAGS_SHIFT         = 24;

const uint FLAG_GRADIENT_MODE  = 0x01u;
const uint FLAG_ZONE_BOUNDARY  = 0x02u;
const uint FLAG_NOISE_APPLIED  = 0x04u;
const uint FLAG_FALLOFF_EDGE   = 0x08u;

// =============================================================================
// ENCODING/DECODING
// =============================================================================

void decode_control(uint packed, out uint base_id, out uint overlay_id, out uint blend) {
    base_id    = (packed >> BASE_ID_SHIFT)    & BASE_ID_MASK;
    overlay_id = (packed >> OVERLAY_ID_SHIFT) & OVERLAY_ID_MASK;
    blend      = (packed >> BLEND_SHIFT)      & BLEND_MASK;
}

uint encode_control(uint packed, uint base_id, uint overlay_id, uint blend) {
    uint clear_mask = ~(
        (BASE_ID_MASK << BASE_ID_SHIFT) | 
        (OVERLAY_ID_MASK << OVERLAY_ID_SHIFT) | 
        (BLEND_MASK << BLEND_SHIFT)
    );
    packed &= clear_mask;
    packed |= (base_id & BASE_ID_MASK) << BASE_ID_SHIFT;
    packed |= (overlay_id & OVERLAY_ID_MASK) << OVERLAY_ID_SHIFT;
    packed |= (blend & BLEND_MASK) << BLEND_SHIFT;
    return packed;
}

void decode_metadata(uint metadata, out uint zone, out float zone_t, out uint blend_val, out uint flags) {
    zone = (metadata >> ZONE_SHIFT) & 0xFu;
    zone_t = float((metadata >> ZONE_T_SHIFT) & 0xFFu) / 255.0;
    blend_val = (metadata >> BLEND_VALUE_SHIFT) & 0xFFu;
    flags = (metadata >> FLAGS_SHIFT) & 0xFFu;
}

// =============================================================================
// PAIR COMPARISON
// =============================================================================

bool same_pair(uint base_a, uint overlay_a, uint base_b, uint overlay_b) {
    return (base_a == base_b && overlay_a == overlay_b);
}

bool same_pair_or_swapped(uint base_a, uint overlay_a, uint base_b, uint overlay_b) {
    return same_pair(base_a, overlay_a, base_b, overlay_b) ||
           (base_a == overlay_b && overlay_a == base_b);
}

// =============================================================================
// SMOOTHING
// =============================================================================

struct SmoothingResult {
    float smoothed_blend;
    int same_pair_count;
    int compatible_pair_count;
    float neighbor_blend_sum;
    float neighbor_blend_min;
    float neighbor_blend_max;
};

SmoothingResult analyze_neighborhood(
    ivec2 center_coord,
    uint center_base,
    uint center_overlay,
    uint center_blend,
    ivec2 map_size
) {
    SmoothingResult result;
    result.same_pair_count = 0;
    result.compatible_pair_count = 0;
    result.neighbor_blend_sum = 0.0;
    result.neighbor_blend_min = 255.0;
    result.neighbor_blend_max = 0.0;
    
    int window = params.window_size;
    
    for (int dy = -window; dy <= window; dy++) {
        for (int dx = -window; dx <= window; dx++) {
            if (dx == 0 && dy == 0) continue;
            
            ivec2 neighbor_coord = center_coord + ivec2(dx, dy);
            
            if (neighbor_coord.x < 0 || neighbor_coord.x >= map_size.x ||
                neighbor_coord.y < 0 || neighbor_coord.y >= map_size.y) {
                continue;
            }
            
            uint n_packed = floatBitsToUint(imageLoad(control_map, neighbor_coord).r);
            uint n_base, n_overlay, n_blend;
            decode_control(n_packed, n_base, n_overlay, n_blend);
            
            // Check for exact same pair
            if (same_pair(center_base, center_overlay, n_base, n_overlay)) {
                result.same_pair_count++;
                result.neighbor_blend_sum += float(n_blend);
                result.neighbor_blend_min = min(result.neighbor_blend_min, float(n_blend));
                result.neighbor_blend_max = max(result.neighbor_blend_max, float(n_blend));
            }
            // Check for swapped pair (compatible for weighted averaging)
            else if (same_pair_or_swapped(center_base, center_overlay, n_base, n_overlay)) {
                result.compatible_pair_count++;
                // For swapped pairs, invert the blend
                float adjusted_blend = 255.0 - float(n_blend);
                result.neighbor_blend_sum += adjusted_blend;
                result.neighbor_blend_min = min(result.neighbor_blend_min, adjusted_blend);
                result.neighbor_blend_max = max(result.neighbor_blend_max, adjusted_blend);
            }
        }
    }
    
    // Calculate smoothed blend
    int total_compatible = result.same_pair_count + result.compatible_pair_count;
    if (total_compatible > 0) {
        float neighbor_avg = result.neighbor_blend_sum / float(total_compatible);
        result.smoothed_blend = neighbor_avg;
    } else {
        result.smoothed_blend = float(center_blend);
    }
    
    return result;
}

// =============================================================================
// MAIN
// =============================================================================

void main() {
    ivec2 center_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 map_size = ivec2(params.region_size);
    
    if (center_coord.x >= map_size.x || center_coord.y >= map_size.y) {
        return;
    }
    
    // Read center pixel
    uint packed = floatBitsToUint(imageLoad(control_map, center_coord).r);
    uint center_base, center_overlay, center_blend;
    decode_control(packed, center_base, center_overlay, center_blend);
    
    // Read metadata
    uint metadata = imageLoad(metadata_map, center_coord).r;
    uint zone, blend_val, flags;
    float zone_t;
    decode_metadata(metadata, zone, zone_t, blend_val, flags);
    
    // Skip if not in gradient mode
    if ((flags & FLAG_GRADIENT_MODE) == 0u) {
        return;
    }
    
    // Determine smoothing amount based on context
    float smooth_amount = params.blend_smoothing;
    
    // Extra smoothing at zone boundaries
    if ((flags & FLAG_ZONE_BOUNDARY) != 0u) {
        smooth_amount = max(smooth_amount, params.boundary_smoothing);
    }
    
    // Extra smoothing at falloff edges
    if ((flags & FLAG_FALLOFF_EDGE) != 0u) {
        smooth_amount = max(smooth_amount, params.falloff_edge_smoothing);
    }
    
    // Skip if no smoothing needed
    if (smooth_amount <= 0.001) {
        return;
    }
    
    // Analyze neighborhood
    SmoothingResult sr = analyze_neighborhood(
        center_coord,
        center_base, center_overlay, center_blend,
        map_size
    );
    
    // Apply smoothing if we have compatible neighbors
    int total_compatible = sr.same_pair_count + sr.compatible_pair_count;
    if (total_compatible > 0) {
        float current = float(center_blend);
        float target = sr.smoothed_blend;
        
        // Weighted smoothing based on neighbor count (more neighbors = more confidence)
        float confidence = clamp(float(total_compatible) / 8.0, 0.0, 1.0);
        float effective_smooth = smooth_amount * confidence;
        
        float final_blend = mix(current, target, effective_smooth);
        
        // Clamp and convert
        uint new_blend = uint(clamp(final_blend, 0.0, 255.0));
        
        // Write updated control map
        uint new_packed = encode_control(packed, center_base, center_overlay, new_blend);
        imageStore(control_map, center_coord, vec4(uintBitsToFloat(new_packed)));
    }
}