shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_always, cull_disabled;

// The geometry (Stitched Terrain Height)
uniform sampler2D terrain_height_tex : repeat_disable, filter_linear;

// The data (Texture Influence 0..1)
uniform sampler2D layer_data_tex : repeat_disable, filter_linear;

uniform bool use_terrain_height = true;
uniform float height_multiplier = 128.0;

void vertex() {
    if (use_terrain_height) {
        float h = texture(terrain_height_tex, UV).r;
        VERTEX.y += h * height_multiplier; 
        
        // Slight offset to prevent Z-fighting
        VERTEX.y += 0.1; 
    }
}

void fragment() {
    // Texture layers are usually 0.0 to 1.0 influence
    float influence = texture(layer_data_tex, UV).r;
    
    // Violet color scheme for textures
    vec3 color = vec3(0.6, 0.2, 1.0);
    
    // Scale alpha by influence
    // If influence is 0, alpha is very low (nearly invisible)
    float alpha = clamp(influence * 0.8, 0.05, 0.8);

    // Highlight strong influence areas with brighter color
    if (influence > 0.8) {
        color = vec3(0.8, 0.4, 1.0);
    }

    ALBEDO = color;
    ALPHA = alpha;
}