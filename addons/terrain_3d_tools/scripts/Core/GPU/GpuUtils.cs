// /Core/GPU/GpuUtils.cs
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices; 

namespace Terrain3DTools.Core
{
    /// <summary>
    /// A static collection of stateless helper methods and classes that prepare data for Gpu.cs.
    /// This "toolbox" handles tasks like data conversion (e.g., float[] to byte[]),
    /// creating RDUniform structs, and building push constants safely.
    ///
    /// This class NEVER interacts with the RenderingDevice directly.
    /// </summary>
    public static class GpuUtils
    {
        #region Uniform Helpers
        public static RDUniform CreateStorageImageUniform(uint binding, Rid textureRid)
        {
            var uniform = new RDUniform
            {
                UniformType = RenderingDevice.UniformType.Image,
                Binding = (int)binding
            };
            uniform.AddId(textureRid);
            return uniform;
        }

        public static RDUniform CreateSamplerWithTextureUniform(uint binding, Rid textureRid, 
            RenderingDevice.SamplerFilter filter = RenderingDevice.SamplerFilter.Linear,
            RenderingDevice.SamplerRepeatMode repeat = RenderingDevice.SamplerRepeatMode.ClampToEdge)
        {
            Rid samplerRid = GpuCache.AcquireSampler(filter, repeat);
            var uniform = new RDUniform
            {
                UniformType = RenderingDevice.UniformType.SamplerWithTexture,
                Binding = (int)binding
            };
            uniform.AddId(samplerRid);
            uniform.AddId(textureRid);
            return uniform;
        }

        public static RDUniform CreateSamplerWithTextureArrayUniform(uint binding, Rid textureArrayRid,
            RenderingDevice.SamplerFilter filter = RenderingDevice.SamplerFilter.Linear,
            RenderingDevice.SamplerRepeatMode repeat = RenderingDevice.SamplerRepeatMode.ClampToEdge)
        {
            // We can reuse the same sampler logic as for regular textures.
            Rid samplerRid = GpuCache.AcquireSampler(filter, repeat);
            var uniform = new RDUniform
            {
                // The uniform type is the same, Godot's RD knows how to handle it based on the texture type.
                UniformType = RenderingDevice.UniformType.SamplerWithTexture,
                Binding = (int)binding
            };
            uniform.AddId(samplerRid);
            uniform.AddId(textureArrayRid); // The RID is for the Texture2DArray resource.
            return uniform;
        }

        public static RDUniform CreateStorageBufferUniform(uint binding, Rid bufferRid)
        {
            var uniform = new RDUniform
            {
                UniformType = RenderingDevice.UniformType.StorageBuffer,
                Binding = (int)binding
            };
            uniform.AddId(bufferRid);
            return uniform;
        }
        #endregion

        #region Data Conversion
        public static RenderingDevice.DataFormat GetRdFormatFromImageFormat(Image.Format imageFormat)
        {
            switch (imageFormat)
            {
                case Image.Format.L8: return RenderingDevice.DataFormat.R8Unorm;
                case Image.Format.La8: return RenderingDevice.DataFormat.R8G8Unorm;
                case Image.Format.Rgb8: return RenderingDevice.DataFormat.R8G8B8Unorm;
                case Image.Format.Rgba8: return RenderingDevice.DataFormat.R8G8B8A8Unorm;
                case Image.Format.Rh: return RenderingDevice.DataFormat.R16Sfloat;
                case Image.Format.Rgh: return RenderingDevice.DataFormat.R16G16Sfloat;
                case Image.Format.Rgbh: return RenderingDevice.DataFormat.R16G16B16Sfloat;
                case Image.Format.Rgbah: return RenderingDevice.DataFormat.R16G16B16A16Sfloat;
                case Image.Format.Rf: return RenderingDevice.DataFormat.R32Sfloat;
                case Image.Format.Rgf: return RenderingDevice.DataFormat.R32G32Sfloat;
                case Image.Format.Rgbf: return RenderingDevice.DataFormat.R32G32B32Sfloat;
                case Image.Format.Rgbaf: return RenderingDevice.DataFormat.R32G32B32A32Sfloat;
                default:
                    GD.PrintErr($"[GpuUtils] No direct mapping from Image.Format '{imageFormat}' to RenderingDevice.DataFormat.");
                    return RenderingDevice.DataFormat.Max;
            }
        }

