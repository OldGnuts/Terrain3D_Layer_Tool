// /Core/GPU/GpuKernels.cs 
using System;
using System.Collections.Generic;
using Godot;

namespace Terrain3DTools.Core
{
    /// <summary>
    // A static library of high-level, reusable GPU compute operations (kernels).
    /// </summary>
    public static class GpuKernels
    {
        /// <summary>
        /// Creates the commands to clear a texture to a specific color.
        /// </summary>
        public static (Action<long> commands, List<Rid> tempRids, List<string>) CreateClearCommands(Rid targetTexture, Color clearColor, int width, int height)
        {
            if (!targetTexture.IsValid) return (null, new List<Rid>(), new List<string> { "" });
            var shaderPath = "res://addons/terrain_3d_tools/Shaders/Utils/ClearRegion.glsl";
            var op = new AsyncComputeOperation(shaderPath);

            op.BindStorageImage(0, targetTexture);

            var pushConstants = GpuUtils.CreatePushConstants()
                .Add(clearColor)
                .Build();
            op.SetPushConstants(pushConstants);

            uint groupsX = (uint)((width + 7) / 8);
            uint groupsY = (uint)((height + 7) / 8);

            return (op.CreateDispatchCommands(groupsX, groupsY), op.GetTemporaryRids(), new List<string> { shaderPath });
        }
    }
}