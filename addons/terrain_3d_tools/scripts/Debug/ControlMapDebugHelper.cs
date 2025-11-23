using Godot;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Debug
{
    /// <summary>
    /// Provides visualization of ControlMap bitfields.
    /// Uses ControlMapUtil to decode pixel fields and map values to colors.
    /// </summary>
    public static class ControlMapDebugHelper
    {
        /// <summary>
        /// Converts a GPU ControlMap (R32UI) into a debug image.
        /// Returns null if the GPU data cannot be retrieved.
        /// </summary>
        public static Image ExportDebugView(Rid controlMap, int regionSize, string mode = "base")
        {
            // REFACTOR: Use the new Gpu class to get data and GpuUtils to convert it.
            var data = Gpu.TextureGetData(controlMap, 0); // raw byte array
            if (data == null || data.Length == 0)
            {
                GD.PrintErr($"[ControlMapDebugHelper] Failed to retrieve data from control map RID {controlMap.Id}.");
                return null;
            }

            var pixels = GpuUtils.BytesToUIntArray(data);
            if (pixels.Length == 0)
            {
                GD.PrintErr("[ControlMapDebugHelper] Failed to convert byte data to uint array.");
                return null;
            }

            var img = Image.CreateEmpty(regionSize, regionSize, false, Image.Format.Rgba8);

            for (int y = 0; y < regionSize; y++)
            {
                for (int x = 0; x < regionSize; x++)
                {
                    int idx = y * regionSize + x;
                    if (idx >= pixels.Length) continue;
                    
                    uint packed = pixels[idx];

                    ControlMapUtil.Decode(packed,
                        out uint baseId, out uint overlayId, out uint blend,
                        out uint angle, out uint scale, out uint hole, out uint nav, out uint autoFlag);

                    Color c = Colors.Black;

                    switch (mode.ToLower())
                    {
                        case "base":
                            c = new Color(baseId / 31.0f, baseId / 31.0f, baseId / 31.0f);
                            break;
                        case "overlay":
                            c = new Color(overlayId / 31.0f, overlayId / 31.0f, overlayId / 31.0f);
                            break;
                        case "blend":
                            c = new Color(blend / 255.0f, blend / 255.0f, blend / 255.0f);
                            break;
                        case "flags":
                            c = new Color(hole > 0 ? 1 : 0, nav > 0 ? 1 : 0, autoFlag > 0 ? 1 : 0);
                            break;
                    }

                    img.SetPixel(x, y, c);
                }
            }
            return img;
        }
    }
}
