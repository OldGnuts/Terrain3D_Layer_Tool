# Terrain 3D Tools - Architecture UML Documentation

## 1. System Architecture (The "Big Picture")
This diagram illustrates the static structure of the system, showing how the central manager orchestrates its subsystems and data.

```mermaid
classDiagram
    %% --- Core Manager ---
    class TerrainLayerManager {
        -UpdatePhase _currentPhase
        -TerrainProcessingContext _context
        -Terrain3DIntegration _integration
        -UpdateScheduler _scheduler
        -LayerCollectionManager _layerCollection
        -RegionMapManager _regionMapManager
        -RegionDependencyManager _regionDependencyManager
        -TerrainUpdateProcessor _processor
        +ProcessUpdate()
        -PushUpdatesToTerrain()
    }

    %% --- Logic Subsystems ---
    class UpdateScheduler {
        +double InteractionThreshold
        +bool ShouldProcessUpdate()
        +IsCurrentUpdateInteractive()
        +SignalChanges()
    }

    class LayerCollectionManager {
        -Array~TerrainLayerBase~ _layers
        +Update() 
        +GetStats()
    }

    class RegionDependencyManager {
        -Dictionary~Vector2I, TieredRegionLayers~ _map
        +Update(layers, dirtyRegions)
        +GetActiveRegionCoords()
    }

    %% --- Data Subsystems ---
    class RegionMapManager {
        -Dictionary~Vector2I, RegionData~ _data
        +GetOrCreateRegionData()
        +SetPreviewsEnabled()
    }

    class Terrain3DIntegration {
        -Terrain3D _terrain3D
        +PushRegionsToTerrain()
        +PushInstancesToTerrain()
        -PushRegionSync()
    }

    %% --- Data Models ---
    class RegionData {
        +Rid HeightMap
        +Rid ControlMap
        +Rid ColorMap
        +GetOrCreateInstanceBuffer()
    }

    class TieredRegionLayers {
        +List~HeightLayer~ HeightLayers
        +List~TextureLayer~ TextureLayers
        +ShouldProcess()
    }

    %% --- Relationships ---
    TerrainLayerManager --> UpdateScheduler : "Timing"
    TerrainLayerManager --> LayerCollectionManager : "Inventory"
    TerrainLayerManager --> RegionDependencyManager : "Spatial Query"
    TerrainLayerManager --> RegionMapManager : "Data Owner"
    TerrainLayerManager --> TerrainUpdateProcessor : "Execution"
    TerrainLayerManager --> Terrain3DIntegration : "Output"
    
    RegionMapManager *-- RegionData : "Owns"
    RegionDependencyManager o-- TieredRegionLayers : "Maps"
```

## 2. Task Creation & Execution Flow (Sequence Diagram)
This diagram demonstrates the **Just-In-Time (JIT)** allocation pattern where expensive GPU resources are only created when the task manager is ready to execute them.

```mermaid
sequenceDiagram
    participant TM as TerrainLayerManager
    participant Proc as TerrainUpdateProcessor
    participant Phase as HeightLayerMaskPhase
    participant Pipeline as LayerMaskPipeline
    participant Task as AsyncGpuTask
    participant Mgr as AsyncGpuTaskManager
    participant GPU as RenderingDevice

    Note over TM, Mgr: 1. Planning Phase (Main Thread)
    
    TM->>Proc: ProcessUpdatesAsync(Context)
    Proc->>Phase: Execute(Context)
    
    loop For Each Dirty Layer
        Phase->>Pipeline: CreateUpdateLayerTextureTask(Layer, Args)
        
        Note right of Pipeline: Creates "Lazy" Generator Closure
        Pipeline->>Task: new AsyncGpuTask(GeneratorFunc)
        activate Task
        Task-->>Pipeline: Task Instance (Pending)
        deactivate Task
        
        Pipeline-->>Phase: Task Instance
        Phase->>Mgr: AddTask(Task)
    end
    
    Phase-->>Proc: Done
    Proc-->>TM: Done

    Note over TM, Mgr: 2. Execution Phase (Per Frame)

    loop Every Frame
        Mgr->>Mgr: SubmitBatch()
        
        Mgr->>Task: Prepare() (JIT Allocation)
        activate Task
        Note right of Task: Generator runs NOW. Allocates RIDs.
        Task->>Task: Invoke GeneratorFunc
        Task-->>Mgr: Ready to Execute
        deactivate Task

        Mgr->>GPU: Dispatch Compute Shaders
        Mgr->>Task: State = InFlight
    end

    Note over Mgr, GPU: 3. Completion
    
    GPU-->>Mgr: GPU Work Finished
    Mgr->>Task: OnComplete()
    Mgr->>Mgr: QueueCleanup(Task Resources)
```

## 3. Pipeline & Phase Hierarchy
Details the `TerrainUpdateProcessor` and its specific phases.

