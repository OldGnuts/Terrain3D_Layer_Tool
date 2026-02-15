#[compute]
#version 450

/*
 * texture_brush_intent.glsl
 * 
 * Accumulates user paint intent into the TextureEdit buffer.
 * When one mode's weight exceeds a threshold, it starts reducing the opponent's weight.
 * This creates direct competition at the intent level - no priority flag needed.
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

layout(r32f, set = 0, binding = 0) uniform image2D u_texture_edit;

layout(push_constant) uniform PushConstants {
    float brush_center_x;      // 0
    float brush_center_z;      // 4
    float brush_radius;        // 8
    float strength;            // 12
    int falloff_type;          // 16
    int bounds_min_x;          // 20
    int bounds_min_y;          // 24
    int region_world_x;        // 28
    int region_world_z;        // 32
    int region_size;           // 36
    int is_circle;             // 40
    int texture_mode;          // 44
    int primary_texture_id;    // 48
    int secondary_texture_id;  // 52
    int target_blend;          // 56
    int blend_step;            // 60
    // Total: 64 bytes
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

// Modes
const uint MODE_OVERLAY      = 0u;
const uint MODE_BASE         = 1u;
const uint MODE_ADJUST_BLEND = 2u;
const uint MODE_FULL_REPLACE = 3u;

// Competition threshold - when weight exceeds this, start eating opponent's weight
const uint COMPETITION_THRESHOLD = 128u;
// How fast to eat opponent's weight (1.0 = match contribution, 2.0 = double)
const float COMPETITION_RATE = 1.5;

float calculate_falloff(float distance, float radius) {
    float t = clamp(distance / radius, 0.0, 1.0);
    
    switch (pc.falloff_type) {
        case 0: return 1.0 - t;
        case 1: return 1.0 - smoothstep(0.0, 1.0, t);
        case 2: return 1.0;
        case 3: return 1.0 - (t * t);
        default: return 1.0 - t;
    }
}

void main() {
    ivec2 local_pos = ivec2(gl_GlobalInvocationID.xy);
    ivec2 pixel_pos = ivec2(pc.bounds_min_x, pc.bounds_min_y) + local_pos;
    
    if (pixel_pos.x < 0 || pixel_pos.x >= pc.region_size ||
        pixel_pos.y < 0 || pixel_pos.y >= pc.region_size) {
        return;
    }
    
    float world_x = float(pc.region_world_x + pixel_pos.x);
    float world_z = float(pc.region_world_z + pixel_pos.y);
    
    float dx = world_x - pc.brush_center_x;
    float dz = world_z - pc.brush_center_z;
    
    float distance;
    if (pc.is_circle == 1) {
        distance = sqrt(dx * dx + dz * dz);
    } else {
        distance = max(abs(dx), abs(dz));
    }
    
    if (distance > pc.brush_radius) {
        return;
    }
    
    float falloff = calculate_falloff(distance, pc.brush_radius);
    float effective_strength = falloff * pc.strength;
    
    if (effective_strength < 0.001) {
        return;
    }
    
    // Read existing intent
    uint existing = floatBitsToUint(imageLoad(u_texture_edit, pixel_pos).r);
    
    // Decode existing values
    uint existing_overlay_id     = (existing >> OVERLAY_ID_SHIFT) & OVERLAY_ID_MASK;
    bool existing_overlay_active = ((existing >> OVERLAY_ACTIVE_SHIFT) & 1u) != 0u;
    uint existing_overlay_weight = (existing >> OVERLAY_WEIGHT_SHIFT) & OVERLAY_WEIGHT_MASK;
    uint existing_base_id        = (existing >> BASE_ID_SHIFT) & BASE_ID_MASK;
    bool existing_base_active    = ((existing >> BASE_ACTIVE_SHIFT) & 1u) != 0u;
    uint existing_base_weight    = (existing >> BASE_WEIGHT_SHIFT) & BASE_WEIGHT_MASK;
    
    // Start with existing values
    uint new_overlay_id     = existing_overlay_id;
    bool new_overlay_active = existing_overlay_active;
    uint new_overlay_weight = existing_overlay_weight;
    uint new_base_id        = existing_base_id;
    bool new_base_active    = existing_base_active;
    uint new_base_weight    = existing_base_weight;
    
    // Calculate weight contribution
    uint weight_contribution = uint(float(pc.blend_step) * effective_strength);
    weight_contribution = max(weight_contribution, 1u);
    
    uint mode = uint(pc.texture_mode);
    
    // ==========================================================================
    // MODE: OVERLAY
    // ==========================================================================
    if (mode == MODE_OVERLAY) {
        uint target_overlay = uint(pc.primary_texture_id);
        
        new_overlay_active = true;
        
        // Check if continuing same overlay or starting new
        if (existing_overlay_active && existing_overlay_id == target_overlay) {
            new_overlay_weight = min(existing_overlay_weight + weight_contribution, 255u);
        } else {
            new_overlay_id = target_overlay;
            new_overlay_weight = min(weight_contribution, 255u);
        }
        
        // COMPETITION: If overlay is strong, eat base's weight
        if (new_overlay_weight > COMPETITION_THRESHOLD && existing_base_weight > 0u) {
            uint excess = new_overlay_weight - COMPETITION_THRESHOLD;
            uint eat_amount = uint(float(excess) * COMPETITION_RATE);
            new_base_weight = uint(max(0, int(existing_base_weight) - int(eat_amount)));
        }
    }
    
    // ==========================================================================
    // MODE: BASE
    // ==========================================================================
    else if (mode == MODE_BASE) {
        uint target_base = uint(pc.primary_texture_id);
        
        new_base_active = true;
        
        // Check if continuing same base or starting new
        if (existing_base_active && existing_base_id == target_base) {
            new_base_weight = min(existing_base_weight + weight_contribution, 255u);
        } else {
            new_base_id = target_base;
            new_base_weight = min(weight_contribution, 255u);
        }
        
        // COMPETITION: If base is strong, eat overlay's weight
        if (new_base_weight > COMPETITION_THRESHOLD && existing_overlay_weight > 0u) {
            uint excess = new_base_weight - COMPETITION_THRESHOLD;
            uint eat_amount = uint(float(excess) * COMPETITION_RATE);
            new_overlay_weight = uint(max(0, int(existing_overlay_weight) - int(eat_amount)));
        }
    }
    
    // ==========================================================================
    // MODE: ADJUST_BLEND
    // ==========================================================================
    else if (mode == MODE_ADJUST_BLEND) {
        new_overlay_active = false;
        new_base_active = false;
        new_overlay_weight = uint(pc.target_blend);
        new_base_weight = 255u;  // Marker
    }
    
    // ==========================================================================
    // MODE: FULL_REPLACE
    // ==========================================================================
    else if (mode == MODE_FULL_REPLACE) {
        uint target_overlay = uint(pc.primary_texture_id);
        uint target_base = uint(pc.secondary_texture_id);
        
        new_overlay_active = true;
        new_base_active = true;
        new_overlay_id = target_overlay;
        new_base_id = target_base;
        
        uint combined_weight = max(existing_overlay_weight, existing_base_weight);
        combined_weight = min(combined_weight + weight_contribution, 255u);
        new_overlay_weight = combined_weight;
        new_base_weight = combined_weight;
    }
    
    // Encode new intent
    uint new_packed = 0u;
    new_packed |= (new_overlay_id & OVERLAY_ID_MASK) << OVERLAY_ID_SHIFT;
    if (new_overlay_active) new_packed |= 1u << OVERLAY_ACTIVE_SHIFT;
    new_packed |= (new_overlay_weight & OVERLAY_WEIGHT_MASK) << OVERLAY_WEIGHT_SHIFT;
    new_packed |= (new_base_id & BASE_ID_MASK) << BASE_ID_SHIFT;
    if (new_base_active) new_packed |= 1u << BASE_ACTIVE_SHIFT;
    new_packed |= (new_base_weight & BASE_WEIGHT_MASK) << BASE_WEIGHT_SHIFT;
    
    imageStore(u_texture_edit, pixel_pos, vec4(uintBitsToFloat(new_packed), 0.0, 0.0, 1.0));
}