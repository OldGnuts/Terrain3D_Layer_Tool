shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, cull_disabled;

uniform sampler2D terrain_height_tex : repeat_disable, filter_linear;
uniform sampler2D layer_data_tex : repeat_disable, filter_linear;

uniform bool use_terrain_height = false;
uniform float height_multiplier = 128.0;

// Used to normalize display - the max expected value in the mask
uniform float layer_strength = 128.0;

uniform vec3 positive_color = vec3(0.0, 1.0, 0.0);
uniform vec3 negative_color = vec3(1.0, 0.0, 0.0);
uniform float base_opacity = 0.6;

void vertex() {
    float total_h = 0.0;
    
    if (use_terrain_height) {
        total_h = texture(terrain_height_tex, UV).r;
    }

    VERTEX.y += total_h * height_multiplier; 
    VERTEX.y += 0.15; 
}

void fragment() {
    float raw_val = texture(layer_data_tex, UV).r;
    float abs_val = abs(raw_val);
    
    if (abs_val < 0.001) {
        discard;
    }
    
    // NORMALIZE by dividing by max expected value
    float intensity = clamp(abs_val / max(layer_strength, 0.001), 0.0, 1.0);
    
    vec3 color;
    if (raw_val > 0.0) {
        color = positive_color * (0.3 + intensity * 0.7);
    } else {
        color = negative_color * (0.3 + intensity * 0.7);
    }
    
    float alpha = base_opacity * intensity;
    alpha = max(alpha, 0.08);
    
    float edge_dist = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
    float edge_fade = smoothstep(0.0, 0.03, edge_dist);
    alpha *= edge_fade;

    ALBEDO = color;
    ALPHA = alpha;
}