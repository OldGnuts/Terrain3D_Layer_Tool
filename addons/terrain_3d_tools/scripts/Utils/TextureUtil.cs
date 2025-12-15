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
    }
}