```mermaid
classDiagram
    class TerrainUpdateProcessor {
        -List~IProcessingPhase~ _phases
        +ProcessUpdatesAsync()
        -ExecutePipeline()
    }

    class IProcessingPhase {
        <<Interface>>
        +Execute(TerrainProcessingContext)
    }

    %% --- The 10 Phases ---
    class HeightLayerMaskPhase { +Execute() }
    class RegionHeightCompositePhase { +Execute() }
    class TextureLayerMaskPhase { +Execute() }
    class RegionTextureCompositePhase { +Execute() }
    class FeatureLayerMaskPhase { +Execute() }
    class FeatureLayerApplicationPhase { +Execute() }
    class ExclusionMapWritePhase { +Execute() }
    class BlendGradientSmoothingPhase { +Execute() }
    class InstancerPlacementPhase { +Execute() }
    class SelectedLayerVisualizationPhase { +Execute() }

    %% --- Factory ---
    class LayerMaskPipeline {
        <<Static Factory>>
        +CreateUpdateLayerTextureTask()
        +CreatePathLayerMaskTaskLazy()
    }

    %% --- Relationships ---
    TerrainUpdateProcessor o-- IProcessingPhase : "Has Phases"
    IProcessingPhase <|-- HeightLayerMaskPhase
    IProcessingPhase <|-- RegionHeightCompositePhase
    IProcessingPhase <|-- TextureLayerMaskPhase
    IProcessingPhase <|-- RegionTextureCompositePhase
    IProcessingPhase <|-- FeatureLayerMaskPhase
    IProcessingPhase <|-- FeatureLayerApplicationPhase
    IProcessingPhase <|-- ExclusionMapWritePhase
    IProcessingPhase <|-- BlendGradientSmoothingPhase
    IProcessingPhase <|-- InstancerPlacementPhase
    IProcessingPhase <|-- SelectedLayerVisualizationPhase
    
    HeightLayerMaskPhase ..> LayerMaskPipeline : "Uses Factory"
    FeatureLayerMaskPhase ..> LayerMaskPipeline : "Uses Factory"
```

## 4. Layer & Mask Data System
Inheritance hierarchy for Layers and Masks, including thread-safe state snapshots.

```mermaid
classDiagram
    %% --- Layers ---
    class TerrainLayerBase {
        <<Abstract>>
        +String LayerName
        +Vector2I Size
        +Rid layerTextureRID
        +Array~TerrainMask~ Masks
        +PrepareMaskResources()
    }

    class HeightLayer {
        +HeightOperation Operation
        +float Strength
    }

    class TextureLayer {
        +TextureBlendMode BlendMode
        +bool GradientModeEnabled
        +CalculateZoneForMaskValue()
    }

    class FeatureLayer {
        <<Abstract>>
        +bool IsInstancer
        +CreateWriteExclusionCommands()
    }

    class PathLayer {
        -PathProfile _profile
        -PathBakeState _activeState
        +CaptureBakeState()
    }

    class InstancerLayer {
        -InstancerBakeState _activeState
        -Array~InstancerMeshEntry~ MeshEntries
        +CaptureBakeState()
    }

    %% --- Snapshots (Thread Safe) ---
    class PathBakeState {
        <<Snapshot>>
        +Vector3[] Points
        +Rid SdfTextureRid
    }
    
    class InstancerBakeState {
        <<Snapshot>>
        +Rid DensityMaskRid
        +List~MeshEntrySnapshot~ MeshEntries
    }

    %% --- Masks ---
    class TerrainMask {
        <<Abstract>>
        +float LayerMix
        +bool Invert
        +CreateApplyCommands()*
    }
    
    class NoiseMask
    class SlopeMask
    class ImageMask

    %% --- Relationships ---
    TerrainLayerBase <|-- HeightLayer
    TerrainLayerBase <|-- TextureLayer
    TerrainLayerBase <|-- FeatureLayer
    FeatureLayer <|-- PathLayer
    FeatureLayer <|-- InstancerLayer
    
    TerrainLayerBase o-- TerrainMask : "Has Masks"
    TerrainMask <|-- NoiseMask
    TerrainMask <|-- SlopeMask
    
    PathLayer ..> PathBakeState : "Captures"
    InstancerLayer ..> InstancerBakeState : "Captures"
```

## 5. GPU Abstraction Layer
Low-level utility classes for the RenderingDevice interface.

```mermaid
classDiagram
    class AsyncGpuTaskManager {
        <<Singleton>>
        -Queue~AsyncGpuTask~ _readyQueue
        -Queue~StaleResource~ _graveyard
        +AddTask(task)
        -SubmitBatch()
        -ProcessStaleResources()
    }

    class GpuKernels {
        <<Static Library>>
        +CreateClearCommands()
        +CreateStitchHeightmapCommands()
        +CreateFalloffCommands()
    }

    class AsyncComputeOperation {
        -Rid _shader
        -Rid _pipeline
        +BindStorageImage()
        +BindStorageBuffer()
        +SetPushConstants()
        +CreateDispatchCommands()
    }

    class GpuUtils {
        <<Static Helper>>
        +FloatArrayToBytes()
        +CreatePushConstants()
    }

    %% --- Relationships ---
    AsyncGpuTaskManager --> GpuKernels : "Uses for Cleanup"
    GpuKernels ..> AsyncComputeOperation : "Configures"
    AsyncComputeOperation ..> GpuUtils : "Uses"
```