#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Binding 0: Height map to modify
layout(rgba32f, set = 0, binding = 0) uniform image2D heightMap;

// Binding 1: Path segment data
struct PathSegment {
    float start_x;
    float start_y;
    float start_height;
    float end_x;
    float end_y;
    float end_height;
    float width_modifier;
    float flow_direction;
};

layout(std430, set = 0, binding = 1) readonly buffer PathSegments {
    PathSegment segments[];
};

// Binding 2: Carve curve data
layout(std430, set = 0, binding = 2) readonly buffer CarveCurveData {
    int carve_curve_point_count;
    float carve_curve_values[];
};

// Binding 3: Embankment curve data
layout(std430, set = 0, binding = 3) readonly buffer EmbankmentCurveData {
    int embankment_curve_point_count;
    float embankment_curve_values[];
};

// Push constants - ALIGNED TO 64 BYTES
layout(push_constant) uniform Params {
    int segment_count;              // offset 0
    float path_width;               // offset 4
    float carve_strength;           // offset 8
    float path_elevation;           // offset 12
    float terrain_conformance;      // offset 16
    int create_embankments;         // offset 20
    float embankment_width;         // offset 24
    float embankment_height;        // offset 28
    float embankment_falloff;       // offset 32
    float river_depth;              // offset 36
    float road_camber;              // offset 40
    int pad1;                 // 4 bytes padding at offset 44
    float region_min_world_x;       // offset 48
    float region_min_world_y;       // offset 52
    float region_size;              // offset 56
    int pad0;                 // 4 bytes padding at offset 60
} params;

// Sample carve curve with interpolation
float sample_carve_curve(float t) {
    if (carve_curve_point_count <= 0) return t;
    
    float index = t * float(carve_curve_point_count - 1);
    int i0 = int(floor(index));
    int i1 = min(i0 + 1, carve_curve_point_count - 1);
    float frac = fract(index);
    
    return mix(carve_curve_values[i0], carve_curve_values[i1], frac);
}

// Sample embankment curve with interpolation
float sample_embankment_curve(float t) {
    if (embankment_curve_point_count <= 0) return t;
    
    float index = t * float(embankment_curve_point_count - 1);
    int i0 = int(floor(index));
    int i1 = min(i0 + 1, embankment_curve_point_count - 1);
    float frac = fract(index);
    
    return mix(embankment_curve_values[i0], embankment_curve_values[i1], frac);
}

// Calculate distance from point to line segment
float distance_to_segment(vec2 point, vec2 seg_start, vec2 seg_end, out float t) {
    vec2 seg_vec = seg_end - seg_start;
    float seg_length_sq = dot(seg_vec, seg_vec);
    
    if (seg_length_sq < 0.0001) {
        t = 0.0;
        return distance(point, seg_start);
    }
    
    t = clamp(dot(point - seg_start, seg_vec) / seg_length_sq, 0.0, 1.0);
    vec2 projection = seg_start + t * seg_vec;
    return distance(point, projection);
}

void main() {
    ivec2 pixel_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 image_size = imageSize(heightMap);
    
    if (pixel_coord.x >= image_size.x || pixel_coord.y >= image_size.y) {
        return;
    }
    
    // Convert pixel coordinate to world space
    vec2 region_min_world = vec2(params.region_min_world_x, params.region_min_world_y);
    vec2 world_pos = region_min_world + vec2(pixel_coord) * (params.region_size / float(image_size.x));
    
    // Read current height
    vec4 current_height = imageLoad(heightMap, pixel_coord);
    float height = current_height.r;
    
    float total_influence = 0.0;
    float accumulated_height_change = 0.0;
    float closest_distance = 1000000.0;
    float closest_segment_t = 0.0;
    int closest_segment_idx = -1;
    
    // Find closest segment and calculate influence
    for (int i = 0; i < params.segment_count; i++) {
        PathSegment seg = segments[i];
        
        float t;
        vec2 seg_start = vec2(seg.start_x, seg.start_y);
        vec2 seg_end = vec2(seg.end_x, seg.end_y);
        float dist = distance_to_segment(world_pos, seg_start, seg_end, t);
        
        if (dist < closest_distance) {
            closest_distance = dist;
            closest_segment_t = t;
            closest_segment_idx = i;
        }
    }
    
    // If we found a segment within influence range
    if (closest_segment_idx >= 0) {
        PathSegment seg = segments[closest_segment_idx];
        float effective_width = params.path_width * seg.width_modifier;
        float max_influence_distance = effective_width * 0.5 + params.embankment_width + params.embankment_falloff;
        
        if (closest_distance < max_influence_distance) {
            // Calculate normalized distance (0 = center, 1 = edge of influence)
            float normalized_dist = closest_distance / (effective_width * 0.5);
            
            // Center path carving
            if (normalized_dist <= 1.0) {
                // 1. Get the brush shape factor from the curve (0.0 to 1.0).
                float carve_factor = sample_carve_curve(1.0 - normalized_dist);
                
                // 2. Calculate the absolute target height for the path's centerline.
                float segment_height = mix(seg.start_height, seg.end_height, closest_segment_t);
                float target_path_height = segment_height + params.path_elevation;
                
                // 3. Calculate the blend amount. This is the key change.
                // We multiply the shape by the strength, BUT THEN we clamp the result
                // to the [0.0, 1.0] range. This prevents the "explosion" from extrapolation
                // while still allowing a strength > 1.0 to mean "maximum effect".
                float blend_amount = clamp(carve_factor * params.carve_strength, 0.0, 1.0);
                
                // 4. Stably blend from the current height to the target height.
                // Because blend_amount is now guaranteed to be in [0,1], this can never explode.
                float new_height = mix(height, target_path_height, blend_amount);
                
                // Calculate the change for the final step.
                accumulated_height_change = new_height - height;
                
                total_influence = carve_factor;
            }
            // Embankment area
            else if (params.create_embankments == 1 && normalized_dist < (1.0 + params.embankment_width / (effective_width * 0.5))) {
                float embankment_t = (normalized_dist - 1.0) / (params.embankment_width / (effective_width * 0.5));
                float embankment_factor = sample_embankment_curve(embankment_t);
                
                accumulated_height_change = params.embankment_height * embankment_factor;
                total_influence = embankment_factor * 0.5;
            }
        }
    }
    
    // Apply height change
    if (total_influence > 0.001) {
        float new_height = height + accumulated_height_change;
        imageStore(heightMap, pixel_coord, vec4(new_height, 0.0, 0.0, 1.0));
    }
}