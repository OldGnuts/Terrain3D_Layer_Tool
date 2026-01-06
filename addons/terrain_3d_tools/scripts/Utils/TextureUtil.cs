using Godot;
using Terrain3DTools.Core;

namespace Terrain3DTools.Utils
{
    /// <summary>
    /// Creates a rid is shared from the local rendering device to the main rendering device.
    /// sourceTextureRid must originate from Gpu.Rd (Local rendering device)
    /// </summary>
    public static class TextureUtil
    {
        /// <summary>
        /// Creates a rid is shared from the local rendering device to the main rendering device.
        /// sourceTextureRid must originate from Gpu.Rd (Local rendering device)
        /// This is a low level operation and is considered *Advanced or Experimental*
        /// </summary>
        /// <param name="sourceTextureRid"> Must originate from Gpu.Rd (Local rendering device) </param>
        /// <param name="width">Image width of the sourceTextureRid</param>
        /// <param name="height">Image height of the sourceTextureRid</param>
        /// <returns></returns>
        public static Rid CreateSharedRenderingDeviceTextureRD(Rid sourceTextureRid, ulong width, ulong height)
        {
            return RenderingServer.GetRenderingDevice().TextureCreateFromExtension(
                RenderingDevice.TextureType.Type2D,
                RenderingDevice.DataFormat.R32Sfloat,
                RenderingDevice.TextureSamples.Samples1,
                RenderingDevice.TextureUsageBits.SamplingBit,
                Gpu.Rd.GetDriverResource(RenderingDevice.DriverResource.Texture, sourceTextureRid, 0),
                width,
                height,
                1,
                1);
        }

        /// <summary>
        /// Copies a GPU texture (RID) into a Godot <see cref="Image"/>.
        /// Allows easy CPU-side inspection or saving to disk.
        /// </summary>
        /// <param name="tex">The texture RID created via RenderingDevice (e.g. heightMap or mask).</param>
        /// <param name="width">Texture width in pixels.</param>
        /// <param name="height">Texture height in pixels.</param>
        /// <param name="format">Expected format (<see cref="Image.Format.Rf"/> for masks, 
        ///                      <see cref="Image.Format.Rgba8"/> for debug color, etc.).</param>
        /// <returns>A Godot image populated with the texture contents, or null on failure.</returns>
        public static Image TextureToImage(Rid tex, int width, int height, Image.Format format)
        {
            var data = Gpu.TextureGetData(tex, 0); // Byte array from GPU

            if (data == null || data.Length == 0)
            {
                GD.PrintErr($"[DebugHelper] Failed to get texture data for RID {tex.Id}. It may be invalid, empty, or have a non-copyable format.");
                return null;
            }

            var img = Image.CreateFromData(width, height, false, format, data);
            return img;
        }

        /// <summary>
        /// Convenience method: converts a mask or height texture to an <see cref="Image"/> 
        /// and writes it to the given path (PNG).
        /// </summary>
        /// <param name="maskTex">Texture RID to export.</param>
        /// <param name="regionSize">Texture size in pixels (square).</param>
        /// <param name="path">Filesystem path to store PNG. 
        /// e.g. "user://debug_mask.png".</param>
        public static void ExportMask(Rid maskTex, int regionSize, string path)
        {
            var img = TextureToImage(maskTex, regionSize, regionSize, Image.Format.Rf);

            if (img != null)
            {
                Error error = img.SavePng(path);
                if (error == Error.Ok)
                {
                    GD.Print($"[DebugHelper] Saved mask export → {path}");
                }
                else
                {
                    GD.PrintErr($"[DebugHelper] Failed to save mask to {path}. Godot Error: {error}");
                }
            }
        }
    }
}