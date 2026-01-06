#[compute]
#version 450

/*
 * TextureLayerPass1.glsl
 * 
 * Zone-based texture blending system.
 * 
 * Key principles:
 * - Texture pairs are FIXED per zone (no flip-flopping)
 * - Zone selection is DETERMINISTIC (based on raw mask * blend_strength, NOT falloff)
 * - Falloff only affects blend intensity, not zone selection
 * - Noise affects BLEND VALUES for variation
 * - Zones transition: Original → Tertiary → Secondary → Primary
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// =============================================================================
// BINDINGS
// =============================================================================

layout(set = 0, binding = 0, r32f) uniform image2D control_map;
layout(set = 0, binding = 1, r32ui) uniform uimage2D metadata_map;
layout(set = 0, binding = 2) uniform sampler2D layer_mask_sampler;

layout(set = 0, binding = 3, std430) restrict readonly buffer ExclusionBuffer {
    uint excluded_ids[];
} exclusions;

layout(set = 0, binding = 4) uniform sampler2D noise_texture;

layout(set = 0, binding = 5, std140) uniform LayerSettings {
    // Block 1: Blend settings (16 bytes)
    uint  blend_mode;
    float blend_strength;
    uint  gradient_enabled;
    uint  output_metadata;
    
    // Block 2: Texture IDs (16 bytes)
    uint  secondary_texture_id;
    uint  tertiary_texture_id;
    uint  original_base_id;
    uint  _pad0;
    
    // Block 3: Zone thresholds (16 bytes)
    float tertiary_threshold;
    float secondary_threshold;
    float primary_threshold;
    uint  _pad1;
    
    // Block 4: Transition widths (16 bytes)
    float tertiary_transition;
    float secondary_transition;
    float primary_transition;
    uint  _pad2;
    
    // Block 5: Reserved (16 bytes)
    float _reserved0;
    float _reserved1;
    float _reserved2;
    uint  _pad3;
    
    // Block 6: Noise settings (16 bytes)
    uint  enable_noise;
    float noise_amount;
    float noise_scale;
    uint  noise_seed;
    
    // Block 7: Noise settings continued (16 bytes)
    uint  noise_type;
    uint  has_noise_texture;
    uint  edge_aware_noise;
    float edge_noise_falloff;
} settings;

layout(set = 0, binding = 6, std430) restrict readonly buffer FalloffCurveBuffer {
    int point_count;
    float values[];
} falloff_curve;

// =============================================================================
// PUSH CONSTANTS
// =============================================================================

layout(push_constant, std430) uniform Params {
    ivec2 region_min_px;
    ivec2 mask_min_px;
    ivec2 layer_size_px;
    uint  texture_id;
    uint  exclusion_count;
    float layer_world_min_x;
    float layer_world_min_y;
    float layer_world_size_x;
    float layer_world_size_y;
    uint  falloff_type;
    float falloff_strength;
    uint  region_size;
    uint  _padding;
} pc;

// =============================================================================
// CONSTANTS
// =============================================================================

const uint BLEND_MODE_REPLACE    = 0u;
const uint BLEND_MODE_STRENGTHEN = 1u;
const uint BLEND_MODE_MAX        = 2u;
const uint BLEND_MODE_ADDITIVE   = 3u;

const uint NOISE_VALUE   = 0u;
const uint NOISE_PERLIN  = 1u;
const uint NOISE_SIMPLEX = 2u;

const uint FALLOFF_NONE     = 0u;
const uint FALLOFF_LINEAR   = 1u;
const uint FALLOFF_CIRCULAR = 2u;

const uint INVALID_TEXTURE_ID = 0xFFFFFFFFu;

// Control map bit layout
const int  BASE_ID_SHIFT    = 27;
const uint BASE_ID_MASK     = 0x1Fu;
const int  OVERLAY_ID_SHIFT = 22;
const uint OVERLAY_ID_MASK  = 0x1Fu;
const int  BLEND_SHIFT      = 14;
const uint BLEND_MASK       = 0xFFu;

// Metadata bit layout
const int  ZONE_SHIFT          = 0;
const int  ZONE_T_SHIFT        = 4;
const int  BLEND_VALUE_SHIFT   = 12;
const int  FLAGS_SHIFT         = 24;

// Metadata flags
const uint FLAG_GRADIENT_MODE  = 0x01u;
const uint FLAG_ZONE_BOUNDARY  = 0x02u;
const uint FLAG_NOISE_APPLIED  = 0x04u;
const uint FLAG_FALLOFF_EDGE   = 0x08u;

// =============================================================================
// CONTROL MAP ENCODING/DECODING
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

uint encode_metadata(uint zone, float zone_t, uint blend_value, uint flags) {
    uint metadata = 0u;
    metadata |= (zone & 0xFu) << ZONE_SHIFT;
    metadata |= (uint(clamp(zone_t * 255.0, 0.0, 255.0)) & 0xFFu) << ZONE_T_SHIFT;
    metadata |= (blend_value & 0xFFu) << BLEND_VALUE_SHIFT;
    metadata |= (flags & 0xFFu) << FLAGS_SHIFT;
    return metadata;
}

// =============================================================================
// EXCLUSION CHECK
// =============================================================================

bool is_excluded(uint id) {
    for (uint i = 0u; i < pc.exclusion_count; i++) {
        if (exclusions.excluded_ids[i] == id) {
            return true;
        }
    }
    return false;
}

// =============================================================================
// FALLOFF FUNCTIONS
// =============================================================================

float calculate_falloff_distance(vec2 uv, uint falloff_type_param) {
    vec2 rel = (uv - 0.5) * 2.0;
    
    if (falloff_type_param == FALLOFF_LINEAR) {
        return max(abs(rel.x), abs(rel.y));
    } else if (falloff_type_param == FALLOFF_CIRCULAR) {
        return length(rel);
    }
    
    return 0.0;
}

float sample_falloff_curve(float t) {
    if (falloff_curve.point_count <= 0) {
        return 1.0;
    }
    if (falloff_curve.point_count == 1) {
        return clamp(falloff_curve.values[0], 0.0, 1.0);
    }
    
    t = clamp(t, 0.0, 1.0);
    float f_idx = t * float(falloff_curve.point_count - 1);
    int idx0 = int(f_idx);
    int idx1 = min(idx0 + 1, falloff_curve.point_count - 1);
    float frac = fract(f_idx);
    
    return mix(falloff_curve.values[idx0], falloff_curve.values[idx1], frac);
}

float calculate_layer_falloff(vec2 mask_uv) {
    if (pc.falloff_type == FALLOFF_NONE || pc.falloff_strength <= 0.0) {
        return 1.0;
    }
    
    float dist = calculate_falloff_distance(mask_uv, pc.falloff_type);
    float base_falloff = clamp(1.0 - dist, 0.0, 1.0);
    float curved_falloff = sample_falloff_curve(base_falloff);
    
    return mix(1.0, curved_falloff, pc.falloff_strength);
}

// =============================================================================
// NOISE FUNCTIONS
// =============================================================================

const float TWO_PI = 6.28318530718;

vec2 get_seed_offset() {
    return vec2(
        0.12345 * float(settings.noise_seed), 
        1.54321 * float(settings.noise_seed)
    );
}

float hash12(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

vec2 grad2(vec2 p) {
    float a = TWO_PI * hash12(p);
    return vec2(cos(a), sin(a));
}

float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    
    vec2 so = get_seed_offset();
    float a = hash12(i + vec2(0.0, 0.0) + so);
    float b = hash12(i + vec2(1.0, 0.0) + so);
    float c = hash12(i + vec2(0.0, 1.0) + so);
    float d = hash12(i + vec2(1.0, 1.0) + so);
    
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

float perlin_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    
    vec2 so = get_seed_offset();
    vec2 g00 = grad2(i + vec2(0, 0) + so);
    vec2 g10 = grad2(i + vec2(1, 0) + so);
    vec2 g01 = grad2(i + vec2(0, 1) + so);
    vec2 g11 = grad2(i + vec2(1, 1) + so);
    
    float n00 = dot(g00, f - vec2(0, 0));
    float n10 = dot(g10, f - vec2(1, 0));
    float n01 = dot(g01, f - vec2(0, 1));
    float n11 = dot(g11, f - vec2(1, 1));
    
    float nx0 = mix(n00, n10, u.x);
    float nx1 = mix(n01, n11, u.x);
    
    return mix(nx0, nx1, u.y) * 0.5 + 0.5;
}

vec3 mod289_v3(vec3 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
vec2 mod289_v2(vec2 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
vec3 permute(vec3 x) { return mod289_v3((x * 34.0 + 1.0) * x); }

float simplex_noise(vec2 v) {
    v += get_seed_offset();
    
    const vec4 C = vec4(
        0.211324865405187,
        0.366025403784439,
        -0.577350269189626,
        0.024390243902439
    );
    
    vec2 i = floor(v + dot(v, C.yy));
    vec2 x0 = v - i + dot(i, C.xx);
    
    vec2 i1 = (x0.x > x0.y) ? vec2(1.0, 0.0) : vec2(0.0, 1.0);
    vec4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;
    
    i = mod289_v2(i);
    vec3 p = permute(permute(i.y + vec3(0.0, i1.y, 1.0)) + i.x + vec3(0.0, i1.x, 1.0));
    
    vec3 m = max(0.5 - vec3(dot(x0, x0), dot(x12.xy, x12.xy), dot(x12.zw, x12.zw)), 0.0);
    m = m * m;
    m = m * m;
    
    vec3 x = 2.0 * fract(p * C.www) - 1.0;
    vec3 h = abs(x) - 0.5;
    vec3 ox = floor(x + 0.5);
    vec3 a0 = x - ox;
    
    m *= 1.79284291400159 - 0.85373472095314 * (a0 * a0 + h * h);
    
    vec3 g;
    g.x = a0.x * x0.x + h.x * x0.y;
    g.yz = a0.yz * x12.xz + h.yz * x12.yw;
    
    return 130.0 * dot(m, g) * 0.5 + 0.5;
}

float sample_noise(vec2 world_pos) {
    vec2 noise_pos = world_pos * settings.noise_scale;
    
    if (settings.has_noise_texture != 0u) {
        return texture(noise_texture, noise_pos).r;
    }
    
    if (settings.noise_type == NOISE_VALUE) {
        return value_noise(noise_pos);
    } else if (settings.noise_type == NOISE_PERLIN) {
        return perlin_noise(noise_pos);
    } else {
        return simplex_noise(noise_pos);
    }
}

// =============================================================================
// ZONE-BASED TEXTURE SELECTION
// =============================================================================

struct ZoneResult {
    uint base_tex;
    uint overlay_tex;
    float zone_t;
    uint zone_index;
    uint flags;
};

// Get the previous texture in the gradient order for a given texture
uint get_previous_texture(uint tex_id, uint original_base, uint primary_tex, uint secondary_tex, uint tertiary_tex) {
    bool has_secondary = (secondary_tex != INVALID_TEXTURE_ID);
    bool has_tertiary = (tertiary_tex != INVALID_TEXTURE_ID);
    
    if (tex_id == primary_tex) {
        if (has_secondary) return secondary_tex;
        if (has_tertiary) return tertiary_tex;
        return original_base;
    }
    if (tex_id == secondary_tex) {
        if (has_tertiary) return tertiary_tex;
        return original_base;
    }
    if (tex_id == tertiary_tex) {
        return original_base;
    }
    return original_base;
}

ZoneResult determine_zone_and_blend(
    float mask_value,
    uint original_base,
    uint primary_tex,
    uint secondary_tex,
    uint tertiary_tex
) {
    ZoneResult result;
    result.flags = FLAG_GRADIENT_MODE;
    
    bool has_secondary = (secondary_tex != INVALID_TEXTURE_ID);
    bool has_tertiary = (tertiary_tex != INVALID_TEXTURE_ID);
    
    float t_tert = settings.tertiary_threshold;
    float t_sec = settings.secondary_threshold;
    float t_prim = settings.primary_threshold;
    
    if (has_tertiary && has_secondary) {
        // Full 4-texture gradient: Original → Tertiary → Secondary → Primary
        if (mask_value < t_tert) {
            // Zone 0: Original → Tertiary
            result.zone_index = 0u;
            result.base_tex = original_base;
            result.overlay_tex = tertiary_tex;
            result.zone_t = mask_value / max(t_tert, 0.001);
        }
        else if (mask_value < t_sec) {
            // Zone 1: Tertiary → Secondary
            result.zone_index = 1u;
            result.base_tex = tertiary_tex;
            result.overlay_tex = secondary_tex;
            result.zone_t = (mask_value - t_tert) / max(t_sec - t_tert, 0.001);
        }
        else if (mask_value < t_prim) {
            // Zone 2: Secondary → Primary
            result.zone_index = 2u;
            result.base_tex = secondary_tex;
            result.overlay_tex = primary_tex;
            result.zone_t = (mask_value - t_sec) / max(t_prim - t_sec, 0.001);
        }
        else {
            // Zone 3: Primary dominates - but keep blending with secondary
            result.zone_index = 3u;
            result.base_tex = secondary_tex;  // FIXED: Keep secondary as base
            result.overlay_tex = primary_tex;
            // zone_t approaches 1.0 as mask goes beyond primary_threshold
            float beyond = (mask_value - t_prim) / max(1.0 - t_prim, 0.001);
            result.zone_t = 0.9 + beyond * 0.1; // 0.9 to 1.0 range
        }
    }
    else if (has_tertiary && !has_secondary) {
        // 3-texture gradient: Original → Tertiary → Primary
        if (mask_value < t_tert) {
            result.zone_index = 0u;
            result.base_tex = original_base;
            result.overlay_tex = tertiary_tex;
            result.zone_t = mask_value / max(t_tert, 0.001);
        }
        else if (mask_value < t_prim) {
            result.zone_index = 1u;
            result.base_tex = tertiary_tex;
            result.overlay_tex = primary_tex;
            result.zone_t = (mask_value - t_tert) / max(t_prim - t_tert, 0.001);
        }
        else {
            // Primary dominates - keep blending with tertiary
            result.zone_index = 2u;
            result.base_tex = tertiary_tex;
            result.overlay_tex = primary_tex;
            float beyond = (mask_value - t_prim) / max(1.0 - t_prim, 0.001);
            result.zone_t = 0.9 + beyond * 0.1;
        }
    }
    else if (!has_tertiary && has_secondary) {
        // 3-texture gradient: Original → Secondary → Primary
        if (mask_value < t_sec) {
            result.zone_index = 0u;
            result.base_tex = original_base;
            result.overlay_tex = secondary_tex;
            result.zone_t = mask_value / max(t_sec, 0.001);
        }
        else if (mask_value < t_prim) {
            result.zone_index = 1u;
            result.base_tex = secondary_tex;
            result.overlay_tex = primary_tex;
            result.zone_t = (mask_value - t_sec) / max(t_prim - t_sec, 0.001);
        }
        else {
            result.zone_index = 2u;
            result.base_tex = secondary_tex;
            result.overlay_tex = primary_tex;
            float beyond = (mask_value - t_prim) / max(1.0 - t_prim, 0.001);
            result.zone_t = 0.9 + beyond * 0.1;
        }
    }
    else {
        // 2-texture gradient: Original → Primary
        if (mask_value < t_prim) {
            result.zone_index = 0u;
            result.base_tex = original_base;
            result.overlay_tex = primary_tex;
            result.zone_t = mask_value / max(t_prim, 0.001);
        }
        else {
            result.zone_index = 1u;
            result.base_tex = original_base;
            result.overlay_tex = primary_tex;
            float beyond = (mask_value - t_prim) / max(1.0 - t_prim, 0.001);
            result.zone_t = 0.9 + beyond * 0.1;
        }
    }
    
    // Clamp zone_t
    result.zone_t = clamp(result.zone_t, 0.0, 1.0);
    
    // Flag if near zone boundary (wider range for better smoothing detection)
    if (result.zone_t < 0.25 || result.zone_t > 0.75) {
        result.flags |= FLAG_ZONE_BOUNDARY;
    }
    
    return result;
}

// Apply transition curve with better smoothstep
float apply_transition_curve(float zone_t, float transition_width) {
    // transition_width: 0.1 = sharp, 1.0 = very gradual
    // Map zone_t through a smoothstep with width-dependent bounds
    
    float half_width = clamp(transition_width * 0.5, 0.05, 0.5);
    float low = 0.5 - half_width;
    float high = 0.5 + half_width;
    
    // Remap zone_t (0-1) to smoothstep range
    float remapped = low + zone_t * (high - low) * 2.0;
    remapped = clamp(remapped, 0.0, 1.0);
    
    return smoothstep(0.0, 1.0, remapped);
}

float get_zone_transition_width(uint zone_index) {
    switch (zone_index) {
        case 0u: return settings.tertiary_transition;
        case 1u: return settings.secondary_transition;
        case 2u: return settings.primary_transition;
        default: return settings.primary_transition;
    }
}

// =============================================================================
// SINGLE TEXTURE MODE
// =============================================================================

struct SingleResult {
    uint base_id;
    uint overlay_id;
    uint blend;
    uint flags;
};

SingleResult apply_single_texture(
    vec2 world_pos,
    float strength,
    float falloff,
    uint original_base,
    uint original_overlay,
    uint original_blend
) {
    SingleResult result;
    result.flags = 0u;
    
    // Combine strength with falloff
    float effective_strength = strength * falloff;
    
    // Apply noise to strength
    if (settings.enable_noise != 0u && settings.noise_amount > 0.0) {
        float edge_factor = 1.0;
        if (settings.edge_aware_noise != 0u) {
            edge_factor = 1.0 - abs(effective_strength - 0.5) * 2.0;
            edge_factor = clamp(edge_factor, 0.0, 1.0);
        }
        
        float noise = sample_noise(world_pos);
        float noise_offset = (noise - 0.5) * settings.noise_amount * edge_factor * 2.0;
        effective_strength = clamp(effective_strength + noise_offset, 0.0, 1.0);
        result.flags |= FLAG_NOISE_APPLIED;
    }
    
    if (effective_strength <= 0.001) {
        result.base_id = original_base;
        result.overlay_id = original_overlay;
        result.blend = original_blend;
        return result;
    }
    
    uint T = pc.texture_id;
    uint new_base = original_base;
    uint new_overlay = original_overlay;
    float new_blend_f = float(original_blend) / 255.0;
    
    if (settings.blend_mode == BLEND_MODE_REPLACE) {
        if (T == original_base) {
            new_blend_f = mix(new_blend_f, 0.0, effective_strength);
        }
        else if (T == original_overlay) {
            new_blend_f = mix(new_blend_f, 1.0, effective_strength);
        }
        else {
            if (effective_strength > 0.5) {
                new_base = T;
                new_overlay = original_base;
                new_blend_f = 1.0 - effective_strength;
            } else {
                new_overlay = T;
                new_blend_f = effective_strength;
            }
        }
    }
    else if (settings.blend_mode == BLEND_MODE_STRENGTHEN) {
        if (T == original_base) {
            new_blend_f = mix(new_blend_f, 0.0, effective_strength);
        }
        else if (T == original_overlay) {
            new_blend_f = mix(new_blend_f, 1.0, effective_strength);
        }
    }
    else if (settings.blend_mode == BLEND_MODE_MAX) {
        if (T == original_base) {
            new_blend_f = min(new_blend_f, 1.0 - effective_strength);
        }
        else if (T == original_overlay) {
            new_blend_f = max(new_blend_f, effective_strength);
        }
        else if (effective_strength > new_blend_f) {
            new_overlay = T;
            new_blend_f = effective_strength;
        }
    }
    else if (settings.blend_mode == BLEND_MODE_ADDITIVE) {
        if (T == original_base) {
            new_blend_f = max(0.0, new_blend_f - effective_strength * 0.5);
        }
        else if (T == original_overlay) {
            new_blend_f = min(1.0, new_blend_f + effective_strength * 0.5);
        }
        else {
            new_overlay = T;
            new_blend_f = min(1.0, new_blend_f + effective_strength * 0.5);
        }
    }
    
    result.base_id = new_base;
    result.overlay_id = new_overlay;
    result.blend = uint(clamp(new_blend_f * 255.0, 0.0, 255.0));
    
    return result;
}

// =============================================================================
// MAIN
// =============================================================================

void main() {
    ivec2 region_px = ivec2(gl_GlobalInvocationID.xy);
    ivec2 region_size = imageSize(control_map);
    
    if (region_px.x >= region_size.x || region_px.y >= region_size.y) {
        return;
    }
    
    // Calculate mask UV coordinates
    ivec2 mask_px = region_px - pc.region_min_px + pc.mask_min_px;
    vec2 mask_uv = (vec2(mask_px) + 0.5) / vec2(pc.layer_size_px);
    
    if (mask_uv.x < 0.0 || mask_uv.x > 1.0 || mask_uv.y < 0.0 || mask_uv.y > 1.0) {
        return;
    }
    
    // Calculate falloff (for blend modulation, NOT zone selection)
    float falloff = calculate_layer_falloff(mask_uv);
    if (falloff <= 0.001) {
        return;
    }
    
    // Calculate world position for noise sampling
    vec2 world_pos = vec2(pc.layer_world_min_x, pc.layer_world_min_y) + 
                     mask_uv * vec2(pc.layer_world_size_x, pc.layer_world_size_y);
    
    // Sample mask
    float raw_mask = texture(layer_mask_sampler, mask_uv).r;
    
    // Read current control map state
    uint packed = floatBitsToUint(imageLoad(control_map, region_px).r);
    uint original_base, original_overlay, original_blend;
    decode_control(packed, original_base, original_overlay, original_blend);
    
    uint new_base, new_overlay, new_blend;
    uint flags = 0u;
    uint zone_index = 0u;
    float zone_t = 0.0;
    
    if (settings.gradient_enabled != 0u) {
        // === ZONE-BASED GRADIENT MODE ===
        
        // IMPORTANT: Zone selection uses raw_mask * blend_strength (NO falloff!)
        // This prevents circular banding from falloff affecting zone boundaries
        float zone_mask = raw_mask * settings.blend_strength;
        
        // Determine zone (DETERMINISTIC)
        ZoneResult zr = determine_zone_and_blend(
            zone_mask,
            original_base,
            pc.texture_id,
            settings.secondary_texture_id,
            settings.tertiary_texture_id
        );
        
        zone_index = zr.zone_index;
        zone_t = zr.zone_t;
        flags = zr.flags;
        
        // Apply transition curve
        float transition_width = get_zone_transition_width(zr.zone_index);
        float curved_t = apply_transition_curve(zr.zone_t, transition_width);
        
        // Apply noise to blend value (INCREASED effect)
        if (settings.enable_noise != 0u && settings.noise_amount > 0.0) {
            float noise = sample_noise(world_pos);
            
            // Stronger base noise effect
            float noise_offset = (noise - 0.5) * settings.noise_amount * 2.5;
            
            // Edge-aware: MORE noise near zone center (transition area)
            if (settings.edge_aware_noise != 0u) {
                // Maximum at curved_t = 0.5, minimum at 0 and 1
                float edge_factor = 1.0 - abs(curved_t - 0.5) * 2.0;
                edge_factor = clamp(edge_factor, 0.0, 1.0);
                // Use edge_noise_falloff to control the curve (higher = more concentrated at edges)
                edge_factor = pow(edge_factor, max(0.3, settings.edge_noise_falloff));
                // Boost edge noise significantly
                noise_offset *= (0.5 + edge_factor * 1.5);
            }
            
            curved_t = clamp(curved_t + noise_offset, 0.0, 1.0);
            flags |= FLAG_NOISE_APPLIED;
        }
        
        // APPLY FALLOFF TO BLEND VALUE (not zone selection)
        // This modulates how much of the overlay shows without changing zones
        // At low falloff, reduce blend to show more base texture
        curved_t *= falloff;
        
        // Flag if near layer falloff edge
        if (falloff < 0.4) {
            flags |= FLAG_FALLOFF_EDGE;
        }
        
        // Set final values
        new_base = zr.base_tex;
        new_overlay = zr.overlay_tex;
        new_blend = uint(clamp(curved_t * 255.0, 0.0, 255.0));
        zone_t = curved_t;
    }
    else {
        // === SINGLE TEXTURE MODE ===
        SingleResult sr = apply_single_texture(
            world_pos,
            raw_mask * settings.blend_strength,
            falloff,
            original_base,
            original_overlay,
            original_blend
        );
        
        new_base = sr.base_id;
        new_overlay = sr.overlay_id;
        new_blend = sr.blend;
        flags = sr.flags;
    }
    
    // === WRITE CONTROL MAP ===
    uint new_packed = encode_control(packed, new_base, new_overlay, new_blend);
    imageStore(control_map, region_px, vec4(uintBitsToFloat(new_packed)));
    
    // === WRITE METADATA ===
    if (settings.output_metadata != 0u) {
        uint metadata = encode_metadata(zone_index, zone_t, new_blend, flags);
        imageStore(metadata_map, region_px, uvec4(metadata, 0u, 0u, 0u));
    }
}