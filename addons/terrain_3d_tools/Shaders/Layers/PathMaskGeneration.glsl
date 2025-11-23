#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform restrict writeonly image2D output_mask;

struct PathSegment {
    vec2 start_pos;
    float start_height;
    float _padding1;
    vec2 end_pos;
    float end_height;
    float width_mult;
    float flow_direction;
    float _padding2;
};

layout(set = 0, binding = 1, std430) readonly buffer PathSegments {
    PathSegment segments[];
};

layout(set = 0, binding = 2, std430) readonly buffer CarveCurve {
    int carve_point_count;
    float carve_curve_values[];
};

layout(set = 0, binding = 3, std430) readonly buffer EmbankmentCurve {
    int embankment_point_count;
    float embankment_curve_values[];
};

layout(push_constant) uniform PushConstants {
    int segment_count;
    float path_width;
    float carve_strength;
    float path_elevation;
    float terrain_conformance;
    int create_embankments;
    float embankment_width;
    float embankment_height;
    float embankment_falloff;
    float river_depth;
    float road_camber;
    int texture_mode;
    uint center_texture_id;
    uint embankment_texture_id;
    float layer_width;
    float layer_height;
} pc;

float sample_carve_curve(float t) {
    if (carve_point_count == 0) return 1.0;
    t = clamp(t, 0.0, 1.0);
    float index_f = t * float(carve_point_count - 1);
    int index = int(floor(index_f));
    if (index >= carve_point_count - 1) return carve_curve_values[carve_point_count - 1];
    if (index < 0) return carve_curve_values[0];
    
    float frac = fract(index_f);
    return mix(carve_curve_values[index], carve_curve_values[index + 1], frac);
}

float sample_embankment_curve(float t) {
    if (embankment_point_count == 0) return 1.0;
    t = clamp(t, 0.0, 1.0);
    float index_f = t * float(embankment_point_count - 1);
    int index = int(floor(index_f));
    if (index >= embankment_point_count - 1) return embankment_curve_values[embankment_point_count - 1];
    if (index < 0) return embankment_curve_values[0];
    
    float frac = fract(index_f);
    return mix(embankment_curve_values[index], embankment_curve_values[index + 1], frac);
}

float point_segment_distance(vec2 p, vec2 a, vec2 b) {
    vec2 ab = b - a;
    vec2 ap = p - a;
    
    float ab_length_sq = dot(ab, ab);
    
    if (ab_length_sq < 0.0001) {
        return length(ap);
    }
    
    float t = clamp(dot(ap, ab) / ab_length_sq, 0.0, 1.0);
    vec2 closest = a + ab * t;
    
    return distance(p, closest);
}

void main() {
    ivec2 pixel_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 mask_size = imageSize(output_mask);
    
    if (pixel_coord.x >= mask_size.x || pixel_coord.y >= mask_size.y) {
        return;
    }
    
    vec2 pixel_pos = vec2(pixel_coord);
    
    // Find minimum distance to the path centerline
    float min_distance = 10000.0;
    
    for (int i = 0; i < pc.segment_count; i++) {
        PathSegment seg = segments[i];
        float dist = point_segment_distance(pixel_pos, seg.start_pos, seg.end_pos);
        min_distance = min(min_distance, dist);
    }
    
    float half_width = pc.path_width * 0.5;
    float influence = 0.0;
    
    // ZONE 1: Inside the path center
    if (min_distance <= half_width) {
        float normalized = min_distance / half_width;
        influence = sample_carve_curve(normalized);
    }
    // ZONE 2: Embankment zone
    else if (pc.create_embankments != 0 && min_distance <= half_width + pc.embankment_width) {
        float embankment_dist = min_distance - half_width;
        float embankment_t = embankment_dist / pc.embankment_width;
        
        float curve_value = sample_embankment_curve(embankment_t);
        
        // Store negative to indicate embankment (raise terrain)
        influence = -curve_value;
    }
    // ZONE 3: Embankment falloff
    else if (pc.create_embankments != 0 && min_distance <= half_width + pc.embankment_width + pc.embankment_falloff) {
        float falloff_start = half_width + pc.embankment_width;
        float falloff_dist = min_distance - falloff_start;
        float falloff_t = falloff_dist / pc.embankment_falloff;
        
        influence = -(1.0 - falloff_t) * 0.5;
    }

        // SIMPLE TEST: Force embankment zone to a specific value to verify it's being written
    // Uncomment this to test:
    
    //if (pc.create_embankments != 0 && min_distance > half_width && min_distance <= half_width + pc.embankment_width) {
    //    influence = 0.8; // Should show as dark in the preview if negatives are preserved
    //}
    
    
    imageStore(output_mask, pixel_coord, vec4(influence));
}