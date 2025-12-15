// /Shaders/Path/PathApplyTexture.glsl
#[compute]
#version 450

/*
 * PathApplyTexture.glsl - Applies path textures to region control maps
 * 
 * Uses Terrain3D's control map format:
 * - Bits 27-31: Base texture ID (5 bits, 0-31)
 * - Bits 22-26: Overlay texture ID (5 bits, 0-31)
 * - Bits 14-21: Blend value (8 bits, 0-255)
 * - Other bits: Reserved for other terrain data
 * 
 * Reference: https://terrain3d.readthedocs.io/en/stable/docs/controlmap_format.html
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Region control map (read/write)
layout(set = 0, binding = 0, r32f) uniform restrict image2D control_map;

// Path data textures
layout(set = 0, binding = 1) uniform sampler2D mask_texture;
layout(set = 0, binding = 2) uniform sampler2D zone_texture;

// Profile data
struct ZoneData {
    float enabled;
    float width;
    float height_offset;
    float height_strength;
    
    float height_blend_mode;
    float terrain_conformance;
    float _pad1;
    float _pad2;
    
    float texture_id;
    float texture_strength;
    float texture_blend_mode;
    float _pad3;
    
    // Noise data (20 floats total, not all used for texture)
    float height_noise_enabled;
    float height_noise_amplitude;
    float height_noise_frequency;
    float height_noise_octaves;
    float height_noise_persistence;
    float height_noise_lacunarity;
    float height_noise_seed;
    float height_noise_offset_x;
    float height_noise_offset_y;
    float height_noise_use_world;
    
    float tex_noise_enabled;
    float tex_noise_amplitude;
    float tex_noise_frequency;
    float tex_noise_octaves;
    float tex_noise_persistence;
    float tex_noise_lacunarity;
    float tex_noise_seed;
    float tex_noise_offset_x;
    float tex_noise_offset_y;
    float tex_noise_use_world;
};

layout(set = 0, binding = 3, std430) readonly buffer ProfileBuffer {
    float zone_count;
    float global_smoothing;
    float symmetrical;
    float half_width;
    float zone_boundaries[8];
    ZoneData zones[8];
};

// Push constants
layout(push_constant) uniform PushConstants {
    ivec2 region_min;
    ivec2 region_max;
    ivec2 mask_min;
    ivec2 mask_max;
    int mask_width;
    int mask_height;
    int pc_zone_count;
    int _padding;
} pc;

// ============================================================================
// TERRAIN3D CONTROL MAP BIT MANIPULATION
// ============================================================================

const int BASE_ID_SHIFT = 27;
const uint BASE_ID_MASK = 0x1Fu;      // 5 bits

const int OVERLAY_ID_SHIFT = 22;
const uint OVERLAY_ID_MASK = 0x1Fu;   // 5 bits

const int BLEND_SHIFT = 14;
const uint BLEND_MASK = 0xFFu;        // 8 bits

// Decode control map value
void decode_control(uint packed, out uint base_id, out uint overlay_id, out uint blend) {
    base_id = (packed >> BASE_ID_SHIFT) & BASE_ID_MASK;
    overlay_id = (packed >> OVERLAY_ID_SHIFT) & OVERLAY_ID_MASK;
    blend = (packed >> BLEND_SHIFT) & BLEND_MASK;
}

// Encode control map value (preserves other bits)
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

// ============================================================================
// TEXTURE BLEND MODES
// ============================================================================

const int TEX_REPLACE = 0;
const int TEX_BLEND = 1;
const int TEX_OVERLAY = 2;
const int TEX_NONE = 3;

// ============================================================================
// NOISE FOR TEXTURE VARIATION
// ============================================================================

float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.13);
    p3 += dot(p3, p3.yzx + 3.333);
    return fract((p3.x + p3.y) * p3.z);
}

float value_noise(vec2 p, float seed) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    
    float a = hash(i + vec2(0.0, 0.0) + seed);
    float b = hash(i + vec2(1.0, 0.0) + seed);
    float c = hash(i + vec2(0.0, 1.0) + seed);
    float d = hash(i + vec2(1.0, 1.0) + seed);
    
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float fbm(vec2 p, float seed, int octaves, float persistence, float lacunarity) {
    float value = 0.0;
    float amplitude = 1.0;
    float max_value = 0.0;
    
    for (int i = 0; i < octaves && i < 8; i++) {
        value += amplitude * value_noise(p, seed + float(i) * 17.0);
        max_value += amplitude;
        amplitude *= persistence;
        p *= lacunarity;
    }
    
    return value / max_value;
}

// ============================================================================
// MAIN
// ============================================================================

void main() {
    ivec2 region_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 region_size = imageSize(control_map);
    
    // Bounds check
    if (region_coord.x >= region_size.x || region_coord.y >= region_size.y) {
        return;
    }
    
    // Check overlap region
    if (region_coord.x < pc.region_min.x || region_coord.x >= pc.region_max.x ||
        region_coord.y < pc.region_min.y || region_coord.y >= pc.region_max.y) {
        return;
    }
    
    // ========================================================================
    // Map region coordinates to mask UV
    // ========================================================================
    
    vec2 region_to_mask = vec2(region_coord - pc.region_min) / vec2(pc.region_max - pc.region_min);
    vec2 mask_coord = mix(vec2(pc.mask_min), vec2(pc.mask_max), region_to_mask);
    vec2 uv = mask_coord / vec2(pc.mask_width, pc.mask_height);
    
    // ========================================================================
    // Sample path data
    // ========================================================================
    
    float influence = texture(mask_texture, uv).r;
    vec4 zone_data = texture(zone_texture, uv);
    
    int zone_index = int(round(zone_data.r));
    float zone_param = zone_data.g;
    
    // No influence or invalid zone - skip
    // Note: For textures, we use absolute influence (embankments still get textured)
    float abs_influence = abs(influence);
    
    if (abs_influence < 0.001) {
        return;
    }
    
    if (zone_index < 0 || zone_index >= pc.pc_zone_count) {
        return;
    }
    
    ZoneData zone = zones[zone_index];
    
    // Zone disabled or no texture assigned
    if (zone.enabled < 0.5 || zone.texture_id < 0.0) {
        return;
    }
    
    int tex_blend_mode = int(zone.texture_blend_mode);
    if (tex_blend_mode == TEX_NONE) {
        return;
    }
    
    // ========================================================================
    // Read current control map value
    // ========================================================================
    
    uint packed = floatBitsToUint(imageLoad(control_map, region_coord).r);
    
    uint base_id, overlay_id, blend;
    decode_control(packed, base_id, overlay_id, blend);
    
    // ========================================================================
    // Apply texture noise (varies texture strength)
    // ========================================================================
    
    float tex_strength = zone.texture_strength * abs_influence;
    
    if (zone.tex_noise_enabled > 0.5) {
        vec2 noise_pos;
        if (zone.tex_noise_use_world > 0.5) {
            noise_pos = vec2(region_coord) * zone.tex_noise_frequency;
        } else {
            noise_pos = uv * zone.tex_noise_frequency * 100.0;
        }
        noise_pos += vec2(zone.tex_noise_offset_x, zone.tex_noise_offset_y);
        
        float noise = fbm(
            noise_pos,
            zone.tex_noise_seed,
            int(zone.tex_noise_octaves),
            zone.tex_noise_persistence,
            zone.tex_noise_lacunarity
        );
        
        // Modulate strength by noise (can reduce or increase)
        float noise_factor = 0.5 + (noise - 0.5) * zone.tex_noise_amplitude;
        tex_strength *= clamp(noise_factor, 0.0, 1.0);
    }
    
    // ========================================================================
    // Compute new texture values
    // ========================================================================
    
    uint target_texture = uint(zone.texture_id) & BASE_ID_MASK;
    
    uint new_base_id = base_id;
    uint new_overlay_id = overlay_id;
    uint new_blend = blend;
    
    // Different strategies based on blend mode
    if (tex_blend_mode == TEX_REPLACE) {
        // Full replacement mode - path texture dominates
        if (tex_strength > 0.5) {
            new_base_id = target_texture;
            new_overlay_id = base_id; // Keep old as overlay
            new_blend = uint((1.0 - tex_strength) * 255.0);
        } else {
            new_overlay_id = target_texture;
            new_blend = uint(tex_strength * 255.0);
        }
    }
    else if (tex_blend_mode == TEX_BLEND) {
        // Blend mode - smooth transition
        
        // Case 1: Target matches current base - strengthen base
        if (target_texture == base_id) {
            float target_blend = 0.0;
            new_blend = uint(mix(float(blend), target_blend, tex_strength));
        }
        // Case 2: Target matches current overlay - strengthen overlay
        else if (target_texture == overlay_id) {
            float target_blend = 255.0;
            new_blend = uint(mix(float(blend), target_blend, tex_strength));
            
            // If blend crosses threshold, swap
            if (new_blend >= 128u && blend < 128u) {
                new_base_id = overlay_id;
                new_overlay_id = base_id;
                new_blend = 255u - new_blend;
            }
        }
        // Case 3: Target is new texture - introduce as overlay
        else {
            // Calculate how much to blend in new texture
            float blend_amount = tex_strength;
            
            if (blend < 128u) {
                // Base is dominant - keep base, set overlay to target
                new_overlay_id = target_texture;
                new_blend = uint(blend_amount * 255.0);
            } else {
                // Overlay is dominant - current overlay becomes base, target becomes overlay
                new_base_id = overlay_id;
                new_overlay_id = target_texture;
                new_blend = uint(blend_amount * 255.0);
            }
        }
    }
    else if (tex_blend_mode == TEX_OVERLAY) {
        // Overlay mode - always add as overlay without changing base
        new_overlay_id = target_texture;
        new_blend = uint(tex_strength * 255.0);
    }
    
    // ========================================================================
    // Encode and store
    // ========================================================================
    
    uint new_packed = encode_control(packed, new_base_id, new_overlay_id, new_blend);
    
    imageStore(control_map, region_coord, vec4(uintBitsToFloat(new_packed), 0.0, 0.0, 0.0));
}