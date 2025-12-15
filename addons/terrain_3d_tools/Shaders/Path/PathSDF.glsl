#[compute]
#version 450

/*
 * PathSDF.glsl - Generates Signed Distance Field and Zone Data for path layers
 * 
 * This shader computes:
 * 1. SDF texture (R32F): Signed distance from each pixel to the path centerline
 *    - Positive values = outside path
 *    - Zero = on centerline
 *    - Includes corner smoothing for natural junctions
 * 
 * 2. Zone data texture (RGBA32F):
 *    - R = Zone index (which profile zone this pixel falls into)
 *    - G = Parameter within zone (0 = inner edge, 1 = outer edge)
 *    - B = Normalized segment index (for height lookup)
 *    - A = Parameter along closest segment (0 = start, 1 = end)
 */

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Output textures
layout(set = 0, binding = 0, r32f) uniform restrict writeonly image2D sdf_output;
layout(set = 0, binding = 1, rgba32f) uniform restrict writeonly image2D zone_output;  

// Path segment data
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

layout(set = 0, binding = 2, std430) readonly buffer SegmentBuffer {
    PathSegment segments[];
};

// Profile data
layout(set = 0, binding = 3, std430) readonly buffer ProfileBuffer {
    // Header
    float zone_count;
    float global_smoothing;
    float symmetrical;
    float half_width;
    // Zone boundaries (cumulative distances from center)
    float zone_boundaries[8];
    // Zone data follows but we only need boundaries here
};

// Push constants
layout(push_constant) uniform PushConstants {
    int layer_width;
    int layer_height;
    vec2 layer_min_world;
    int segment_count;
    int pc_zone_count;
    float profile_half_width;
    float corner_smoothing;
    int smooth_corners;
    int _padding;
} pc;

// ============================================================================
// DISTANCE FUNCTIONS
// ============================================================================

/*
 * Compute the minimum distance from a point to a line segment.
 * Also outputs:
 *   - t: Parameter along segment (0 = start, 1 = end)
 *   - closest: The closest point on the segment
 */
float point_to_segment_distance(vec2 p, vec2 a, vec2 b, out float t, out vec2 closest) {
    vec2 ab = b - a;
    float ab_length_sq = dot(ab, ab);
    
    if (ab_length_sq < 0.0001) {
        // Degenerate segment (point)
        t = 0.0;
        closest = a;
        return distance(p, a);
    }
    
    // Project point onto line, clamped to segment
    t = clamp(dot(p - a, ab) / ab_length_sq, 0.0, 1.0);
    closest = a + ab * t;
    
    return distance(p, closest);
}

/*
 * Smooth minimum function for blending distances at corners.
 * Creates smooth transitions instead of hard creases.
 * k controls the smoothing radius.
 */
float smooth_min(float a, float b, float k) {
    if (k <= 0.0) return min(a, b);
    
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * k * 0.25;
}

/*
 * Polynomial smooth minimum - provides smoother results than basic smooth_min.
 */
float smooth_min_cubic(float a, float b, float k) {
    if (k <= 0.0) return min(a, b);
    
    float h = max(k - abs(a - b), 0.0) / k;
    return min(a, b) - h * h * h * k * (1.0 / 6.0);
}

// ============================================================================
// ZONE CLASSIFICATION
// ============================================================================

/*
 * Determine which zone a given distance falls into.
 * Returns: zone index (0-based), or -1 if outside all zones
 * Also outputs: parameter within zone (0 = inner edge, 1 = outer edge)
 */
int get_zone_at_distance(float dist, out float zone_param) {
    float abs_dist = abs(dist);
    float accumulated = 0.0;
    
    int zone_cnt = int(zone_count);
    
    for (int i = 0; i < zone_cnt && i < 8; i++) {
        float zone_end = zone_boundaries[i];
        
        if (abs_dist <= zone_end) {
            float zone_start = (i == 0) ? 0.0 : zone_boundaries[i - 1];
            float zone_width = zone_end - zone_start;
            
            if (zone_width > 0.001) {
                zone_param = (abs_dist - zone_start) / zone_width;
            } else {
                zone_param = 0.0;
            }
            
            return i;
        }
    }
    
    // Outside all zones
    zone_param = 1.0;
    return -1;
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
    
    // Pixel position in layer-local space
    vec2 pixel_pos = vec2(pixel_coord) + 0.5; // Center of pixel
    
    // ========================================================================
    // PHASE 1: Compute SDF (minimum distance to path centerline)
    // ========================================================================
    
    float min_distance = 1e10;
    float closest_t = 0.0;
    int closest_segment = -1;
    vec2 closest_point = vec2(0.0);
    
    // First pass: find the absolutely closest segment
    for (int i = 0; i < pc.segment_count; i++) {
        PathSegment seg = segments[i];
        
        float t;
        vec2 cp;
        float dist = point_to_segment_distance(pixel_pos, seg.start_pos, seg.end_pos, t, cp);
        
        if (dist < min_distance) {
            min_distance = dist;
            closest_t = t;
            closest_segment = i;
            closest_point = cp;
        }
    }
    
    // ========================================================================
    // PHASE 2: Apply corner smoothing
    // ========================================================================
    
    float smooth_distance = min_distance;
    
    if (pc.smooth_corners != 0 && pc.corner_smoothing > 0.0) {
        // Re-compute with smooth blending between nearby segments
        float smooth_k = pc.corner_smoothing;
        smooth_distance = 1e10;
        
        for (int i = 0; i < pc.segment_count; i++) {
            PathSegment seg = segments[i];
            
            float t;
            vec2 cp;
            float dist = point_to_segment_distance(pixel_pos, seg.start_pos, seg.end_pos, t, cp);
            
            // Only blend with segments that are reasonably close
            if (dist < min_distance + smooth_k * 3.0) {
                smooth_distance = smooth_min_cubic(smooth_distance, dist, smooth_k);
            }
        }
    }
    
    // ========================================================================
    // PHASE 3: Compute zone classification
    // ========================================================================
    
    float zone_param;
    int zone_index = get_zone_at_distance(smooth_distance, zone_param);
    
    // Encode zone as float (-1 becomes a special value)
    float zone_float = (zone_index >= 0) ? float(zone_index) : -1.0;
    
    // ========================================================================
    // PHASE 4: Compute additional data for height interpolation
    // ========================================================================
    
    // We store the parameter along the closest segment for height interpolation
    // This will be used by the height application shader
    float path_t = 0.0;
    
    if (closest_segment >= 0) {
        PathSegment seg = segments[closest_segment];
        path_t = mix(seg.start_distance, seg.end_distance, closest_t);
    }
    
    // ========================================================================
    // OUTPUT
    // ========================================================================
    
    // SDF output: signed distance (always positive for distance from centerline)
    imageStore(sdf_output, pixel_coord, vec4(smooth_distance, 0.0, 0.0, 0.0));
    
    // Zone output: 
    //   R = zone index (-1 if outside)
    //   G = parameter within zone (0-1)
    //   B = closest segment index (normalized 0-1)
    //   A = parameter along that segment (0-1)
    float seg_normalized = (closest_segment >= 0) ? float(closest_segment) / max(1.0, float(pc.segment_count - 1)) : 0.0;
    
    imageStore(zone_output, pixel_coord, vec4(zone_float, zone_param, seg_normalized, closest_t));
}