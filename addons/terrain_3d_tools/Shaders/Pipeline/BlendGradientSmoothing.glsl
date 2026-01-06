#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// === BINDINGS ===
layout(set = 0, binding = 0, r32f) uniform image2D control_map;

layout(set = 0, binding = 1, std140) uniform SmoothingSettings {
    // Block 1: Core settings (16 bytes)
    float smoothing_strength;      // How much to blend toward neighbor average
    float isolation_threshold;     // 0-1: ratio of non-matching neighbors to be "isolated"
    float isolation_blend_target;  // Blend value to push isolated pixels toward (0-255)
    float isolation_strength;      // How strongly to push isolated pixels toward target
    
    // Block 2: Additional settings (16 bytes)
    uint  min_blend_for_smoothing; // Only smooth pixels with blend >= this
    uint  consider_swapped_pairs;  // 1 = treat (A,B) and (B,A) as matching pairs
    uint  _pad0;
    uint  _pad1;
} settings;

layout(push_constant, std430) uniform Params {
    uint region_width;
    uint region_height;
    uint _pad0;
    uint _pad1;
} pc;

// === CONTROL MAP BITPACKING ===
const int BASE_ID_SHIFT    = 27;
const uint BASE_ID_MASK    = 0x1Fu;
const int OVERLAY_ID_SHIFT = 22;
const uint OVERLAY_ID_MASK = 0x1Fu;
const int BLEND_SHIFT      = 14;
const uint BLEND_MASK      = 0xFFu;

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

// 8-neighbor offsets with weights (cardinal = 1.0, diagonal = 0.707)
const ivec2 NEIGHBOR_OFFSETS[8] = ivec2[8](
    ivec2(-1, -1), ivec2(0, -1), ivec2(1, -1),
    ivec2(-1,  0),               ivec2(1,  0),
    ivec2(-1,  1), ivec2(0,  1), ivec2(1,  1)
);

const float NEIGHBOR_WEIGHTS[8] = float[8](
    0.707, 1.0, 0.707,
    1.0,        1.0,
    0.707, 1.0, 0.707
);

bool is_valid_coord(ivec2 pos) {
    return pos.x >= 0 && pos.x < int(pc.region_width) &&
           pos.y >= 0 && pos.y < int(pc.region_height);
}

// Check if two texture pairs match (considering swapped equivalence)
bool pairs_match(uint base_a, uint overlay_a, uint base_b, uint overlay_b) {
    // Exact match
    if (base_a == base_b && overlay_a == overlay_b) {
        return true;
    }
    
    // Swapped match (if enabled)
    if (settings.consider_swapped_pairs != 0u) {
        if (base_a == overlay_b && overlay_a == base_b) {
            return true;
        }
    }
    
    return false;
}

// Check if pairs are swapped relative to each other
bool pairs_are_swapped(uint base_a, uint overlay_a, uint base_b, uint overlay_b) {
    return (base_a == overlay_b && overlay_a == base_b);
}

struct NeighborAnalysis {
    int   valid_count;
    int   matching_count;
    float blend_sum;
    float weight_sum;
};

NeighborAnalysis analyze_neighbors(ivec2 pixel, uint my_base, uint my_overlay) {
    NeighborAnalysis result;
    result.valid_count = 0;
    result.matching_count = 0;
    result.blend_sum = 0.0;
    result.weight_sum = 0.0;
    
    for (int i = 0; i < 8; i++) {
        ivec2 n_pos = pixel + NEIGHBOR_OFFSETS[i];
        
        if (!is_valid_coord(n_pos)) {
            continue;
        }
        
        result.valid_count++;
        
        uint n_packed = floatBitsToUint(imageLoad(control_map, n_pos).r);
        uint n_base, n_overlay, n_blend;
        decode_control(n_packed, n_base, n_overlay, n_blend);
        
        // Check if this neighbor has a matching texture pair
        if (pairs_match(my_base, my_overlay, n_base, n_overlay)) {
            result.matching_count++;
            
            float weight = NEIGHBOR_WEIGHTS[i];
            
            // If pairs are swapped, invert the blend for proper averaging
            float neighbor_blend = float(n_blend);
            if (pairs_are_swapped(my_base, my_overlay, n_base, n_overlay)) {
                neighbor_blend = 255.0 - neighbor_blend;
            }
            
            result.blend_sum += neighbor_blend * weight;
            result.weight_sum += weight;
        }
    }
    
    return result;
}

void main() {
    ivec2 pixel = ivec2(gl_GlobalInvocationID.xy);
    
    if (pixel.x >= int(pc.region_width) || pixel.y >= int(pc.region_height)) {
        return;
    }
    
    // Read current pixel
    uint packed = floatBitsToUint(imageLoad(control_map, pixel).r);
    uint base_id, overlay_id, blend;
    decode_control(packed, base_id, overlay_id, blend);
    
    // Skip pixels with same base/overlay (no blending happening)
    if (base_id == overlay_id) {
        return;
    }
    
    // Skip pixels below minimum blend threshold
    if (blend < settings.min_blend_for_smoothing) {
        return;
    }
    
    // Analyze neighbors
    NeighborAnalysis neighbors = analyze_neighbors(pixel, base_id, overlay_id);
    
    if (neighbors.valid_count == 0) {
        return;
    }
    
    float current_blend = float(blend);
    float new_blend = current_blend;
    
    // === STEP 1: GRADIENT SMOOTHING ===
    // Blend toward neighbor average if we have matching neighbors
    if (neighbors.matching_count > 0 && neighbors.weight_sum > 0.001 && settings.smoothing_strength > 0.0) {
        float neighbor_avg = neighbors.blend_sum / neighbors.weight_sum;
        
        // Interpolate toward neighbor average
        new_blend = mix(current_blend, neighbor_avg, settings.smoothing_strength);
    }
    
    // === STEP 2: ISOLATION SOFTENING ===
    // If pixel is isolated (few matching neighbors), push toward neutral blend
    float isolation = 1.0 - (float(neighbors.matching_count) / float(neighbors.valid_count));
    
    if (isolation >= settings.isolation_threshold && settings.isolation_strength > 0.0) {
        // Calculate how strongly to push toward target based on isolation level
        float isolation_factor = (isolation - settings.isolation_threshold) / (1.0 - settings.isolation_threshold);
        isolation_factor = clamp(isolation_factor, 0.0, 1.0);
        
        float push_strength = isolation_factor * settings.isolation_strength;
        
        // Push toward isolation blend target (typically 127.5 for 50/50)
        new_blend = mix(new_blend, settings.isolation_blend_target, push_strength);
    }
    
    // === STEP 3: WRITE RESULT ===
    uint final_blend = uint(clamp(new_blend, 0.0, 255.0));
    
    // Only write if blend actually changed
    if (final_blend != blend) {
        uint new_packed = encode_control(packed, base_id, overlay_id, final_blend);
        imageStore(control_map, pixel, vec4(uintBitsToFloat(new_packed)));
    }
}