// /Core/GPU/AsyncComputeOperation.cs

using System;
using System.Collections.Generic;
using Godot;
using Terrain3DTools.Core.Debug;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// A high-level helper for building compute dispatch commands.
    /// This is a builder class and should be short-lived. It does not manage the lifecycle
    /// of the commands it creates.
    /// </summary>
    public class AsyncComputeOperation
    {
        private const string DEBUG_CLASS_NAME = "AsyncComputeOperation";
        
        #region Fields
        private readonly string _shaderPath;
        private readonly Rid _shader;
        private readonly Rid _pipeline;
        private readonly List<Rid> _temporaryRidsToFree = new();
        private readonly List<RDUniform> _uniforms = new();
        private byte[] _pushConstants;
        public string ShaderPath => _shaderPath;
        private bool _hasErrors = false;
        #endregion

        public AsyncComputeOperation(string shaderPath)
        {
            _shaderPath = shaderPath;
            _shader = GpuCache.AcquireShader(_shaderPath);
            _pipeline = GpuCache.AcquirePipeline(_shaderPath);

            // Early validation
            if (!_shader.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Failed to acquire shader: {_shaderPath}");
                _hasErrors = true;
            }

            if (!_pipeline.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Failed to acquire pipeline: {_shaderPath}");
                _hasErrors = true;
            }

            if (!_hasErrors)
            {
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations, 
                    $"Created compute operation for shader: {System.IO.Path.GetFileName(_shaderPath)}");
            }
        }

        #region Resource Binding
        public void BindStorageImage(uint binding, Rid textureRid)
        {
            if (_hasErrors) return;

            if (!textureRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot bind invalid texture RID at binding {binding} for shader '{_shaderPath}'");
                _hasErrors = true;
                return;
            }

            _uniforms.Add(GpuUtils.CreateStorageImageUniform(binding, textureRid));
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations, 
                $"Bound storage image at binding {binding}");
        }

        public void BindSamplerWithTexture(uint binding, Rid textureRid,
            RenderingDevice.SamplerFilter filter = RenderingDevice.SamplerFilter.Linear,
            RenderingDevice.SamplerRepeatMode repeat = RenderingDevice.SamplerRepeatMode.ClampToEdge)
        {
            if (_hasErrors) return;

            if (!textureRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot bind invalid texture RID at binding {binding} for shader '{_shaderPath}'");
                _hasErrors = true;
                return;
            }

            var uniform = GpuUtils.CreateSamplerWithTextureUniform(binding, textureRid, filter, repeat);
            _uniforms.Add(uniform);
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations, 
                $"Bound sampler+texture at binding {binding} (filter: {filter})");
        }

        public void BindSamplerWithTextureArray(uint binding, Rid textureArrayRid,
            RenderingDevice.SamplerFilter filter = RenderingDevice.SamplerFilter.Linear,
            RenderingDevice.SamplerRepeatMode repeat = RenderingDevice.SamplerRepeatMode.ClampToEdge)
        {
            if (_hasErrors) return;

            if (!textureArrayRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot bind invalid texture array RID at binding {binding} for shader '{_shaderPath}'");
                _hasErrors = true;
                return;
            }

            _uniforms.Add(GpuUtils.CreateSamplerWithTextureArrayUniform(binding, textureArrayRid, filter, repeat));
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations, 
                $"Bound sampler+texture array at binding {binding}");
        }

        public void BindStorageBuffer(uint binding, Rid bufferRid)
        {
            if (_hasErrors) return;

            if (!bufferRid.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot bind invalid buffer RID at binding {binding} for shader '{_shaderPath}'");
                _hasErrors = true;
                return;
            }

            _uniforms.Add(GpuUtils.CreateStorageBufferUniform(binding, bufferRid));
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations, 
                $"Bound storage buffer at binding {binding}");
        }

        public void BindTemporaryStorageBuffer(uint binding, byte[] data)
        {
            if (_hasErrors) return;

            if (data == null || data.Length == 0)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot bind null or empty buffer data at binding {binding} for shader '{_shaderPath}'");
                _hasErrors = true;
                return;
            }

            try
            {
                Rid bufferRid = Gpu.CreateStorageBuffer((uint)data.Length, data);
                if (!bufferRid.IsValid)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                        $"Failed to create storage buffer at binding {binding} for shader '{_shaderPath}'");
                    _hasErrors = true;
                    return;
                }

                _uniforms.Add(GpuUtils.CreateStorageBufferUniform(binding, bufferRid));
                _temporaryRidsToFree.Insert(0, bufferRid);
                
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"Created temporary storage buffer at binding {binding} ({data.Length} bytes)");
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Exception creating storage buffer at binding {binding}: {ex.Message}");
                _hasErrors = true;
            }
        }

        public void SetPushConstants(byte[] pushConstants)
        {
            if (_hasErrors) return;
            
            _pushConstants = pushConstants;
            
            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.ShaderOperations, 
                $"Set push constants ({pushConstants?.Length ?? 0} bytes) for shader: {System.IO.Path.GetFileName(_shaderPath)}");
        }
        #endregion

        #region Task Builder
        public Action<long> CreateDispatchCommands(uint xGroups, uint yGroups = 1, uint zGroups = 1, uint setIndex = 0)
        {
            // If we had errors during setup, return a no-op command
            if (_hasErrors)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot create dispatch commands due to earlier errors in shader: {_shaderPath}");
                return (computeList) => { }; // Return safe no-op
            }

            if (!_pipeline.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot create dispatch commands with an invalid pipeline for shader: {_shaderPath}");
                return (computeList) => { };
            }

            if (!_shader.IsValid)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Cannot create dispatch commands with an invalid shader for path: {_shaderPath}");
                return (computeList) => { };
            }

            // Validate uniforms before creating uniform set
            if (_uniforms.Count == 0)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"No uniforms bound for shader: {_shaderPath}");
                return (computeList) => { };
            }

            // Create uniform set with error handling
            Rid uniformSet = new Rid();
            try
            {
                var uniformsForSet = new Godot.Collections.Array<RDUniform>(_uniforms);
                uniformSet = Gpu.CreateUniformSet(uniformsForSet, _shader, setIndex);

                if (!uniformSet.IsValid)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                        $"FAILED to create uniform set for shader '{_shaderPath}'. Check uniform bindings match shader layout.");
                    return (computeList) => { };
                }
                
                DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuResources, 
                    $"Created uniform set with {_uniforms.Count} binding(s) for shader: {System.IO.Path.GetFileName(_shaderPath)}");
            }
            catch (Exception ex)
            {
                DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                    $"Exception creating uniform set for shader '{_shaderPath}': {ex.Message}");
                return (computeList) => { };
            }

            // Add the uniform set to our temporary RIDs for cleanup
            _temporaryRidsToFree.Insert(0, uniformSet);

            // Capture variables for the lambda (defensive copies)
            Rid pipelineForDispatch = _pipeline;
            Rid capturedUniformSet = uniformSet;
            string shaderPathForDispatch = _shaderPath;
            byte[] pushConstantsForDispatch = _pushConstants != null ? (byte[])_pushConstants.Clone() : null;

            // Reset push constants to prevent state leakage if this builder is ever reused
            _pushConstants = null;

            DebugManager.Instance?.Log(DEBUG_CLASS_NAME, DebugCategory.GpuDispatches, 
                $"Dispatch created: {xGroups}x{yGroups}x{zGroups} groups for {System.IO.Path.GetFileName(shaderPathForDispatch)}");

            // Create the dispatch command
            Action<long> gpuCommands = (computeList) =>
            {
                try
                {
                    // Validate again at dispatch time
                    if (!capturedUniformSet.IsValid)
                    {
                        DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                            $"Uniform set became invalid before dispatch for shader '{shaderPathForDispatch}'");
                        return;
                    }
                    
                    Gpu.AddDispatchToComputeList(computeList, pipelineForDispatch, capturedUniformSet, pushConstantsForDispatch, xGroups, yGroups, zGroups, setIndex);
                }
                catch (Exception ex)
                {
                    DebugManager.Instance?.LogError(DEBUG_CLASS_NAME, 
                        $"Exception during dispatch for shader '{shaderPathForDispatch}': {ex.Message}");
                }
            };

            return gpuCommands;
        }

        /// <summary>
        /// Returns the list of temporary RIDs (like uniform sets and temp buffers) that this
        /// operation created. The calling code (e.g., a pipeline) must take ownership of these
        /// and pass them to the AsyncGpuTask for eventual cleanup.
        /// </summary>
        public List<Rid> GetTemporaryRids() => new List<Rid>(_temporaryRidsToFree);

        /// <summary>
        /// Check if this operation is in a valid state
        /// </summary>
        public bool IsValid() => !_hasErrors && _pipeline.IsValid && _shader.IsValid;
        #endregion
    }
}