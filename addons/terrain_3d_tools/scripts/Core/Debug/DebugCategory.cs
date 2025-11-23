using System;

namespace Terrain3DTools.Core.Debug
{
    /// <summary>
    /// Categories for debug output. These are bitflags that can be combined.
    /// Each class can enable specific categories to filter what gets logged.
    /// </summary>
    [Flags]
    public enum DebugCategory : uint
    {
        None = 0,

        // System Lifecycle (Low Frequency - Always Useful)
        Initialization = 1 << 0, // Startup, connections, sub-manager setup
        Cleanup = 1 << 1, // Resource disposal, shutdown
        Validation = 1 << 2, // Connection validation, system checks

        // Update Orchestration (Medium Frequency)
        UpdateCycle = 1 << 3, // Main ProcessUpdate flow
        Scheduling = 1 << 4, // UpdateScheduler timing, interaction states

        // Layer Management (Variable Frequency)
        LayerLifecycle = 1 << 5, // Layer add/remove, collection changes (aggregated)
        LayerDirtying = 1 << 6, // Dirty propagation (aggregated count)
        LayerDetails = 1 << 7, // Individual layer names and data

        // Region Management (High Frequency)
        RegionLifecycle = 1 << 8, // Region create/remove (aggregated)
        RegionDependencies = 1 << 9, // Dependency tracking, overlap detection
        RegionDetails = 1 << 10, // Individual region coords

        // Task System (Very High Frequency)
        TaskCreation = 1 << 11, // Task creation (aggregated by type)
        TaskExecution = 1 << 12, // Task dispatch and completion (aggregated)
        TaskDependencies = 1 << 13, // Dependency resolution details

        // Pipeline Phases (High Frequency)
        PhaseExecution = 1 << 14, // Phase start/end, counts
        MaskGeneration = 1 << 15, // Mask pipeline operations (aggregated)

        // GPU Operations (Very High Frequency)
        GpuDispatches = 1 << 16, // Compute dispatches (aggregated)
        GpuSync = 1 << 17, // Sync points, barriers
        GpuResources = 1 << 18, // RID allocation/cleanup
        ShaderOperations = 1 << 19, // Shader binding, validation

        // Terrain Integration (High Frequency)
        TerrainPush = 1 << 20, // Async push operations
        TerrainSync = 1 << 21, // Region sync with Terrain3D
        TerrainCallbacks = 1 << 22, // GPU texture callbacks

        // Performance & Metrics (Variable)
        PerformanceMetrics = 1 << 23, // Counts, timings, batch sizes
        ResourceTracking = 1 << 24, // Memory allocation tracking
        PerformanceTiming = 1 << 25, // Task start/end with timing data

        // NEW: Mask-Specific Categories
        MaskSetup = 1 << 26, // Mask initialization, resource prep
        MaskPasses = 1 << 27, // Individual mask pass execution (e.g., erosion iterations)
        MaskBlending = 1 << 28, // Final blending operations

/*
        // Composite Flags (for convenience)
        AllSystem = Initialization | Cleanup | Validation,
        AllLayers = LayerLifecycle | LayerDirtying | LayerDetails,
        AllRegions = RegionLifecycle | RegionDependencies | RegionDetails,
        AllTasks = TaskCreation | TaskExecution | TaskDependencies,
        AllGpu = GpuDispatches | GpuSync | GpuResources | ShaderOperations,
        AllTerrain = TerrainPush | TerrainSync | TerrainCallbacks,
        AllPerformance = PerformanceMetrics | PerformanceTiming,
        AllMasks = MaskSetup | MaskPasses | MaskBlending,

        All = ~0u
*/
    }
}