// /Shaders/Brushes/exclusion_brush.glsl
#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Binding 0: Exclusion edit buffer (for undo tracking)
layout(r32f, set = 0, binding = 0) uniform image2D u_exclusion_edit;

// Binding 1: Region ExclusionMap (for real-time display)
layout(r32f, set = 0, binding = 1) uniform image2D u_exclusion_map;

layout(push_constant) uniform PushConstants {
    float brush_center_x;    // 0
    float brush_center_z;    // 4
    float brush_radius;      // 8
    float strength;          // 12
    int falloff_type;        // 16
    int bounds_min_x;        // 20
    int bounds_min_y;        // 24
    int region_world_x;      // 28
    int region_world_z;      // 32
    int region_size;         // 36
    int is_circle;           // 40
    int add_exclusion;       // 44
    int accumulate;          // 48
    // Total: 52 bytes, pad to 64
    int _pad0;               // 52
    int _pad1;               // 56
    int _pad2;               // 60
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
    
    float falloff = calculate_falloff(distance, pc.brush_radius);
    float contribution = pc.strength * falloff;
    
    float current_value = imageLoad(u_exclusion_edit, pixel_pos).r;
    float new_value;
    
    if (pc.add_exclusion == 1) {
        // Adding exclusion (moving toward 1.0)
        if (pc.accumulate == 1) {
            new_value = clamp(current_value + contribution, 0.0, 1.0);
        } else {
            new_value = max(current_value, contribution);
        }
    } else {
        // Removing exclusion (moving toward 0.0)
        if (pc.accumulate == 1) {
            new_value = clamp(current_value - contribution, 0.0, 1.0);
        } else {
            new_value = min(current_value, 1.0 - contribution);
        }
    }
    
    // Write to exclusion edit (for undo)
    imageStore(u_exclusion_edit, pixel_pos, vec4(new_value, 0.0, 0.0, 1.0));
    
    // Write to ExclusionMap (for real-time display)
    imageStore(u_exclusion_map, pixel_pos, vec4(new_value, 0.0, 0.0, 1.0));
}