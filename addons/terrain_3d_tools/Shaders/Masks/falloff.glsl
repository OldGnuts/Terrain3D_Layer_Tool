#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D maskTex;

layout(set = 0, binding = 1, std430) buffer CurveData {
    int pointCount;
    float values[];
} curve;

layout(push_constant) uniform PushConstants {
    int falloffType;   // 0=None, 1=Linear, 2=Circular
    float strength;
    float sizeX;
    float sizeY;
} params;

float sampleCurve(float t) {
    if (curve.pointCount == 0) return 0.0;
    if (curve.pointCount == 1) return clamp(curve.values[0], 0.0, 1.0);

    t = clamp(t, 0.0, 1.0);
    
    float f_idx = t * float(curve.pointCount - 1);
    int idx0 = int(f_idx);
    int idx1 = min(idx0 + 1, curve.pointCount - 1);
    
    float val0 = curve.values[idx0];
    float val1 = curve.values[idx1];
    float frac = fract(f_idx);
    
    return mix(val0, val1, frac);
}

void main() {
    ivec2 gid = ivec2(gl_GlobalInvocationID.xy);
    ivec2 size = imageSize(maskTex);
    if (gid.x >= size.x || gid.y >= size.y) return;

    float value = imageLoad(maskTex, gid).r;

    // Normalized coords
    vec2 uv = vec2(gid) / vec2(size);
    vec2 rel = (uv - 0.5) * 2.0;

    float distLinear = max(abs(rel.x), abs(rel.y));
    float distCircular = length(rel);

    float baseFalloff = 1.0;
    if (params.falloffType == 1) // Linear
        baseFalloff = clamp(1.0 - distLinear, 0.0, 1.0);
    else if (params.falloffType == 2) // Circular
        baseFalloff = clamp(1.0 - distCircular, 0.0, 1.0);

    // Remap with curve LUT
    float curvedFalloff = sampleCurve(baseFalloff);

    // Blend with FalloffStrength
    float finalFalloff = mix(1.0, curvedFalloff, params.strength);

    imageStore(maskTex, gid, vec4(value * finalFalloff, 0, 0, 1));
}