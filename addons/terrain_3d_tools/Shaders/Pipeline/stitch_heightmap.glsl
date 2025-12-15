#[compute]
#version 450
#extension GL_EXT_nonuniform_qualifier : require
layout(local_size_x = 8, local_size_y = 8, local_size_z = 1) in;

// BINDINGS
// Input: The complex, multi-region texture array.
layout(set = 0, binding = 0) uniform sampler2DArray heightmap_array;

// This struct MUST match the C# version exactly.
struct HeightStagingMetadata {
    ivec2 region_world_offset_px;
    ivec2 source_start_px;
    ivec2 target_start_px;
    ivec2 copy_size_px;
    int texture_array_index;
    int neighborN_index;
    int neighborS_index;
    int neighborW_index;
    int neighborE_index;
    int neighborNW_index;
    int neighborNE_index;
    int neighborSW_index;
    int neighborSE_index;
    int padding0;
    int padding1;
    int padding2;
};

// Input: The metadata to navigate the array.
layout(set = 0, binding = 1, std430) buffer MetadataBuffer {
   HeightStagingMetadata metadata[];
};
// Output: The simple, seamless, stitched 2D texture.
layout(set = 0, binding = 2, r32f) uniform image2D stitched_heightmap_output;



// The robust sampling function from the working slope mask.
float sample_height_stitched(ivec2 pixel_coord, int current_slice, ivec2 tex_size, HeightStagingMetadata meta) {
    int sample_slice = current_slice;

    if (pixel_coord.x < 0) {
        if (pixel_coord.y < 0)            { sample_slice = meta.neighborNW_index; }
        else if (pixel_coord.y >= tex_size.y) { sample_slice = meta.neighborSW_index; }
        else                              { sample_slice = meta.neighborW_index;  }
    } else if (pixel_coord.x >= tex_size.x) {
        if (pixel_coord.y < 0)            { sample_slice = meta.neighborNE_index; }
        else if (pixel_coord.y >= tex_size.y) { sample_slice = meta.neighborSE_index; }
        else                              { sample_slice = meta.neighborE_index;  }
    } else if (pixel_coord.y < 0) {
        sample_slice = meta.neighborN_index;
    } else if (pixel_coord.y >= tex_size.y) {
        sample_slice = meta.neighborS_index;
    }

    ivec2 sample_coord = pixel_coord;

    if (sample_slice == -1) {
        sample_slice = current_slice;
        sample_coord = clamp(pixel_coord, ivec2(0), tex_size - 1);
    } else {
        if (sample_coord.x < 0)              { sample_coord.x += tex_size.x; }
        else if (sample_coord.x >= tex_size.x) { sample_coord.x -= tex_size.x; }
        if (sample_coord.y < 0)              { sample_coord.y += tex_size.y; }
        else if (sample_coord.y >= tex_size.y) { sample_coord.y -= tex_size.y; }
    }
    
    vec2 uv = (vec2(sample_coord) + 0.5) / vec2(tex_size);
    return texture(heightmap_array, vec3(uv, nonuniformEXT(sample_slice))).r;
}

void main() {
    HeightStagingMetadata current_meta = metadata[gl_WorkGroupID.z];
    ivec2 mask_pixel_coord = ivec2(gl_GlobalInvocationID.xy);

    // Boundary check ensures each thread only works on its assigned region.
    ivec2 max_coord = current_meta.target_start_px + current_meta.copy_size_px;
    if (mask_pixel_coord.x < current_meta.target_start_px.x || mask_pixel_coord.x >= max_coord.x ||
        mask_pixel_coord.y < current_meta.target_start_px.y || mask_pixel_coord.y >= max_coord.y) {
        return;
    }

    ivec2 offset_in_region = mask_pixel_coord - current_meta.target_start_px;
    ivec2 source_pixel = current_meta.source_start_px + offset_in_region;
    
    ivec2 source_size = textureSize(heightmap_array, 0).xy;
    int slice_index = nonuniformEXT(current_meta.texture_array_index);

    // Sample the height using the stitching logic...
    float height = sample_height_stitched(source_pixel, slice_index, source_size, current_meta);

    // ...and write it to the output texture.
    imageStore(stitched_heightmap_output, mask_pixel_coord, vec4(height));
}