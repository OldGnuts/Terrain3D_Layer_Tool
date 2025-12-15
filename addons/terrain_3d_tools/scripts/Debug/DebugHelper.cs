using Godot;
using Terrain3DTools.Core; 

namespace Terrain3DTools.Debug
{
    /// <summary>
    /// Utility class for exporting GPU textures (Height, ControlMap, Masks, etc.)
    /// to CPU-side <see cref="Image"/> objects for debugging and visualization.
    /// 
    /// Typical usage:
    /// <code>
    /// var img = DebugHelper.TextureToImage(myTextureRid, 512, 512, Image.Format.Rf);
    /// if (img != null) img.SavePng("user://mask.png");
    /// </code>
    /// </summary>
    public static class DebugHelper
    {
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
            // REFACTOR: Call the new, clean Gpu.TextureGetData method.
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

            // REFACTOR: Add a check to ensure the image was created successfully before saving.
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