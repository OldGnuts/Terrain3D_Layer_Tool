// /Core/GPU/GpuCache.cs
using Godot;
using System.Collections.Generic;
using System.Diagnostics;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Manages the lifecycle and caching of shareable, stateless GPU resources
    /// like Shaders, Compute Pipelines, and Samplers. This prevents costly
    /// resource duplication and recompilation.
    ///
    /// This class MUST NOT cache stateful or instance-specific resources like
    /// textures or buffers.
    /// </summary>
    public static class GpuCache
    {
        private class CachedResource
        {
            public Rid Rid;
            public int RefCount;
        }

        private class CachedPipeline
        {
            public Rid PipelineRid;
            public Rid ShaderRid;
            public int RefCount;
        }

        // Cache for compiled shaders, keyed by their file path.
        private static readonly Dictionary<string, CachedResource> _shaderCache = new();

        // Cache for compute pipelines, keyed by the RID of their parent shader.
        private static readonly Dictionary<string, CachedPipeline> _pipelineCache = new();

        private static readonly Dictionary<(RenderingDevice.SamplerFilter, RenderingDevice.SamplerRepeatMode), CachedResource> _samplerCache = new();

        #region Shader Cache
        /// <summary>
        /// Acquires a shader from the cache or creates it if it doesn't exist. Increments its reference count.
        /// </summary>
        /// <param name="shaderPath">The "res://" path to the shader file.</param>
        /// <returns>A valid Rid for the shader, or an invalid Rid on failure.</returns>
        public static Rid AcquireShader(string shaderPath)
        {
            if (_shaderCache.TryGetValue(shaderPath, out var cached))
            {
                if (cached.Rid.IsValid)
                {
                    cached.RefCount++;
                    return cached.Rid;
                }
                _shaderCache.Remove(shaderPath);
            }

            var shaderFile = GD.Load<RDShaderFile>(shaderPath);
            if (shaderFile == null)
            {
                GD.PrintErr($"[GpuCache] Failed to load shader file: {shaderPath}");
                return new Rid();
            }

            var spirV = shaderFile.GetSpirV();
            Rid shaderRid = Gpu.CreateShaderFromSpirV(spirV);
            System.Diagnostics.Debug.Assert(shaderRid.IsValid, $"[GpuCache] Failed to create shader from SPIR-V for {shaderPath}");

            _shaderCache[shaderPath] = new CachedResource { Rid = shaderRid, RefCount = 1 };
            return shaderRid;
        }

        /// <summary>
        /// Releases a reference to a cached shader. If the reference count drops to zero, the GPU resource is freed.
        /// </summary>
        public static void ReleaseShader(string shaderPath)
        {
            if (_shaderCache.TryGetValue(shaderPath, out var cached))
            {
                cached.RefCount--;
                if (cached.RefCount <= 0)
                {
                    // A Shader's release does NOT handle pipelines. This breaks the recursive loop.
                    Gpu.FreeRid(cached.Rid);
                    _shaderCache.Remove(shaderPath);
                }
            }
        }
        #endregion

        #region Pipeline Cache
        /// <summary>
        /// Acquires a compute pipeline from the cache or creates it. Increments its reference count.
        /// </summary>
        /// <param name="shaderPath">The path of the shader for which to get a pipeline.</param>
        /// <returns>A valid Rid for the pipeline, or an invalid Rid on failure.</returns>
        public static Rid AcquirePipeline(string shaderPath)
        {
            if (string.IsNullOrEmpty(shaderPath)) return new Rid();

            // First, acquire the shader. This gives us the most up-to-date RID.
            Rid shaderRid = AcquireShader(shaderPath);
            if (!shaderRid.IsValid) return new Rid();

            // Check if a pipeline is cached for this path.
            if (_pipelineCache.TryGetValue(shaderPath, out var cached))
            {
                // If the cached pipeline is valid AND was created with the *exact same* shader RID, it's safe to reuse.
                if (cached.PipelineRid.IsValid && cached.ShaderRid == shaderRid)
                {
                    cached.RefCount++;
                    // Since we are reusing the pipeline, we don't need the new shader reference we just acquired.
                    ReleaseShader(shaderPath);
                    return cached.PipelineRid;
                }

                // If we're here, the cache is stale (shader changed). Free the old GPU resource and remove the entry.
                Gpu.FreeRid(cached.PipelineRid);
                _pipelineCache.Remove(shaderPath);
            }

            // No valid pipeline in cache; create a new one.
            Rid pipelineRid = Gpu.CreateComputePipeline(shaderRid);
            System.Diagnostics.Debug.Assert(pipelineRid.IsValid, $"[GpuCache] Failed to create compute pipeline for shader: {shaderPath}");

            // Add the new pipeline to the cache. The shader reference is already held by AcquireShader.
            _pipelineCache[shaderPath] = new CachedPipeline { PipelineRid = pipelineRid, ShaderRid = shaderRid, RefCount = 1 };
            return pipelineRid;
        }

        public static void ReleasePipeline(string shaderPath)
        {
            if (string.IsNullOrEmpty(shaderPath)) return;

            if (_pipelineCache.TryGetValue(shaderPath, out var cached))
            {
                cached.RefCount--;
                if (cached.RefCount <= 0)
                {
                    Gpu.FreeRid(cached.PipelineRid);
                    _pipelineCache.Remove(shaderPath);
                }

                // A Pipeline release MUST also release its dependency on the shader.
                ReleaseShader(shaderPath);
            }
        }
        #endregion

        #region Sampler Cache
        /// <summary>
        /// Acquires a sampler from the cache, creating it if needed. Increments its reference count.
        /// </summary>
        public static Rid AcquireSampler(RenderingDevice.SamplerFilter filter, RenderingDevice.SamplerRepeatMode repeat)
        {
            var key = (filter, repeat);
            if (_samplerCache.TryGetValue(key, out var cached) && cached.Rid.IsValid)
            {
                cached.RefCount++;
                return cached.Rid;
            }

            var samplerState = new RDSamplerState
            {
                MagFilter = filter,
                MinFilter = filter,
                MipFilter = filter,
                RepeatU = repeat,
                RepeatV = repeat,
                RepeatW = repeat
            };

            Rid samplerRid = Gpu.CreateSampler(samplerState);
            System.Diagnostics.Debug.Assert(samplerRid.IsValid, "[GpuCache] Failed to create sampler!");

            _samplerCache[key] = new CachedResource { Rid = samplerRid, RefCount = 1 };
            return samplerRid;
        }

        /// <summary>
        /// Releases a reference to a cached sampler. If the reference count drops to zero, the GPU resource is freed.
        /// </summary>
        public static void ReleaseSampler(RenderingDevice.SamplerFilter filter, RenderingDevice.SamplerRepeatMode repeat)
        {
            var key = (filter, repeat);
            if (_samplerCache.TryGetValue(key, out var cached))
            {
                cached.RefCount--;
                if (cached.RefCount <= 0)
                {
                    Gpu.FreeRid(cached.Rid);
                    _samplerCache.Remove(key);
                }
            }
        }
        #endregion

        #region Cleanup
        /// <summary>
        /// Frees all cached GPU resources. Should be called when the plugin is unloaded.
        /// </summary>
        public static void Cleanup()
        {
            foreach (var entry in _pipelineCache.Values) Gpu.FreeRid(entry.PipelineRid);
            _pipelineCache.Clear();

            foreach (var entry in _shaderCache.Values) Gpu.FreeRid(entry.Rid);
            _shaderCache.Clear();

            foreach (var entry in _samplerCache.Values) Gpu.FreeRid(entry.Rid);
            _samplerCache.Clear();

            GD.Print("[GpuCache] All cached GPU resources have been freed.");
        }
        #endregion
    }
}