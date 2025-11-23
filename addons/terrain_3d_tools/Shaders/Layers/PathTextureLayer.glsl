#[compute]
#version 450

// PathTextureLayer.glsl - Compute shader for path texture modifications
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) restrict uniform image2D control_map;
layout(set = 0, binding = 1, std430) restrict readonly buffer PathDataBuffer {
    float path_data[];
};

layout(push_constant) uniform PushConstants {
    int path_segment_count;
    float path_width;
    float embankment_width;
    float transition_width;
    int texture_mode; // 0=SingleTexture, 1=CenterEmbankment, 2=TripleTexture
    uint center_texture_id;
    uint embankment_texture_id;
    uint transition_texture_id;
    float texture_blend_strength;
    int river_flow_direction; // boolean for flow-based texturing
    float river_flow_strength;
    float region_min_x;
    float region_min_y;
    float region_size;
    // Padding to align to 16 bytes
    float _pad1;
    float _pad2;
};

const float EPSILON = 0.001;
const int PATH_SEGMENT_SIZE = 8; // floats per segment

float distance_to_line_segment(vec2 point, vec2 line_start, vec2 line_end) {
    vec2 line_dir = line_end - line_start;
    float line_length_sq = dot(line_dir, line_dir);
    
    if (line_length_sq < EPSILON) {
        return distance(point, line_start);
    }
    
    vec2 point_to_start = point - line_start;
    float t = clamp(dot(point_to_start, line_dir) / line_length_sq, 0.0, 1.0);
    vec2 projection = line_start + t * line_dir;
    
    return distance(point, projection);
}

// Encode texture ID into the control map value
// This is a simplified version - in a real system you might want more sophisticated texture blending
float encode_texture_id(uint texture_id, float strength) {
    // Simple encoding: texture_id + fractional strength
    // In practice, you'd want a more sophisticated system for multiple texture blending
    return float(texture_id) + clamp(strength, 0.0, 0.99);
}

void main() {
    ivec2 coord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 image_size = imageSize(control_map);
    
    if (coord.x >= image_size.x || coord.y >= image_size.y) {
        return;
    }
    
    // Convert pixel coordinate to world position
    vec2 world_pos = vec2(region_min_x, region_min_y) + 
                     (vec2(coord) / vec2(image_size)) * region_size;
    
    float original_control = imageLoad(control_map, coord).r;
    float texture_influence = 0.0;
    uint selected_texture_id = center_texture_id;
    float blend_factor = 0.0;
    
    // Process each path segment
    for (int i = 0; i < path_segment_count; i++) {
        int base_idx = i * PATH_SEGMENT_SIZE;
        
        vec2 segment_start = vec2(path_data[base_idx], path_data[base_idx + 1]);
        // float start_height = path_data[base_idx + 2]; // Not needed for texture
        vec2 segment_end = vec2(path_data[base_idx + 3], path_data[base_idx + 4]);
        // float end_height = path_data[base_idx + 5]; // Not needed for texture
        float width_modifier = path_data[base_idx + 6];
        float flow_direction = path_data[base_idx + 7];
        
        float segment_width = path_width * width_modifier;
        float distance_to_segment = distance_to_line_segment(world_pos, segment_start, segment_end);
        
        // Calculate maximum influence distance based on texture mode
        float max_influence_distance = segment_width * 0.5;
        if (texture_mode == 1 || texture_mode == 2) { // CenterEmbankment or TripleTexture
            max_influence_distance += embankment_width;
            if (texture_mode == 2) {
                max_influence_distance += transition_width;
            }
        }
        
        if (distance_to_segment <= max_influence_distance) {
            float influence = 0.0;
            uint texture_id = center_texture_id;
            
            // Determine texture zone and influence
            if (distance_to_segment <= segment_width * 0.5) {
                // Center/Path zone
                influence = texture_blend_strength;
                texture_id = center_texture_id;
                
                // Apply flow-based variation for rivers
                if (river_flow_direction == 1 && river_flow_strength > 0.0) {
                    // Calculate perpendicular distance for flow effects
                    vec2 segment_dir = normalize(segment_end - segment_start);
                    vec2 to_point = world_pos - segment_start;
                    float perpendicular_dist = abs(dot(to_point, vec2(-segment_dir.y, segment_dir.x)));
                    
                    // Modify influence based on flow pattern
                    float flow_factor = 1.0 + sin(perpendicular_dist * 3.14159 / segment_width) * river_flow_strength * 0.2;
                    influence *= flow_factor;
                }
            }
            else if ((texture_mode == 1 || texture_mode == 2) && 
                     distance_to_segment <= segment_width * 0.5 + embankment_width) {
                // Embankment zone
                float embankment_distance = distance_to_segment - segment_width * 0.5;
                float embankment_t = clamp(embankment_distance / embankment_width, 0.0, 1.0);
                
                influence = texture_blend_strength * (1.0 - embankment_t * 0.5);
                texture_id = embankment_texture_id;
            }
            else if (texture_mode == 2 && 
                     distance_to_segment <= segment_width * 0.5 + embankment_width + transition_width) {
                // Transition zone (only in TripleTexture mode)
                float transition_distance = distance_to_segment - (segment_width * 0.5 + embankment_width);
                float transition_t = clamp(transition_distance / transition_width, 0.0, 1.0);
                
                influence = texture_blend_strength * (1.0 - transition_t) * 0.3; // Reduced influence for transitions
                texture_id = transition_texture_id;
            }
            
            // Accumulate the strongest influence
            if (influence > texture_influence) {
                texture_influence = influence;
                selected_texture_id = texture_id;
                blend_factor = influence;
            }
        }
    }
    
    // Apply texture modification
    if (texture_influence > EPSILON) {
        float new_control_value = encode_texture_id(selected_texture_id, blend_factor);
        
        // Blend with existing control value
        float final_control = mix(original_control, new_control_value, 
                                 clamp(texture_influence, 0.0, 1.0));
        
        imageStore(control_map, coord, vec4(final_control, 0.0, 0.0, 1.0));
    }
}