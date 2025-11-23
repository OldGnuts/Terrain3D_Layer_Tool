shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_always, cull_disabled;

// The geometry (Stitched Terrain Height from previous frame/background)
uniform sampler2D terrain_height_tex : repeat_disable, filter_linear;

// The mask data (0.0 to 1.0 influence)
uniform sampler2D layer_data_tex : repeat_disable, filter_linear;

uniform bool use_terrain_height = false;
uniform float height_multiplier = 128.0;

// NEW: The strength of the layer being edited
uniform float layer_strength = 1.0;

void vertex() {
    float total_h = 0.0;
    
    // 1. Get Base Terrain Height
    if (use_terrain_height) {
        total_h += texture(terrain_height_tex, UV).r;
    }
    
    // 2. Add the Layer's own contribution
    // This allows us to see the displacement of the layer we are currently editing
    float mask_val = texture(layer_data_tex, UV).r;
    
    // Note: This assumes "Add" mode. 
    // If your mask logic produces negative values for subtraction, this works automatically.
    // If your mask is 0..1 and you subtract, visualizer might look additive unless we handle mode.
    // But for a generic preview, seeing the magnitude of change is usually sufficient.
    total_h += mask_val * layer_strength;

    VERTEX.y += total_h * height_multiplier; 
    
    // Slight offset to ensure the preview mesh sits on top of the actual terrain
    VERTEX.y += 0.05; 
}

void fragment() {
    float val = texture(layer_data_tex, UV).r;
    
    vec3 color;
    float alpha = 0.6;

    if (val > 0.0) {
        // Positive/Add (Green)
        color = vec3(0.0, 1.0, 0.0);
        alpha = clamp(val * 0.5 + 0.2, 0.2, 0.8);
    } 
    else if (val < 0.0) {
        // Negative/Subtract (Red)
        color = vec3(1.0, 0.0, 0.0);
        alpha = clamp(abs(val) * 0.5 + 0.2, 0.2, 0.8);
    } 
    else {
        // No change (Transparent)
        color = vec3(0.5, 0.5, 0.5);
        alpha = 0.0; // Fully transparent where no influence
    }

    ALBEDO = color;
    ALPHA = alpha;
}