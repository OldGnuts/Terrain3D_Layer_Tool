// /Shaders/Path/PathApplyHeight.glsl
#[compute]
#version 450

/*
 * PathApplyHeight.glsl - Applies path height modifications to region heightmaps
 * 
 * This shader:
 * 1. Samples the path mask and zone data
 * 2. Computes target height from path segments
 * 3. Applies zone-specific blend modes
 * 4. Handles terrain conformance
 * 5. Applies noise variations
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Region heightmap (read/write)
layout(set = 0, binding = 0, r32f) uniform restrict image2D heightmap;

// Path data textures
layout(set = 0, binding = 1) uniform sampler2D mask_texture;
layout(set = 0, binding = 2) uniform sampler2D sdf_texture;
layout(set = 0, binding = 3) uniform sampler2D zone_texture;

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
    
    // Height noise (10 floats)
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
    
    // Texture noise (10 floats - not used here but keeps struct aligned)
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

layout(set = 0, binding = 4, std430) readonly buffer ProfileBuffer {
    float zone_count;
    float global_smoothing;
    float symmetrical;
    float half_width;
    float zone_boundaries[8];
    ZoneData zones[8];
};

// Path segments for height interpolation
struct PathSegment {
    vec2 start_pos;
    float start_height;
    float start_distance;
    vec2 end_pos;
    float end_height;
    float end_distance;
    vec2 tangent;
    vec2 perpendicular;
};

layout(set = 0, binding = 5, std430) readonly buffer SegmentBuffer {
    PathSegment segments[];
};

// Push constants
layout(push_constant) uniform PushConstants {
    ivec2 region_min;
    ivec2 region_max;
    ivec2 mask_min;
    ivec2 mask_max;
    int mask_width;
    int mask_height;
    int segment_count;
    int pc_zone_count;
    float world_height_scale;
    float _pad1;
    float _pad2;
    float _pad3;
} pc;

// ============================================================================
// HEIGHT BLEND MODES
// ============================================================================

// HeightBlendMode enum values
const int BLEND_REPLACE = 0;
const int BLEND_ADD = 1;
const int BLEND_SUBTRACT = 2;
const int BLEND_MIN = 3;
const int BLEND_MAX = 4;
const int BLEND_LERP = 5;

float apply_blend_mode(int mode, float current, float target, float strength, float conformance) {
    float result;
    
    switch (mode) {
        case BLEND_REPLACE:
            // Direct replacement, blended by strength
            result = mix(current, target, strength);
            break;
            
        case BLEND_ADD:
            // Add target as offset
            result = current + target * strength;
            break;
            
        case BLEND_SUBTRACT:
            // Subtract target as offset
            result = current - target * strength;
            break;
            
        case BLEND_MIN:
            // Only lower terrain (carve)
            result = mix(current, min(current, target), strength);
            break;
            
        case BLEND_MAX:
            // Only raise terrain
            result = mix(current, max(current, target), strength);
            break;
            
        case BLEND_LERP:
        default:
            // Standard blend
            result = mix(current, target, strength);
            break;
    }
    
    // Apply terrain conformance (blend back toward original)
    return mix(result, current, conformance);
}

// ============================================================================
// NOISE (simplified version for height application)
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
// PATH HEIGHT INTERPOLATION
// ============================================================================
/*
// Removed get_path_height_at_position loop function in order to use cached data
float get_path_height_at_position(vec2 pos) {
    float min_dist = 1e10;
    float closest_height = 0.0;
    
    for (int i = 0; i < pc.segment_count; i++) {
        PathSegment seg = segments[i];
        
        vec2 ab = seg.end_pos - seg.start_pos;
        float ab_len_sq = dot(ab, ab);
        
        float t;
        if (ab_len_sq < 0.0001) {
            t = 0.0;
        } else {
            t = clamp(dot(pos - seg.start_pos, ab) / ab_len_sq, 0.0, 1.0);
        }
        
        vec2 closest = seg.start_pos + ab * t;
        float dist = distance(pos, closest);
        
        if (dist < min_dist) {
            min_dist = dist;
            closest_height = mix(seg.start_height, seg.end_height, t);
        }
    }
    
    return closest_height;
}
*/
// helper to get height from cached segment data
float get_cached_path_height(float norm_segment_index, float segment_t) {
    // Reconstruct exact segment index from normalized value (0.0 - 1.0)
    // Formula from PathSDF: float seg_normalized = float(closest_segment) / max(1.0, float(pc.segment_count - 1));
    
    int max_index = pc.segment_count - 1;
    int segment_idx = int(round(norm_segment_index * float(max_index)));
    segment_idx = clamp(segment_idx, 0, max_index);

    // Fetch the specific segment directly
    PathSegment seg = segments[segment_idx];

    // Interpolate height
    return mix(seg.start_height, seg.end_height, segment_t);
}

