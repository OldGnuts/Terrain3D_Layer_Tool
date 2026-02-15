// /Shaders/Brushes/height_smooth.glsl
#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Binding 0: Height delta (for undo tracking)
layout(r32f, set = 0, binding = 0) uniform image2D u_height_delta;

// Binding 1: HeightMap (for real-time display AND reading current heights)
layout(r32f, set = 0, binding = 1) uniform image2D u_height_map;

layout(push_constant) uniform PushConstants {
    float brush_center_x;    // 0
    float brush_center_z;    // 4
    float brush_radius;      // 8
    float strength;          // 12
    int falloff_type;        // 16
    int kernel_size;         // 20
    int bounds_min_x;        // 24
    int bounds_min_y;        // 28
    int region_world_x;      // 32
    int region_world_z;      // 36
    int region_size;         // 40
    int is_circle;           // 44
    // Total: 48 bytes, pad to 64
    int _pad0;               // 48
    int _pad1;               // 52
    int _pad2;               // 56
    int _pad3;               // 60
} pc;

float calculate_falloff(float distance, float radius) {
    float t = clamp(distance / radius, 0.0, 1.0);
    
    switch (pc.falloff_type) {
        case 0: return 1.0 - t;
        case 1: return 1.0 - smoothstep(0.0, 1.0, t);
        case 2: return 1.0;
        case 3: return 1.0 - (t * t);
        default: return 1.0 - t;
    }
}

float sample_height(ivec2 pos) {
    pos = clamp(pos, ivec2(0), ivec2(pc.region_size - 1));
    return imageLoad(u_height_map, pos).r;
}

void main() {
    ivec2 local_pos = ivec2(gl_GlobalInvocationID.xy);
    ivec2 pixel_pos = ivec2(pc.bounds_min_x, pc.bounds_min_y) + local_pos;
    
    if (pixel_pos.x < 0 || pixel_pos.x >= pc.region_size ||
        pixel_pos.y < 0 || pixel_pos.y >= pc.region_size) {
        return;
    }
    
    float world_x = float(pc.region_world_x + pixel_pos.x);
    float world_z = float(pc.region_world_z + pixel_pos.y);
    
    float dx = world_x - pc.brush_center_x;
    float dz = world_z - pc.brush_center_z;
    
    float distance;
    if (pc.is_circle == 1) {
        distance = sqrt(dx * dx + dz * dz);
    } else {
        distance = max(abs(dx), abs(dz));
    }
    
    if (distance > pc.brush_radius) {
        return;
    }
    
    // Calculate smoothed height using box filter
    float sum = 0.0;
    int count = 0;
    int half_kernel = pc.kernel_size / 2;
    
    for (int ky = -half_kernel; ky <= half_kernel; ky++) {
        for (int kx = -half_kernel; kx <= half_kernel; kx++) {
            sum += sample_height(pixel_pos + ivec2(kx, ky));
            count++;
        }
    }
    
    float smoothed_height = sum / float(count);
    float current_height = sample_height(pixel_pos);
    
    float falloff = calculate_falloff(distance, pc.brush_radius);
    float blend = pc.strength * falloff;
    
    // Calculate the delta needed to move toward smoothed height
    float target_height = mix(current_height, smoothed_height, blend);
    float delta = target_height - current_height;
    
    // Write delta to height delta (for undo)
    float existing_delta = imageLoad(u_height_delta, pixel_pos).r;
    imageStore(u_height_delta, pixel_pos, vec4(existing_delta + delta, 0.0, 0.0, 1.0));
    
    // Write new height to heightmap (for real-time display)
    imageStore(u_height_map, pixel_pos, vec4(target_height, 0.0, 0.0, 1.0));
}