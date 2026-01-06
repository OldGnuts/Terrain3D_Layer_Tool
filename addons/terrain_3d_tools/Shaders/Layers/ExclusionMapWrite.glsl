// /Shaders/Layers/ExclusionMapWrite.glsl
#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Exclusion map (read-write for MAX operation)
layout(set = 0, binding = 0, r32f) uniform image2D exclusionMap;

// Source influence texture (e.g., path SDF or layer mask)
layout(set = 0, binding = 1) uniform sampler2D influenceSource;

layout(push_constant) uniform Params {
    // Region pixel bounds
    int regionMinX;
    int regionMinY;
    int regionMaxX;
    int regionMaxY;
    
    // Mask UV bounds (in source texture space)
    float maskMinU;
    float maskMinV;
    float maskMaxU;
    float maskMaxV;
    
    // Influence parameters
    float influenceRadius;    // How far the exclusion extends
    float exclusionStrength;  // Maximum exclusion value (usually 1.0)
};

void main() {
    ivec2 pixelCoord = ivec2(gl_GlobalInvocationID.xy);
    ivec2 imageSize = imageSize(exclusionMap);
    
    // Bounds check
    if (pixelCoord.x >= imageSize.x || pixelCoord.y >= imageSize.y) {
        return;
    }
    
    // Check if pixel is in the affected region
    if (pixelCoord.x < regionMinX || pixelCoord.x > regionMaxX ||
        pixelCoord.y < regionMinY || pixelCoord.y > regionMaxY) {
        return;
    }
    
    // Calculate UV in the region
    vec2 regionUV = vec2(pixelCoord) / vec2(imageSize);
    
    // Map to source texture UV
    float u = mix(maskMinU, maskMaxU, regionUV.x);
    float v = mix(maskMinV, maskMaxV, regionUV.y);
    
    // Check if within source bounds
    if (u < 0.0 || u > 1.0 || v < 0.0 || v > 1.0) {
        return;
    }
    
    // Sample the influence source
    // For path SDF: value represents signed distance, negative = inside path
    // For other masks: value represents direct influence (0-1)
    float sourceValue = texture(influenceSource, vec2(u, v)).r;
    
    // For SDF-based sources (paths), convert distance to exclusion
    // Negative distance = inside path = full exclusion
    // Positive distance = outside, with falloff
    float exclusion;
    if (influenceRadius > 0.0) {
        // SDF mode: convert signed distance to exclusion with falloff
        float normalizedDist = sourceValue / influenceRadius;
        exclusion = 1.0 - clamp(normalizedDist, 0.0, 1.0);
    } else {
        // Direct mask mode: use source value directly
        exclusion = clamp(sourceValue, 0.0, 1.0);
    }
    
    exclusion *= exclusionStrength;
    
    // Read current exclusion value
    float currentExclusion = imageLoad(exclusionMap, pixelCoord).r;
    
    // MAX blend: take the higher exclusion value
    float newExclusion = max(currentExclusion, exclusion);
    
    // Write back
    imageStore(exclusionMap, pixelCoord, vec4(newExclusion, 0.0, 0.0, 0.0));
}