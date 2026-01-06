// /Shaders/Layers/InstancerPlacement.glsl
#[compute]
#version 450

layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// Input textures
layout(set = 0, binding = 0) uniform sampler2D densityMask;
layout(set = 0, binding = 1) uniform sampler2D heightMap;
layout(set = 0, binding = 2) uniform sampler2D exclusionMap;

// Output buffers
// Transform buffer: 13 floats per instance (mat3x4 + mesh index)
layout(set = 0, binding = 3, std430) buffer TransformBuffer {
    float transforms[];
};

layout(set = 0, binding = 4, std430) buffer CountBuffer {
    uint instanceCount;
};

// Mesh entry buffer: 8 floats per entry
// [meshId(uint bits), cumulativeWeight, minScale, maxScale, yRotRange, alignNormal(uint bits), normalStrength, heightOffset]
layout(set = 0, binding = 5, std430) readonly buffer MeshEntryBuffer {
    float meshEntries[];
};

// Push constants
layout(push_constant) uniform Params {
    vec2 regionWorldMin;
    vec2 regionWorldSize;
    vec2 maskWorldMin;
    vec2 maskWorldSize;
    float worldHeightScale;
    float baseDensity;
    float cellSize;
    float exclusionThreshold;
    uint seed;
    uint maxInstances;
    int regionSize;
    int meshEntryCount;
    int cellsX;
    int cellsY;
};

// Constants
const float TAU = 6.28318530718;
const int FLOATS_PER_INSTANCE = 13;
const int FLOATS_PER_MESH_ENTRY = 8;

// ============================================================================
// Hash functions for deterministic randomness
// ============================================================================

uint hash(uint x) {
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

uint hash2(uvec2 v) {
    return hash(v.x ^ hash(v.y));
}

uint hash3(uvec3 v) {
    return hash(v.x ^ hash(v.y ^ hash(v.z)));
}

float randomFloat(uint seed) {
    return float(hash(seed)) / 4294967295.0;
}

vec2 randomVec2(uvec2 seed) {
    return vec2(
        randomFloat(hash2(seed)),
        randomFloat(hash2(seed + uvec2(1, 0)))
    );
}

// ============================================================================
// Normal calculation from heightmap
// ============================================================================

vec3 calculateNormal(vec2 uv) {
    float texelSize = 1.0 / float(regionSize);
    
    // Sample neighbors
    float hL = texture(heightMap, uv + vec2(-texelSize, 0)).r;
    float hR = texture(heightMap, uv + vec2(texelSize, 0)).r;
    float hD = texture(heightMap, uv + vec2(0, -texelSize)).r;
    float hU = texture(heightMap, uv + vec2(0, texelSize)).r;
    
    // Calculate gradient (scaled by world units)
    float worldTexelSize = regionWorldSize.x / float(regionSize);
    float dx = (hR - hL) * worldHeightScale / (2.0 * worldTexelSize);
    float dz = (hU - hD) * worldHeightScale / (2.0 * worldTexelSize);
    
    return normalize(vec3(-dx, 1.0, -dz));
}

// ============================================================================
// Transform building
// ============================================================================

mat3 buildRotationFromNormal(vec3 normal, float yRotation) {
    // Create orthonormal basis aligned to normal
    vec3 up = normalize(normal);
    
    // Choose a reference vector that's not parallel to up
    vec3 ref = abs(up.y) < 0.99 ? vec3(0, 1, 0) : vec3(1, 0, 0);
    
    vec3 right = normalize(cross(ref, up));
    vec3 forward = cross(up, right);
    
    // Apply Y rotation around the up axis
    float c = cos(yRotation);
    float s = sin(yRotation);
    
    vec3 rotatedRight = right * c + forward * s;
    vec3 rotatedForward = forward * c - right * s;
    
    // Return basis matrix (column vectors)
    return mat3(rotatedRight, up, rotatedForward);
}

void writeTransform(uint idx, mat3 rotation, float scale, vec3 position, uint meshIndex) {
    uint base = idx * uint(FLOATS_PER_INSTANCE);
    
    mat3 scaledRot = rotation * scale;
    
    // Column 0 (X basis) + position.x
    transforms[base + 0] = scaledRot[0][0];
    transforms[base + 1] = scaledRot[0][1];
    transforms[base + 2] = scaledRot[0][2];
    transforms[base + 3] = position.x;
    
    // Column 1 (Y basis) + position.y
    transforms[base + 4] = scaledRot[1][0];
    transforms[base + 5] = scaledRot[1][1];
    transforms[base + 6] = scaledRot[1][2];
    transforms[base + 7] = position.y;
    
    // Column 2 (Z basis) + position.z
    transforms[base + 8] = scaledRot[2][0];
    transforms[base + 9] = scaledRot[2][1];
    transforms[base + 10] = scaledRot[2][2];
    transforms[base + 11] = position.z;
    
    // Mesh index (stored as uint bits in float)
    transforms[base + 12] = uintBitsToFloat(meshIndex);
}

// ============================================================================
// Mesh entry access helpers
// ============================================================================

uint getMeshId(int entryIndex) {
    return floatBitsToUint(meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 0]);
}

float getCumulativeWeight(int entryIndex) {
    return meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 1];
}

float getMinScale(int entryIndex) {
    return meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 2];
}

float getMaxScale(int entryIndex) {
    return meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 3];
}

float getYRotRange(int entryIndex) {
    return meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 4];
}

bool getAlignToNormal(int entryIndex) {
    return floatBitsToUint(meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 5]) != 0u;
}

float getNormalStrength(int entryIndex) {
    return meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 6];
}

