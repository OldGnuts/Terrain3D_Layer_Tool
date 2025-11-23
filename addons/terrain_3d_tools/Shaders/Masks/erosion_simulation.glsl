#[compute]
#version 450
#extension GL_EXT_shader_atomic_float : require
layout(local_size_x = 64, local_size_y = 1, local_size_z = 1) in;

// BINDINGS (Unchanged)
layout(set = 0, binding = 0, r32f) uniform image2D heightmap;
layout(set = 0, binding = 1, r32f) uniform image2D deposition_map;
layout(set = 0, binding = 2, r32f) uniform image2D flow_map;

struct Droplet {
    vec2 pos;
    vec2 vel;
    float water;
    float sediment;
};

layout(set = 0, binding = 3, std430) buffer DropletBuffer {
    Droplet droplets[];
};

layout(push_constant) uniform PushConstants {
    float inertia;
    float erosion_strength;
    float deposition_strength;
    float evaporation_rate;
    int max_lifetime;
    int map_width;
    int map_height;
    int iteration_seed;
    float gravity; 
    float height_scale;
    float max_erosion_depth;
    int erosion_radius; 
} pc;

// Weights are pre-calculated for a 3x3 Gaussian-like blur kernel.
void distribute_material(vec2 pos, float amount, bool is_deposition) {
    ivec2 p = ivec2(pos);
    vec2 frac = pos - vec2(p);

    // Normalized weights for a 3x3 kernel (center is heaviest)
    float weights[9] = float[](
        0.075, 0.125, 0.075,
        0.125, 0.200, 0.125,
        0.075, 0.125, 0.075
    );
    float total_weight = 1.0; // The weights sum to 1.0

    if (pc.erosion_radius <= 0) { // Radius 0 means old point-based method
        imageAtomicAdd(heightmap, p + ivec2(0,0), amount * (1.0 - frac.x) * (1.0 - frac.y));
        imageAtomicAdd(heightmap, p + ivec2(1,0), amount * frac.x * (1.0 - frac.y));
        imageAtomicAdd(heightmap, p + ivec2(0,1), amount * (1.0 - frac.x) * frac.y);
        imageAtomicAdd(heightmap, p + ivec2(1,1), amount * frac.x * frac.y);
    } else { // Radius 1+ uses the kernel
        for (int y = -1; y <= 1; ++y) {
            for (int x = -1; x <= 1; ++x) {
                int index = (y + 1) * 3 + (x + 1);
                float weight = weights[index];
                ivec2 offset_p = p + ivec2(x, y);
                imageAtomicAdd(heightmap, offset_p, amount * weight);
            }
        }
    }
    
    // For the deposition mask, a simple point is fine for performance
    if (is_deposition) {
        imageAtomicAdd(deposition_map, p, amount);
    }
}

vec3 get_height_and_gradient(vec2 pos) {
    // ... (This function is correct from the previous step, no changes needed)
    ivec2 p = ivec2(pos);
    vec2 frac = pos - vec2(p);
    float h00 = imageLoad(heightmap, clamp(p + ivec2(0, 0), ivec2(0), ivec2(pc.map_width - 1, pc.map_height - 1))).r;
    float h10 = imageLoad(heightmap, clamp(p + ivec2(1, 0), ivec2(0), ivec2(pc.map_width - 1, pc.map_height - 1))).r;
    float h01 = imageLoad(heightmap, clamp(p + ivec2(0, 1), ivec2(0), ivec2(pc.map_width - 1, pc.map_height - 1))).r;
    float h11 = imageLoad(heightmap, clamp(p + ivec2(1, 1), ivec2(0), ivec2(pc.map_width - 1, pc.map_height - 1))).r;
    vec2 gradient = vec2((h10 - h00) * (1.0 - frac.y) + (h11 - h01) * frac.y,
                         (h01 - h00) * (1.0 - frac.x) + (h11 - h10) * frac.x);
    float height = mix(mix(h00, h10, frac.x), mix(h01, h11, frac.x), frac.y);
    return vec3(gradient, height);
}
vec2 hash(uvec2 seed) {
    // ... (This function is correct, no changes needed)
    seed = seed * 747796405u + 2891336453u;
    seed = uvec2(seed.x ^ (seed.x >> 16), seed.y ^ (seed.y >> 16)) * 747796405u + 2891336453u;
    seed = uvec2(seed.x ^ (seed.x >> 16), seed.y ^ (seed.y >> 16));
    return vec2(seed) / vec2(0xffffffffu);
}


