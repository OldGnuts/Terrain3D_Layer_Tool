// /Shaders/ManualEdit/apply_texture_edit.glsl
#[compute]
#version 450

/*
 * apply_texture_edit.glsl
 * 
 * Applies texture overrides from manual edits.
 * Uses selective override based on active flags in the edit data.
 * 
 * Edit data format (packed uint32 in R32F):
 *   Bits 0-4:   Base texture ID (0-31)
 *   Bit 5:      Base edit active flag
 *   Bits 6-10:  Overlay texture ID (0-31)
 *   Bit 11:     Overlay edit active flag
 *   Bits 12-19: Blend value (0-255)
 *   Bit 20:     Blend edit active flag
 *   Bits 21-31: Reserved
 * 
 * Terrain3D control map format (packed uint32 in R32F):
 *   Bits 27-31: Base texture ID
 *   Bits 22-26: Overlay texture ID
 *   Bits 14-21: Blend value (0-255)
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Bindings
layout(set = 0, binding = 0, r32f) uniform image2D control_map;     // In/Out
layout(set = 0, binding = 1, r32f) uniform readonly image2D texture_edit;  // In

// Push constants
layout(push_constant, std430) uniform Params {
    int u_region_size;
    int _pad0;
    int _pad1;
    int _pad2;
} pc;

// Edit data bit layout
const int EDIT_BASE_ID_SHIFT = 0;
const uint EDIT_BASE_ID_MASK = 0x1Fu;
const int EDIT_BASE_ACTIVE_SHIFT = 5;
const int EDIT_OVERLAY_ID_SHIFT = 6;
const uint EDIT_OVERLAY_ID_MASK = 0x1Fu;
const int EDIT_OVERLAY_ACTIVE_SHIFT = 11;
const int EDIT_BLEND_SHIFT = 12;
const uint EDIT_BLEND_MASK = 0xFFu;
const int EDIT_BLEND_ACTIVE_SHIFT = 20;

// Terrain3D control map bit layout
const int CTRL_BASE_ID_SHIFT = 27;
const uint CTRL_BASE_ID_MASK = 0x1Fu;
const int CTRL_OVERLAY_ID_SHIFT = 22;
const uint CTRL_OVERLAY_ID_MASK = 0x1Fu;
const int CTRL_BLEND_SHIFT = 14;
const uint CTRL_BLEND_MASK = 0xFFu;

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
    
    // Read edit data
    uint edit_packed = floatBitsToUint(imageLoad(texture_edit, px).r);
    
    // Skip if no edit flags are set (packed value is 0)
    if (edit_packed == 0u) {
        return;
    }
    
    // Decode edit data
    uint edit_base_id       = (edit_packed >> EDIT_BASE_ID_SHIFT) & EDIT_BASE_ID_MASK;
    bool edit_base_active   = ((edit_packed >> EDIT_BASE_ACTIVE_SHIFT) & 1u) != 0u;
    uint edit_overlay_id    = (edit_packed >> EDIT_OVERLAY_ID_SHIFT) & EDIT_OVERLAY_ID_MASK;
    bool edit_overlay_active = ((edit_packed >> EDIT_OVERLAY_ACTIVE_SHIFT) & 1u) != 0u;
    uint edit_blend         = (edit_packed >> EDIT_BLEND_SHIFT) & EDIT_BLEND_MASK;
    bool edit_blend_active  = ((edit_packed >> EDIT_BLEND_ACTIVE_SHIFT) & 1u) != 0u;
    
    // Skip if no active edits
    if (!edit_base_active && !edit_overlay_active && !edit_blend_active) {
        return;
    }
    
    // Read current control map
    uint ctrl_packed = floatBitsToUint(imageLoad(control_map, px).r);
    uint ctrl_base, ctrl_overlay, ctrl_blend;
    decode_control(ctrl_packed, ctrl_base, ctrl_overlay, ctrl_blend);
    
    // Apply selective overrides
    uint final_base    = edit_base_active    ? edit_base_id    : ctrl_base;
    uint final_overlay = edit_overlay_active ? edit_overlay_id : ctrl_overlay;
    uint final_blend   = edit_blend_active   ? edit_blend      : ctrl_blend;
    
    // Re-encode and write
    uint new_packed = encode_control(ctrl_packed, final_base, final_overlay, final_blend);
    imageStore(control_map, px, vec4(uintBitsToFloat(new_packed), 0.0, 0.0, 0.0));
}