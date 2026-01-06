// /Core/Pipeline/IProcessingPhase.cs
using Godot;
using System.Collections.Generic;
using Terrain3DTools.Core;

namespace Terrain3DTools.Pipeline
{
    /// <summary>
    /// Represents a phase in the terrain update pipeline.
    /// Each phase processes a specific type of update and returns the tasks it created.
    /// </summary>
    public interface IProcessingPhase
    {
        /// <summary>
        /// Executes this phase of processing.
        /// </summary>
        /// <param name="context">Shared context containing all data needed for processing</param>
        /// <returns>Dictionary mapping identifiers to the async tasks created by this phase</returns>
        Dictionary<object, AsyncGpuTask> Execute(TerrainProcessingContext context);
    }
}