        public static byte[] FloatArrayToBytes(float[] data)
        {
            var bytes = new byte[data.Length * sizeof(float)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            return bytes;
        }

        public static byte[] IntArrayToBytes(int[] data)
        {
            var bytes = new byte[data.Length * sizeof(int)];
            Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
            return bytes;
        }
        /// <summary>
        /// Converts a byte array back to a uint array.
        /// </summary>
        public static uint[] BytesToUIntArray(byte[] data)
        {
            if (data == null) return Array.Empty<uint>();
            if (data.Length % sizeof(uint) != 0)
            {
                GD.PrintErr("[GpuUtils] Byte array length is not a multiple of 4. Cannot convert to uint array.");
                return Array.Empty<uint>();
            }
            var uints = new uint[data.Length / sizeof(uint)];
            Buffer.BlockCopy(data, 0, uints, 0, data.Length);
            return uints;
        }
        
        public static byte[] StructArrayToBytes<T>(T[] data) where T : struct
        {
            int structSize = Marshal.SizeOf(typeof(T));
            byte[] byteArray = new byte[data.Length * structSize];
            
            IntPtr handle = Marshal.AllocHGlobal(structSize);
            try
            {
                for (int i = 0; i < data.Length; i++)
                {
                    Marshal.StructureToPtr(data[i], handle, true);
                    Marshal.Copy(handle, byteArray, i * structSize, structSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(handle);
            }
            
            return byteArray;
        }

        public static byte[] Vector2ArrayToBytes(Vector2[] data)
        {
            var bytes = new byte[data.Length * sizeof(float) * 2];
            for (int i = 0; i < data.Length; i++)
            {
                Buffer.BlockCopy(BitConverter.GetBytes(data[i].X), 0, bytes, i * 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(data[i].Y), 0, bytes, i * 8 + 4, 4);
            }
            return bytes;
        }

        #endregion

        #region Push Constant Builder
        /// <summary>
        /// A fluent builder for creating push constant byte arrays with proper std140/std430 alignment.
        /// It automatically inserts padding to meet alignment requirements and prints a warning
        /// when it does so. Manual padding is also logged for complete transparency.
        /// </summary>
        public sealed class PushConstantBuilder
        {
            private readonly List<byte> _data = new();
            private int _currentOffset = 0;

            private void AddWithAlignment(byte[] bytes, int alignment, string typeName)
            {
                int padding = (_currentOffset % alignment == 0) ? 0 : alignment - (_currentOffset % alignment);
                if (padding > 0)
                {
                    //GD.Print($"[PushConstantBuilder] Auto-padding added: {padding} byte(s) inserted before '{typeName}' to meet {alignment}-byte alignment at offset {_currentOffset}. " +
                    //                "Ensure your GLSL push_constant struct matches this layout.");

                    _data.AddRange(new byte[padding]);
                    _currentOffset += padding;
                }
                _data.AddRange(bytes);
                _currentOffset += bytes.Length;
            }

            public PushConstantBuilder Add(float value) { AddWithAlignment(BitConverter.GetBytes(value), 4, "float"); return this; }
            public PushConstantBuilder Add(int value) { AddWithAlignment(BitConverter.GetBytes(value), 4, "int"); return this; }
            public PushConstantBuilder Add(uint value) { AddWithAlignment(BitConverter.GetBytes(value), 4, "uint"); return this; }
            public PushConstantBuilder Add(bool value) { AddWithAlignment(BitConverter.GetBytes(value ? 1 : 0), 4, "bool (as int)"); return this; }

            public PushConstantBuilder Add(Vector2 value)
            {
                var bytes = new byte[8];
                Buffer.BlockCopy(BitConverter.GetBytes(value.X), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(value.Y), 0, bytes, 4, 4);
                AddWithAlignment(bytes, 8, "vec2");
                return this;
            }

            public PushConstantBuilder Add(Vector2I value)
            {
                var bytes = new byte[8];
                Buffer.BlockCopy(BitConverter.GetBytes(value.X), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(value.Y), 0, bytes, 4, 4);
                AddWithAlignment(bytes, 8, "ivec2");
                return this;
            }

            public PushConstantBuilder Add(Vector3 value)
            {
                var bytes = new byte[12];
                Buffer.BlockCopy(BitConverter.GetBytes(value.X), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(value.Y), 0, bytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(value.Z), 0, bytes, 8, 4);
                AddWithAlignment(bytes, 16, "vec3");
                return this;
            }

            public PushConstantBuilder Add(Color value) => Add(new Vector4(value.R, value.G, value.B, value.A));

            public PushConstantBuilder Add(Vector4 value)
            {
                var bytes = new byte[16];
                Buffer.BlockCopy(BitConverter.GetBytes(value.X), 0, bytes, 0, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(value.Y), 0, bytes, 4, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(value.Z), 0, bytes, 8, 4);
                Buffer.BlockCopy(BitConverter.GetBytes(value.W), 0, bytes, 12, 4);
                AddWithAlignment(bytes, 16, "vec4");
                return this;
            }

            /// <summary>
            /// Adds a specific number of zero bytes for explicit, manual padding.
            /// This action is logged to the console for debugging transparency.
            /// </summary>
            public PushConstantBuilder AddPadding(int byteCount)
            {
                if (byteCount > 0)
                {
                    /*
                    GD.Print($"[PushConstantBuilder] Manually adding {byteCount} byte(s) of explicit padding at offset {_currentOffset}. " +
                             "Ensure your GLSL push_constant struct mirrors this layout.");
                    */
                    _data.AddRange(new byte[byteCount]);
                    _currentOffset += byteCount;
                }
                return this;
            }

            public byte[] Build()
            {
                //GD.Print("Building push constant : " + _data.ToArray().Length);
                return _data.ToArray();
            }
            
        }

        public static PushConstantBuilder CreatePushConstants() => new();
        #endregion
    }
}