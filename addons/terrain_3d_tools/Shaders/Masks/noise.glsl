#[compute]
#version 450
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

layout(set = 0, binding = 0, r32f) uniform image2D maskTex;

layout(push_constant, std430) uniform Params {
    int   noise_type;
    int   seed;
    int   invert;
    int   octaves;

    float frequency;
    float amplitude;    
    float lacunarity;
    float gain;

    float ridge_offset;
    float ridge_gain;
    float layer_mix;    // blend strength
    int   blend_type;   // 0=Mix, 1=Multiply, 2=Add, 3=Subtract
} params;

// ---------------- UTILITIES ----------------
const float TWO_PI = 6.28318530718;

vec2 seed_ofs() {
    return vec2(0.12345 * float(params.seed), 1.54321 * float(params.seed));
}

float hash12(vec2 p) {
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123);
}

vec2 grad2(vec2 p) {
    float a = TWO_PI * hash12(p);
    return vec2(cos(a), sin(a));
}

// ---------------- VALUE NOISE (0..1) ----------------
float value_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);

    vec2 so = seed_ofs();
    float a = hash12(i + vec2(0.0,0.0) + so);
    float b = hash12(i + vec2(1.0,0.0) + so);
    float c = hash12(i + vec2(0.0,1.0) + so);
    float d = hash12(i + vec2(1.0,1.0) + so);

    float x1 = mix(a, b, u.x);
    float x2 = mix(c, d, u.x);
    return mix(x1, x2, u.y); // 0..1
}

// ---------------- PERLIN NOISE (~[-1..1]) ----------------

float perlin_noise(vec2 p) {
    vec2 i = floor(p);
    vec2 f = fract(p);
    vec2 u = f*f*f*(f*(f*6.0 - 15.0) + 10.0);

    vec2 so = seed_ofs();
    vec2 g00 = grad2(i + vec2(0,0) + so);
    vec2 g10 = grad2(i + vec2(1,0) + so);
    vec2 g01 = grad2(i + vec2(0,1) + so);
    vec2 g11 = grad2(i + vec2(1,1) + so);

    float n00 = dot(g00, f - vec2(0,0));
    float n10 = dot(g10, f - vec2(1,0));
    float n01 = dot(g01, f - vec2(0,1));
    float n11 = dot(g11, f - vec2(1,1));

    float nx0 = mix(n00, n10, u.x);
    float nx1 = mix(n01, n11, u.x);
    return mix(nx0, nx1, u.y); // ~[-1,1]
}

