// DebugPreviewShaderRegion.gdshader
shader_type spatial;
render_mode unshaded;

uniform sampler2D height_tex : repeat_disable;
uniform float height_scale = 1.0;
uniform float value_min = -0.0; // expected min range of your data
uniform float value_max = 2000.0;  // expected max range of your data

void vertex() {
    float h = texture(height_tex, UV).r;
    VERTEX.y += h * height_scale * 128.0;
}

void fragment() {
    float h = texture(height_tex, UV).r;

    // Normalize: map from [value_min, value_max] -> [0,1]
    //float normalized = (h - value_min) / (value_max - value_min);
    float normalized = h;
    //normalized = clamp(normalized, 0.0, 1.0);
    
    ALBEDO = vec3(0,0,normalized); 
    //if (normalized == 1.0) ALBEDO = vec3(0,normalized,normalized);
    if (normalized > 1.0) ALBEDO = vec3(normalized - 1.0 ,0,1);
    
    ALPHA = 0.6f;
}