void main() {
    uint id = gl_GlobalInvocationID.x;
    if (id >= droplets.length()) return;

    // --- Droplet Respawn (Unchanged) ---
    if (droplets[id].pos.x < 1.0 || droplets[id].pos.x >= pc.map_width - 2.0 || 
        droplets[id].pos.y < 1.0 || droplets[id].pos.y >= pc.map_height - 2.0 ||
        droplets[id].water <= 0.0) {
        
        uvec2 seed = uvec2(id, pc.iteration_seed);
        vec2 random_vec = hash(seed);
        droplets[id].pos = vec2(random_vec.x * pc.map_width, random_vec.y * pc.map_height);
        droplets[id].vel = vec2(0.0);
        droplets[id].water = 1.0;
        droplets[id].sediment = 0.0;
    }

    vec2 initial_pos = droplets[id].pos;
    vec3 height_data = get_height_and_gradient(initial_pos);
    vec2 gradient = height_data.xy;
    float h = height_data.z;

    // --- 1. Update Velocity (Acceleration) ---
    // CHANGED: Normalize the gradient to get a direction vector.
    // This prevents extreme acceleration on steep slopes (which causes pitting)
    // and ensures there's always some force on shallow slopes (which prevents stagnation).
    vec2 force_dir = vec2(0.0);
    if (length(gradient) > 0.0) {
        force_dir = -normalize(gradient);
    }
    droplets[id].vel = (droplets[id].vel * pc.inertia) + (force_dir * pc.gravity * (1.0 - pc.inertia));
    
    // --- 2. Update Position (Unchanged) ---
    vec2 new_pos = initial_pos + droplets[id].vel;
    
    // --- 3. Get Height Difference (Unchanged) ---
    float new_h = get_height_and_gradient(new_pos).z;
    float delta_h = new_h - h;

    // --- 4. Calculate Sediment Capacity ---
    // CHANGED: Removed the '0.01' minimum. Capacity should be 0 on flat/uphill slopes.
    float vel_len = length(droplets[id].vel);
    float sediment_capacity = max(-delta_h * pc.height_scale, 0.0) * vel_len * droplets[id].water;

    // --- 5. Erode or Deposit ---
    if (droplets[id].sediment > sediment_capacity || delta_h > 0) {
        // Deposit sediment
        float amount_to_deposit = (delta_h > 0) 
            ? min(droplets[id].sediment, delta_h * pc.height_scale)
            : (droplets[id].sediment - sediment_capacity) * pc.deposition_strength;
        
        amount_to_deposit = min(amount_to_deposit, droplets[id].sediment);
        droplets[id].sediment -= amount_to_deposit;

        // MODIFIED: Use the distribution function
        distribute_material(initial_pos, amount_to_deposit, true);

    } else {
        // Erode material
        float amount_to_erode = min((sediment_capacity - droplets[id].sediment) * pc.erosion_strength, -delta_h * pc.height_scale);
        amount_to_erode = min(amount_to_erode, pc.max_erosion_depth);
        amount_to_erode = max(0.0, amount_to_erode);
        
        droplets[id].sediment += amount_to_erode;

        // MODIFIED: Use the distribution function (note the negative sign)
        distribute_material(initial_pos, -amount_to_erode, false);
    }
    
    // --- 6. Update droplet state (Unchanged) ---
    droplets[id].pos = new_pos;
    droplets[id].water *= (1.0 - pc.evaporation_rate);
    imageAtomicAdd(flow_map, ivec2(initial_pos), droplets[id].water * 0.1);
}