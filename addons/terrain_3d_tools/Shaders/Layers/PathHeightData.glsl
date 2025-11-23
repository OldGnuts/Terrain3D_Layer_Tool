#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform restrict writeonly image2D output_height;

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

layout(push_constant) uniform PushConstants {
    int segment_count;
    float path_width;
    float layer_width;
    float layer_height;
} pc;

float point_segment_distance(vec2 p, vec2 a, vec2 b, out float t) {
    vec2 ab = b - a;
    vec2 ap = p - a;
    
    float ab_length_sq = dot(ab, ab);
    
    if (ab_length_sq < 0.0001) {
        t = 0.0;
        return length(ap);
    }
    
    t = clamp(dot(ap, ab) / ab_length_sq, 0.0, 1.0);
    vec2 closest = a + ab * t;
    
    return distance(p, closest);
}

void main() {
    ivec2 pixel_coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(output_height);
    
    if (pixel_coord.x >= size.x || pixel_coord.y >= size.y) {
        return;
    }
    
    vec2 pixel_pos = vec2(pixel_coord);
    float half_width = pc.path_width * 0.5;
    
    // NEW APPROACH: Find minimum height from ALL segments within path width
    // This handles junctions and overlapping segments correctly
    float min_height = 10000.0;
    bool found_any = false;
    
    for (int i = 0; i < pc.segment_count; i++) {
        PathSegment seg = segments[i];
        float t;
        float dist = point_segment_distance(pixel_pos, seg.start_pos, seg.end_pos, t);
        
        // Only consider segments that this pixel is actually ON (within path width)
        if (dist <= half_width) {
            float interpolated_height = mix(seg.start_height, seg.end_height, t);
            
            // Take the MINIMUM height from all segments we're on
            // This ensures valleys/dips are preserved
            min_height = min(min_height, interpolated_height);
            found_any = true;
        }
    }
    
    if (found_any) {
        imageStore(output_height, pixel_coord, vec4(min_height));
    } else {
        // Fallback: find closest segment if we're just outside path width
        // (for embankment zones)
        float min_distance = 10000.0;
        float fallback_height = 0.0;
        
        for (int i = 0; i < pc.segment_count; i++) {
            PathSegment seg = segments[i];
            float t;
            float dist = point_segment_distance(pixel_pos, seg.start_pos, seg.end_pos, t);
            
            if (dist < min_distance) {
                min_distance = dist;
                fallback_height = mix(seg.start_height, seg.end_height, t);
            }
        }
        
        imageStore(output_height, pixel_coord, vec4(fallback_height));
    }
}