shader_type spatial;
render_mode unshaded, blend_mix, depth_test_disabled, cull_disabled;

// The geometry (Stitched Terrain Height)
uniform sampler2D terrain_height_tex : repeat_disable, filter_linear;

// The data (Texture Influence 0..1)
uniform sampler2D layer_data_tex : repeat_disable, filter_linear;

uniform bool use_terrain_height = true;
uniform float height_multiplier = 128.0;

// Falloff parameters for late-apply visualization
uniform bool apply_falloff = false;
uniform int falloff_type = 0; // 0=None, 1=Linear, 2=Circular
uniform float falloff_strength = 1.0;
uniform sampler2D falloff_curve_tex : repeat_disable, filter_linear;

// Visualization settings from GlobalToolSettings (no source_color hint)
uniform vec3 base_color = vec3(0.6, 0.2, 1.0);
uniform vec3 highlight_color = vec3(0.8, 0.4, 1.0);
uniform float base_opacity = 0.8;

// Calculate falloff multiplier
float calculate_falloff(vec2 uv) {
    if (!apply_falloff || falloff_type == 0 || falloff_strength <= 0.0) {
        return 1.0;
    }
    
    vec2 rel = (uv - 0.5) * 2.0;
    float dist;
    
    if (falloff_type == 1) { // Linear
        dist = max(abs(rel.x), abs(rel.y));
    } else { // Circular
        dist = length(rel);
    }
    
    float base_falloff_val = clamp(1.0 - dist, 0.0, 1.0);
    
    // Sample curve texture (1D texture, sample along U)
    float curved_falloff = texture(falloff_curve_tex, vec2(base_falloff_val, 0.5)).r;
    
    return mix(1.0, curved_falloff, falloff_strength);
}

void vertex() {
    if (use_terrain_height) {
        float h = texture(terrain_height_tex, UV).r;
        VERTEX.y += h * height_multiplier; 
        VERTEX.y += 0.1; 
    }
}

void fragment() {
    // Get raw mask influence
    float raw_influence = texture(layer_data_tex, UV).r;
    
    // Apply falloff for visualization
    float falloff = calculate_falloff(UV);
    float influence = raw_influence * falloff;
    
    // Use configurable colors
    vec3 color = base_color;
    
    // Scale alpha by influence
    float alpha = clamp(influence * base_opacity, 0.05, base_opacity);

    // Highlight strong influence areas with brighter color
    if (influence > 0.8) {
        color = highlight_color;
    }

    ALBEDO = color;
    ALPHA = alpha;
}