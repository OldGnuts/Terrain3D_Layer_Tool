using Godot;

namespace Terrain3DTools.Core
{
    // This class encapsulates all the GPU resources for a single terrain region.
    public class RegionData
    {
        public Rid HeightMap { get; set; }
        public Rid ControlMap { get; set; } // For future use
        public Rid ColorMap { get; set; } // For future use

        public void FreeAll()
        {
            //GD.Print($"[DEBUG] RegionData is now FREEING HeightMap with Rid: {HeightMap.Id}");
            if (HeightMap.IsValid) Gpu.FreeRid(HeightMap);
            if (ControlMap.IsValid) Gpu.FreeRid(ControlMap);
            if (ColorMap.IsValid) Gpu.FreeRid(ColorMap);
        }
    }
}