// ---------------- SIMPLEX NOISE (~[-1..1]) ----------------
vec3 mod289(vec3 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
vec2 mod289(vec2 x) { return x - floor(x * (1.0/289.0)) * 289.0; }
vec3 permute(vec3 x){ return mod289((x*34.0+1.0)*x); }

float simplex_noise(vec2 v) {
    v += seed_ofs();

    const vec4 C = vec4(
        0.211324865405187, // (3.0 - sqrt(3.0))/6.0
        0.366025403784439, // (sqrt(3.0) - 1.0)/2.0
       -0.577350269189626, // -1.0/sqrt(3.0)
        0.024390243902439  // 1/41
    );

    vec2 i  = floor(v + dot(v, C.yy));
    vec2 x0 = v - i + dot(i, C.xx);
    vec2 i1 = (x0.x > x0.y) ? vec2(1.0,0.0) : vec2(0.0,1.0);
    vec4 x12 = x0.xyxy + C.xxzz;
    x12.xy -= i1;

    i = mod289(i);
    vec3 p = permute(permute(i.y + vec3(0.0, i1.y, 1.0))
                   + i.x + vec3(0.0, i1.x, 1.0));

    vec3 x = fract(p * C.www) * 2.0 - 1.0;
    vec3 h = abs(x) - 0.5;
    vec3 ox = floor(x + 0.5);
    vec3 a0 = x - ox;

    vec2 g0 = vec2(a0.x, h.x);
    vec2 g1 = vec2(a0.y, h.y);
    vec2 g2 = vec2(a0.z, h.z);

    vec3 norm = 1.79284291400159 - 0.85373472095314 *
                (vec3(dot(g0,g0), dot(g1,g1), dot(g2,g2)));
    g0 *= norm.x;
    g1 *= norm.y;
    g2 *= norm.z;

    float m0 = max(0.5 - dot(x0,x0), 0.0);
    float m1 = max(0.5 - dot(x12.xy,x12.xy), 0.0);
    float m2 = max(0.5 - dot(x12.zw,x12.zw), 0.0);

    m0 = m0*m0; m1 = m1*m1; m2 = m2*m2;

    return 130.0 * (m0*m0*dot(g0,x0) +
                    m1*m1*dot(g1,x12.xy) +
                    m2*m2*dot(g2,x12.zw));
}

// ---------------- FRACTAL NOISE (FBM) ----------------
float fbm(vec2 p, int type) {
    float sum = 0.0;
    float amp = 1.0;
    float freq = 1.0;

    // Precompute max possible sum
    float maxAmp = (params.gain == 1.0) ? float(params.octaves)
                   : (1.0 - pow(params.gain, params.octaves)) / (1.0 - params.gain);

    for (int i=0; i<params.octaves; i++) {
        // Rotate/offset based on octave index to break grid alignment
        vec2 offset = vec2(37.0 * i, 17.0 * i); 
        float angle = 0.5 * float(i); // in radians; adjust constant as you like
        mat2 rot = mat2(cos(angle), -sin(angle), sin(angle), cos(angle));

        vec2 sampleP = rot * (p * freq + offset);

        float n = 0.0;
        if (type == 0) n = value_noise(sampleP);
        else if (type == 1) n = perlin_noise(sampleP) * 0.5 + 0.5;
        else if (type == 2) n = simplex_noise(sampleP) * 0.5 + 0.5;

        sum += n * amp;
        freq *= params.lacunarity;
        amp  *= params.gain;
    }
    return sum / maxAmp;
}

// ---------------- RIDGE MULTIFRACTAL ----------------
float ridge_noise(vec2 p) {
    float sum = 0.0;
    float freq = 1.0;
    float amp = 0.5;
    float prev = 1.0;

    float totalAmp = 0.0;

    for (int i = 0; i < params.octaves; i++) {
        // Rotate/offset based on octave index to break grid alignment
        vec2 offset = vec2(37.0 * i, 17.0 * i); 
        float angle = 0.5 * float(i); // in radians; adjust constant as you like
        mat2 rot = mat2(cos(angle), -sin(angle), sin(angle), cos(angle));

        vec2 sampleP = rot * (p * freq + offset);

        float n = simplex_noise(sampleP * freq); // [-1,1]
        n = abs(n);                        // [0,1]
        n = params.ridge_offset - n;
        n *= n;

        float weight = amp * prev;
        sum += n * weight;
        totalAmp += weight;

        prev = n * params.ridge_gain;
        freq *= params.lacunarity;
        amp *= params.gain;
    }

    return sum / totalAmp; // normalized ridge [0,1]
}
// ---------------- MAIN ----------------
void main() {
    ivec2 uv = ivec2(gl_GlobalInvocationID.xy);
    float base = imageLoad(maskTex, uv).r;
    vec2 coord = vec2(uv) * params.frequency;

    float n = 0.0;
    if (params.noise_type == 0) { // value
        n = fbm(coord,0);
    } else if (params.noise_type == 1) { // perlin
        n = fbm(coord,1);
    } else if (params.noise_type == 2) { // simplex
        n = fbm(coord,2);
    } else if (params.noise_type == 3) { // ridge multifractal
        n = ridge_noise(coord);
    }

    n = clamp(0.5 + (n - 0.5) * params.amplitude, 0.0, 1.0);

    float noisy;
    if (params.blend_type == 0) { 
        // Mix/Lerp (current behavior)
        noisy = mix(base, n, params.layer_mix);
    } 
    else if (params.blend_type == 1) { 
        // Multiply
        noisy = mix(base, base * n, params.layer_mix);
    }
    else if (params.blend_type == 2) { 
        // Add
        noisy = mix(base, base + n, params.layer_mix);
    } 
    else if (params.blend_type == 3) { 
        // Subtract
        noisy = mix(base, base - n, params.layer_mix);
    }

    // Clamp to -1 1 to keep data safe
    noisy = clamp(noisy, -1.0, 1.0);

    if (params.invert == 1) noisy = 1.0 - noisy;

    imageStore(maskTex, uv, vec4(noisy));
}