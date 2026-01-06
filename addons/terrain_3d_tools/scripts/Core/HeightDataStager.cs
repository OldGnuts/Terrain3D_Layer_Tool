using System;
using System.Linq;
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Core
{
    // The struct is reordered and padded to match std430 memory layout rules.
    // Ensure std430 alignment rules are followed. GLSL ivec4 is 16 bytes.
    public struct HeightStagingMetadata
    {
        // Existing fields
        public Vector2I RegionWorldOffsetPx; // 8 bytes
        public Vector2I SourceStartPx;       // 8 bytes
        public Vector2I TargetStartPx;       // 8 bytes
        public Vector2I CopySizePx;          // 8 bytes
        public int TextureArrayIndex;        // 4 bytes

        // New neighbor indices
        public int NeighborN_Index;          // 4 bytes
        public int NeighborS_Index;          // 4 bytes
        public int NeighborW_Index;          // 4 bytes
        public int NeighborE_Index;          // 4 bytes
        public int NeighborNW_Index;         // 4 bytes
        public int NeighborNE_Index;         // 4 bytes
        public int NeighborSW_Index;         // 4 bytes
        public int NeighborSE_Index;         // 4 bytes
        private readonly int _padding0;      // 4 bytes to align to a 16-byte boundary if needed
        private readonly int _padding1;      // 4 bytes to align to a 16-byte boundary if needed
        private readonly int _padding2;      // 4 bytes to align to a 16-byte boundary if needed

    }

    /// <summary>
    /// A simple data container holding the results of a staging operation.
    /// The RIDs are owned by the GpuTask that generates them.
    /// </summary>
    public struct HeightStagingResult
    {
        public Rid HeightmapArrayRid;
        public Rid MetadataBufferRid;
        public int ActiveRegionCount;

        public bool IsValid => HeightmapArrayRid.IsValid && MetadataBufferRid.IsValid && ActiveRegionCount > 0;
    }


    public static class HeightDataStager
    {
        public static (AsyncGpuTask task, HeightStagingResult result) StageHeightDataForLayerAsync(
            TerrainLayerBase layer,
            RegionMapManager regionMapManager,
            int regionSize,
            List<AsyncGpuTask> dependencies)
        {
            var result = new HeightStagingResult();
            var regionBounds = TerrainCoordinateHelper.GetRegionBoundsForLayer(layer, regionSize);

            var (worldMin, worldMax) = layer.GetWorldBounds();

            // Find overlapping regions and their data in a single pass ---
            var regionsToStage = new Dictionary<Vector2I, (RegionData data, OverlapResult overlap)>();
            for (int x = regionBounds.Position.X; x < regionBounds.End.X; x++)
                for (int z = regionBounds.Position.Y; z < regionBounds.End.Y; z++)
                {
                    var regionCoord = new Vector2I(x, z);
                    // Use GetRegionData to avoid creating new regions here.
                    var regionData = regionMapManager.GetRegionData(regionCoord);
                    if (regionData != null)
                    {
                        var overlap = RegionMaskOverlap.GetRegionMaskOverlapFromBounds(
                            regionCoord,
                            regionSize,
                            worldMin,
                            worldMax,
                            layer.Size);
                            
                        if (overlap.HasValue)
                        {
                            regionsToStage.Add(regionCoord, (regionData, overlap.Value));
                        }
                    }
                }

            result.ActiveRegionCount = regionsToStage.Count;
            if (result.ActiveRegionCount == 0) return (null, result);

            result.HeightmapArrayRid = Gpu.CreateTexture2DArray((uint)regionSize, (uint)regionSize, (uint)result.ActiveRegionCount, RenderingDevice.DataFormat.R32Sfloat, RenderingDevice.TextureUsageBits.CanCopyToBit | RenderingDevice.TextureUsageBits.SamplingBit);

            var metadataArray = new HeightStagingMetadata[result.ActiveRegionCount];
            var coordToIndexMap = new Dictionary<Vector2I, int>();
            int sliceIndex = 0;
            foreach (var coord in regionsToStage.Keys)
            {
                coordToIndexMap[coord] = sliceIndex++;
            }

            Action<long> gpuCommands = (computeList) =>
            {
                foreach (var (coord, (data, _)) in regionsToStage)
                {
                    if (data != null && data.HeightMap.IsValid)
                    {
                        int index = coordToIndexMap[coord];
                        //GD.Print("[DEBUG] + Adding Texture to slice " + index);
                        Gpu.AddCopyTextureToArraySliceCommand(computeList, data.HeightMap, result.HeightmapArrayRid, index, (uint)regionSize, (uint)regionSize);
                    }
                }
            };

            sliceIndex = 0;
            foreach (var (coord, (_, overlap)) in regionsToStage)
            {
                int GetNeighborIndex(Vector2I offset) => coordToIndexMap.TryGetValue(coord + offset, out int index) ? index : -1;
                metadataArray[sliceIndex] = new HeightStagingMetadata
                {
                    RegionWorldOffsetPx = coord * regionSize,
                    SourceStartPx = overlap.RegionMin,
                    TargetStartPx = overlap.MaskMin,
                    CopySizePx = new Vector2I(overlap.RegionMax.X - overlap.RegionMin.X, overlap.RegionMax.Y - overlap.RegionMin.Y),
                    TextureArrayIndex = sliceIndex,
                    NeighborN_Index = GetNeighborIndex(new Vector2I(0, -1)),
                    NeighborS_Index = GetNeighborIndex(new Vector2I(0, 1)),
                    NeighborW_Index = GetNeighborIndex(new Vector2I(-1, 0)),
                    NeighborE_Index = GetNeighborIndex(new Vector2I(1, 0)),
                    NeighborNW_Index = GetNeighborIndex(new Vector2I(-1, -1)),
                    NeighborNE_Index = GetNeighborIndex(new Vector2I(1, -1)),
                    NeighborSW_Index = GetNeighborIndex(new Vector2I(-1, 1)),
                    NeighborSE_Index = GetNeighborIndex(new Vector2I(1, 1))
                };
                sliceIndex++;
            }
            result.MetadataBufferRid = Gpu.CreateStorageBuffer(metadataArray);

            // These resources are passed to another task, which will take ownership and free them when it is complete.
            var resourcesToFree = new List<Rid>();

            // This task "borrows" resources from the layer AND all of the RegionData
            // objects that it copies from.
            var owners = new List<object> { layer };
            owners.AddRange(regionsToStage.Values.Select(val => val.data));

            var task = new AsyncGpuTask(gpuCommands, null, resourcesToFree, owners, "Heightmap Stager", dependencies);

            return (task, result);
        }
    }
}