// /Shaders/DebugPreviewShaderFeature.gdshader
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_always, cull_disabled;

// The geometry (Stitched Terrain Height)
uniform sampler2D terrain_height_tex : repeat_disable, filter_linear;

// The mask data (influence values)
uniform sampler2D layer_data_tex : repeat_disable, filter_linear;

uniform bool use_terrain_height = true;
uniform float height_multiplier = 128.0;
uniform float layer_strength = 1.0;

// Visualization controls
uniform float preview_opacity : hint_range(0.0, 1.0) = 0.35;
uniform bool show_contours = true;
uniform float contour_spacing = 0.1;

void vertex() {
    float total_h = 0.0;
    
    if (use_terrain_height) {
        total_h += texture(terrain_height_tex, UV).r;
    }
    
    VERTEX.y += total_h * height_multiplier; 
    VERTEX.y += 0.08; // Slight offset above terrain
}

void fragment() {
    float val = texture(layer_data_tex, UV).r;
    
    vec3 color;
    float alpha = 0.0;
    
    float abs_val = abs(val);
    
    if (abs_val < 0.001) {
        // No influence - fully transparent
        discard;
    }
    else if (val > 0.0) {
        // Positive influence - blue/cyan gradient (path surface, raised areas)
        float intensity = clamp(val, 0.0, 1.0);
        color = mix(vec3(0.2, 0.5, 0.8), vec3(0.4, 0.9, 1.0), intensity);
        alpha = preview_opacity * (0.3 + intensity * 0.5);
    } 
    else {
        // Negative influence - orange/red gradient (carved areas, embankments)
        float intensity = clamp(-val, 0.0, 1.0);
        color = mix(vec3(0.8, 0.5, 0.2), vec3(1.0, 0.3, 0.1), intensity);
        alpha = preview_opacity * (0.3 + intensity * 0.5);
    }
    
    // Optional contour lines for better depth perception
    if (show_contours && abs_val > 0.01) {
        float contour = fract(abs_val / contour_spacing);
        if (contour < 0.05 || contour > 0.95) {
            color = mix(color, vec3(1.0), 0.4);
            alpha = min(alpha + 0.15, 0.8);
        }
    }
    
    // Edge fade - softer edges at layer boundaries
    float edge_dist = min(min(UV.x, 1.0 - UV.x), min(UV.y, 1.0 - UV.y));
    float edge_fade = smoothstep(0.0, 0.05, edge_dist);
    alpha *= edge_fade;
    
    ALBEDO = color;
    ALPHA = alpha;
}