// ============================================================================
// MAIN
// ============================================================================

void main() {
    ivec2 region_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 region_size = imageSize(heightmap);
    
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
    float sdf = texture(sdf_texture, uv).r;
    
    // Sample full vec4 to get segment cache
    // R = Zone Index, G = Zone Param, B = Norm Segment Index, A = Segment T
    vec4 zone_data = texture(zone_texture, uv);
    
    int zone_index = int(round(zone_data.r));
    // float zone_param = zone_data.g; // Unused in height, useful for texturing
    
    // No influence - skip
    if (abs(influence) < 0.001) {
        return;
    }
    
    // Invalid zone - skip
    if (zone_index < 0 || zone_index >= pc.pc_zone_count) {
        return;
    }
    
    ZoneData zone = zones[zone_index];
    
    if (zone.enabled < 0.5) {
        return;
    }
    
    // ========================================================================
    // Get current and target heights
    // ========================================================================
    
    float current_height = imageLoad(heightmap, region_coord).r;
    
    // OPTIMIZED: Use cached segment data from channels B and A
    float path_height = get_cached_path_height(zone_data.b, zone_data.a);
    // Used computed height, now uses cached height
    // float path_height = get_path_height_at_position(mask_coord);

    // Add zone's height offset (already normalized by world height scale in C#)
    float target_height = path_height + zone.height_offset / pc.world_height_scale;
   
    // ========================================================================
    // Apply noise
    // ========================================================================
    
    if (zone.height_noise_enabled > 0.5) {
        vec2 noise_pos;
        if (zone.height_noise_use_world > 0.5) {
            noise_pos = vec2(region_coord) * zone.height_noise_frequency;
        } else {
            noise_pos = uv * zone.height_noise_frequency * 100.0;
        }
        noise_pos += vec2(zone.height_noise_offset_x, zone.height_noise_offset_y);
        
        float noise = fbm(
            noise_pos,
            zone.height_noise_seed,
            int(zone.height_noise_octaves),
            zone.height_noise_persistence,
            zone.height_noise_lacunarity
        );
        
        // Map to [-0.5, 0.5] and scale by amplitude
        float noise_offset = (noise - 0.5) * zone.height_noise_amplitude / pc.world_height_scale;
        target_height += noise_offset;
    }
    
    // ========================================================================
    // Apply blend mode
    // ========================================================================
    
    int blend_mode = int(zone.height_blend_mode);
    float strength = abs(influence) * zone.height_strength;
    
    // Special handling for embankments (negative influence)
    if (influence < 0.0) {
        // Embankment mode - add height offset
        blend_mode = BLEND_ADD;
        target_height = zone.height_offset / pc.world_height_scale;
    }
    
    float new_height = apply_blend_mode(
        blend_mode,
        current_height,
        target_height,
        strength,
        zone.terrain_conformance
    );
    
    // ========================================================================
    // Output
    // ========================================================================
    
    imageStore(heightmap, region_coord, vec4(new_height, 0.0, 0.0, 0.0));
}