float getHeightOffset(int entryIndex) {
    return meshEntries[entryIndex * FLOATS_PER_MESH_ENTRY + 7];
}

// Select mesh based on random value and cumulative weights
int selectMesh(float r) {
    for (int i = 0; i < meshEntryCount; i++) {
        if (r <= getCumulativeWeight(i)) {
            return i;
        }
    }
    return meshEntryCount - 1;
}

// ============================================================================
// Main
// ============================================================================

void main() {
    ivec2 cellCoord = ivec2(gl_GlobalInvocationID.xy);
    
    // Bounds check
    if (cellCoord.x >= cellsX || cellCoord.y >= cellsY) {
        return;
    }
    
    // Calculate cell center in world space
    vec2 cellWorldPos = regionWorldMin + (vec2(cellCoord) + 0.5) * cellSize;
    
    // Convert to region UV
    vec2 regionUV = (cellWorldPos - regionWorldMin) / regionWorldSize;
    
    // Early bounds check for region
    if (regionUV.x < 0.0 || regionUV.x > 1.0 || regionUV.y < 0.0 || regionUV.y > 1.0) {
        return;
    }
    
    // Sample exclusion at cell center
    float exclusion = texture(exclusionMap, regionUV).r;
    
    // Early exit if fully excluded
    if (exclusion >= 1.0) {
        return;
    }
    
    // Convert to mask UV
    vec2 maskUV = (cellWorldPos - maskWorldMin) / maskWorldSize;
    
    // Sample density (0 if outside mask bounds)
    float density = 0.0;
    if (maskUV.x >= 0.0 && maskUV.x <= 1.0 && maskUV.y >= 0.0 && maskUV.y <= 1.0) {
        density = texture(densityMask, maskUV).r;
    }
    
    // Apply exclusion threshold with soft falloff
    float effectiveExclusion = smoothstep(exclusionThreshold * 0.5, exclusionThreshold, exclusion);
    float adjustedDensity = density * (1.0 - effectiveExclusion);
    
    // Skip if density too low
    if (adjustedDensity < 0.001) {
        return;
    }
    
    // Calculate expected instance count for this cell
    float cellArea = cellSize * cellSize;
    float expectedCount = adjustedDensity * baseDensity * cellArea;
    
    // Use deterministic random based on cell position and seed
    uvec3 rngBase = uvec3(cellCoord, seed);
    float placementRoll = randomFloat(hash3(rngBase));
    
    // Probabilistic placement: compare roll to expected count
    // For expectedCount < 1, this gives probability = expectedCount
    // For expectedCount >= 1, we could place multiple, but we limit to 1 per cell
    if (placementRoll > min(expectedCount, 1.0)) {
        return;
    }
    
    // Generate random position within cell
    vec2 offset = randomVec2(uvec2(hash3(rngBase + uvec3(1, 0, 0)), hash3(rngBase + uvec3(0, 1, 0))));
    offset = offset - 0.5; // Center around 0
    vec2 worldPos = cellWorldPos + offset * cellSize;
    
    // Clamp to region bounds
    worldPos = clamp(worldPos, regionWorldMin, regionWorldMin + regionWorldSize);
    
    // Calculate final UVs at actual position
    vec2 finalRegionUV = (worldPos - regionWorldMin) / regionWorldSize;
    vec2 finalMaskUV = (worldPos - maskWorldMin) / maskWorldSize;
    
    // Final density check at actual position
    float finalDensity = 0.0;
    if (finalMaskUV.x >= 0.0 && finalMaskUV.x <= 1.0 && finalMaskUV.y >= 0.0 && finalMaskUV.y <= 1.0) {
        finalDensity = texture(densityMask, finalMaskUV).r;
    }
    
    // Final exclusion check
    float finalExclusion = texture(exclusionMap, finalRegionUV).r;
    if (finalExclusion >= exclusionThreshold || finalDensity < 0.001) {
        return;
    }
    
    // Sample height
    float height = texture(heightMap, finalRegionUV).r * worldHeightScale;
    
    // Select mesh based on probability weights
    float meshRoll = randomFloat(hash3(rngBase + uvec3(2, 0, 0)));
    int meshIdx = selectMesh(meshRoll);
    uint meshId = getMeshId(meshIdx);
    
    // Get mesh-specific parameters
    float minScale = getMinScale(meshIdx);
    float maxScale = getMaxScale(meshIdx);
    float yRotRange = getYRotRange(meshIdx);
    bool alignNormal = getAlignToNormal(meshIdx);
    float normalStrength = getNormalStrength(meshIdx);
    float heightOffset = getHeightOffset(meshIdx);
    
    // Generate variations
    float scaleRoll = randomFloat(hash3(rngBase + uvec3(3, 0, 0)));
    float scale = mix(minScale, maxScale, scaleRoll);
    
    float rotRoll = randomFloat(hash3(rngBase + uvec3(4, 0, 0)));
    float yRotation = (rotRoll - 0.5) * yRotRange; // Centered rotation
    
    // Calculate normal if needed
    vec3 upVector = vec3(0, 1, 0);
    if (alignNormal) {
        vec3 terrainNormal = calculateNormal(finalRegionUV);
        upVector = mix(upVector, terrainNormal, normalStrength);
        upVector = normalize(upVector);
    }
    
    // Build rotation matrix
    mat3 rotation = buildRotationFromNormal(upVector, yRotation);
    
    // Final position
    vec3 position = vec3(worldPos.x, height + heightOffset, worldPos.y);
    
    // Atomic append to output buffer
    uint idx = atomicAdd(instanceCount, 1u);
    
    if (idx < maxInstances) {
        writeTransform(idx, rotation, scale, position, meshId);
    }
}