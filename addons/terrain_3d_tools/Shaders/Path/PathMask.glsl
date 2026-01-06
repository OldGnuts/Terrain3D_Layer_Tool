// /Shaders/Path/PathMask.glsl
#[compute]
#version 450

/*
 * PathMask.glsl - Generates the final influence mask from SDF and profile data
 * 
 * This shader reads the SDF and zone data, then:
 * 1. Samples the appropriate zone's height curve
 * 2. Applies zone-specific noise
 * 3. Outputs a combined influence mask
 * 
 * The mask encodes:
 * - Positive values: Path center influence (0-1)
 * - Negative values: Embankment/rim zones that raise terrain
 * - Zero: No influence
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Output
layout(set = 0, binding = 0, r32f) uniform restrict writeonly image2D mask_output;

// Inputs
layout(set = 0, binding = 1) uniform sampler2D sdf_texture;
layout(set = 0, binding = 2) uniform sampler2D zone_texture;

// Profile data with full zone information
struct ZoneData {
    // Basic info (4 floats)
    float enabled;
    float width;
    float height_offset;
    float height_strength;
    
    // Height settings (4 floats)
    float height_blend_mode;
    float terrain_conformance;
    float _pad1;
    float _pad2;
    
    // Texture settings (4 floats)
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
    
    // Texture noise (10 floats)
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
    // Header (4 floats)
    float zone_count;
    float global_smoothing;
    float symmetrical;
    float half_width;
    // Zone boundaries (8 floats)
    float zone_boundaries[8];
    // Zone data (32 floats per zone)
    ZoneData zones[8];
};

// Baked curve data (64 samples per zone)
layout(set = 0, binding = 4, std430) readonly buffer CurveBuffer {
    float curve_data[]; // 64 floats per zone
};

// Push constants
layout(push_constant) uniform PushConstants {
    int layer_width;
    int layer_height;
    int pc_zone_count;
    float profile_half_width;
} pc;

// ============================================================================
// NOISE FUNCTIONS
// ============================================================================

// Hash function for noise
float hash(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * 0.13);
    p3 += dot(p3, p3.yzx + 3.333);
    return fract((p3.x + p3.y) * p3.z);
}

float hash_with_seed(vec2 p, float seed) {
    return hash(p + vec2(seed * 17.31, seed * 23.57));
}

// Value noise
float value_noise(vec2 p, float seed) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    
    // Smooth interpolation
    vec2 u = f * f * (3.0 - 2.0 * f);
    
    float a = hash_with_seed(i + vec2(0.0, 0.0), seed);
    float b = hash_with_seed(i + vec2(1.0, 0.0), seed);
    float c = hash_with_seed(i + vec2(0.0, 1.0), seed);
    float d = hash_with_seed(i + vec2(1.0, 1.0), seed);
    
    return mix(mix(a, b, u.x), mix(c, d, u.x), u.y);
}

// Fractal Brownian Motion
float fbm(vec2 p, float seed, int octaves, float persistence, float lacunarity) {
    float value = 0.0;
    float amplitude = 1.0;
    float frequency = 1.0;
    float max_value = 0.0;
    
    for (int i = 0; i < octaves && i < 8; i++) {
        value += amplitude * value_noise(p * frequency, seed + float(i));
        max_value += amplitude;
        amplitude *= persistence;
        frequency *= lacunarity;
    }
    
    return value / max_value;
}

// Sample noise for a zone
float sample_zone_noise(int zone_idx, vec2 world_pos, vec2 local_uv, bool for_height) {
    if (zone_idx < 0 || zone_idx >= 8) return 0.0;
    
    ZoneData zone = zones[zone_idx];
    
    float enabled, amplitude, frequency, seed;
    float offset_x, offset_y, use_world;
    int octaves;
    float persistence, lacunarity;
    
    if (for_height) {
        enabled = zone.height_noise_enabled;
        amplitude = zone.height_noise_amplitude;
        frequency = zone.height_noise_frequency;
        octaves = int(zone.height_noise_octaves);
        persistence = zone.height_noise_persistence;
        lacunarity = zone.height_noise_lacunarity;
        seed = zone.height_noise_seed;
        offset_x = zone.height_noise_offset_x;
        offset_y = zone.height_noise_offset_y;
        use_world = zone.height_noise_use_world;
    } else {
        enabled = zone.tex_noise_enabled;
        amplitude = zone.tex_noise_amplitude;
        frequency = zone.tex_noise_frequency;
        octaves = int(zone.tex_noise_octaves);
        persistence = zone.tex_noise_persistence;
        lacunarity = zone.tex_noise_lacunarity;
        seed = zone.tex_noise_seed;
        offset_x = zone.tex_noise_offset_x;
        offset_y = zone.tex_noise_offset_y;
        use_world = zone.tex_noise_use_world;
    }
    
    if (enabled < 0.5) return 0.0;
    
    vec2 sample_pos;
    if (use_world > 0.5) {
        sample_pos = world_pos * frequency + vec2(offset_x, offset_y);
    } else {
        sample_pos = local_uv * frequency + vec2(offset_x, offset_y);
    }
    
    float noise = fbm(sample_pos, seed, octaves, persistence, lacunarity);
    
    // Map from [0,1] to [-0.5, 0.5] and scale by amplitude
    return (noise - 0.5) * amplitude;
}

// ============================================================================
// CURVE SAMPLING
// ============================================================================

const int CURVE_RESOLUTION = 64;

float sample_zone_curve(int zone_idx, float t) {
    if (zone_idx < 0 || zone_idx >= 8) return 1.0;
    
    t = clamp(t, 0.0, 1.0);
    
    int base_idx = zone_idx * CURVE_RESOLUTION;
    float index_f = t * float(CURVE_RESOLUTION - 1);
    int index = int(floor(index_f));
    float frac = fract(index_f);
    
    int idx0 = base_idx + min(index, CURVE_RESOLUTION - 1);
    int idx1 = base_idx + min(index + 1, CURVE_RESOLUTION - 1);
    
    return mix(curve_data[idx0], curve_data[idx1], frac);
}

// ============================================================================
// MAIN
// ============================================================================

void main() {
    ivec2 pixel_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = ivec2(pc.layer_width, pc.layer_height);
    
    if (pixel_coord.x >= size.x || pixel_coord.y >= size.y) {
        return;
    }
    
    vec2 uv = (vec2(pixel_coord) + 0.5) / vec2(size);
    
    // Sample SDF and zone data
    float sdf = texture(sdf_texture, uv).r;
    vec4 zone_data = texture(zone_texture, uv);
    
    int zone_index = int(round(zone_data.r));
    float zone_param = zone_data.g;
    float seg_t = zone_data.a; // Parameter along closest segment
    
    // Outside all zones - no influence
    if (zone_index < 0 || sdf > pc.profile_half_width * 1.5) {
        imageStore(mask_output, pixel_coord, vec4(0.0));
        return;
    }
    
    // Get zone data
    ZoneData zone = zones[zone_index];
    
    if (zone.enabled < 0.5) {
        imageStore(mask_output, pixel_coord, vec4(0.0));
        return;
    }
    
    // ========================================================================
    // Compute base influence from curve
    // ========================================================================
    
    float curve_value = sample_zone_curve(zone_index, zone_param);
    float influence = curve_value * zone.height_strength;
    
    // ========================================================================
    // Apply height noise
    // ========================================================================
    
    vec2 world_pos = vec2(pixel_coord); // Simplified - would need actual world coords
    vec2 local_uv = uv;
    
    float noise = sample_zone_noise(zone_index, world_pos, local_uv, true);
    
    // Apply noise to influence
    influence += noise;
    
    // ========================================================================
    // Encode zone type into influence sign
    // ========================================================================
    
    // HeightBlendMode: 0=Replace, 1=Add, 2=Subtract, 3=Min, 4=Max, 5=Blend
    int blend_mode = int(zone.height_blend_mode);
    
    // For Add mode (embankments, rims), encode as negative
    // This tells the height application shader to ADD rather than blend toward path height
    if (blend_mode == 1 && zone.height_offset > 0.0) {
        // Positive offset with Add mode = embankment (raise terrain)
        influence = -abs(influence);
    } else if (blend_mode == 2) {
        // Subtract mode = carve deeper
        influence = abs(influence);
    }
    
    // Clamp to valid range
    influence = clamp(influence, -1.0, 1.0);
    
    imageStore(mask_output, pixel_coord, vec4(influence, 0.0, 0.0, 0.0));
}