// /Shaders/Brushes/height_brush.glsl
#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Binding 0: Height delta (for undo tracking)
layout(r32f, set = 0, binding = 0) uniform image2D u_height_delta;

// Binding 1: HeightMap (for real-time display)
layout(r32f, set = 0, binding = 1) uniform image2D u_height_map;

layout(push_constant) uniform PushConstants {
    float brush_center_x;    // 0
    float brush_center_z;    // 4
    float brush_radius;      // 8
    float height_delta;      // 12
    float strength;          // 16
    int falloff_type;        // 20
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
        case 0: // Linear
            return 1.0 - t;
        case 1: // Smooth (smoothstep)
            return 1.0 - smoothstep(0.0, 1.0, t);
        case 2: // Constant
            return 1.0;
        case 3: // Squared
            return 1.0 - (t * t);
        default:
            return 1.0 - t;
    }
}

void main() {
    ivec2 local_pos = ivec2(gl_GlobalInvocationID.xy);
    ivec2 pixel_pos = ivec2(pc.bounds_min_x, pc.bounds_min_y) + local_pos;
    
    if (pixel_pos.x < 0 || pixel_pos.x >= pc.region_size ||
        pixel_pos.y < 0 || pixel_pos.y >= pc.region_size) {
        return;
    }
    
    // World position of this pixel
    float world_x = float(pc.region_world_x + pixel_pos.x);
    float world_z = float(pc.region_world_z + pixel_pos.y);
    
    // Distance from brush center
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
    
    float falloff = calculate_falloff(distance, pc.brush_radius);
    float contribution = pc.height_delta * pc.strength * falloff;
    
    // Write to height delta (for undo)
    float current_delta = imageLoad(u_height_delta, pixel_pos).r;
    imageStore(u_height_delta, pixel_pos, vec4(current_delta + contribution, 0.0, 0.0, 1.0));
    
    // Write to heightmap (for real-time display)
    float current_height = imageLoad(u_height_map, pixel_pos).r;
    imageStore(u_height_map, pixel_pos, vec4(current_height + contribution, 0.0, 0.0, 1.0));
}