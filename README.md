# Terrain3D_Layer_Tool

Terrain3DTools: Non-Destructive Layer System for Godot 4.5+
Overview
Terrain3DTools is a high-performance, non-destructive terrain extension system designed for Terrain3D.

Standard terrain editing is destructive—painting geometry or textures permanently alters the data. Terrain3DTools introduces an object-oriented Layer System. You define mountains, rivers, paths, and biomes as independent nodes (TerrainLayerBase). These nodes can be moved, resized, and reordered in real-time.

The system is powered by a multi-threaded, asynchronous GPU compute pipeline. It composites these layers into Region Heightmaps (R32F) and Region Control Maps (R32UI), synchronizing the result to Terrain3D without freezing the editor.

Key Features
Non-Destructive Workflow: Terrain features are objects. Moving a mountain node automatically recalculates geometry and texturing for affected regions.
Async GPU Pipeline: All masking, simulation, and compositing occur on the GPU using Godot's low-level RenderingDevice API.
Control Map Architecture: Instead of traditional splatmaps, the system manipulates Terrain3D's bit-packed Control Map format, allowing for complex Base/Overlay texture blending and material properties per pixel.
Smart Region Optimization: The world is divided into grid regions. The system employs dependency tracking (RegionDependencyManager) to only process regions strictly affected by "dirty" layers.
Cascading Dependencies: Moving a Height Layer automatically triggers updates for dependent Texture Layers (e.g., slope-based texturing) and Feature Layers (e.g., height-adapting paths).
Advanced Masking: Includes procedural noise, topological analysis (Slope/Concavity), image stamping (with async upload), and hydraulic erosion.
🏛️ System Architecture
The project is structured into Management, Pipeline, and a low-level GPU Backend.

1. Management & Logic
TerrainLayerManager: The central coordinator. It uses a state machine (Idle → Processing → ReadyToPush) to manage the update loop and sync with Terrain3D.
UpdateScheduler: Manages timing to maintain editor FPS. It differentiates between "Interactive" updates (dragging a node) and "Full" updates (mouse release).
RegionMapManager: Manages the lifecycle of GPU resources for terrain chunks. It handles the allocation of Textures and generation of RegionPreview meshes.
2. The Update Pipeline
The TerrainUpdateProcessor orchestrates a 6-phase pipeline. Dependencies are resolved so geometry exists before slope analysis occurs.

Height Mask Phase: Generates influence masks for height layers.
Height Composite Phase: Blends layers into the Region Heightmap (R32F).
Texture Mask Phase: Generates masks for texture layers. Can utilize the composite height data from Phase 2 for slope/concavity rules.
Texture Composite Phase: Blends texture IDs into the Region Control Map (R32UI).
Feature Mask Phase: Calculates geometry/masks for complex features (Paths).
Feature Application Phase: Applies final modifications to both height and control maps.
3. Data & Storage (The Control Map)
Unlike standard splatmaps, this system writes to Terrain3D's specific Control Map format. This is a 32-bit unsigned integer texture where every pixel packs multiple data points.

ControlMapUtil: Handles the bitwise encoding/decoding:
Base Texture ID: 5 bits
Overlay Texture ID: 5 bits
Blend Weight: 8 bits
UV Angle: 4 bits
UV Scale: 3 bits
Flags: Hole, Navigation, Auto-Shader (1 bit each)
4. GPU Backend
The system bypasses high-level nodes for direct RenderingDevice control.

AsyncGpuTaskManager: Batches compute tasks, manages DAG dependencies (Wait/Signal), and handles Sync() calls.
AsyncComputeOperation: A builder for compute dispatches. It manages UniformSets and strictly aligned std430 Push Constants.
TextureUtil: Implements Zero-Copy Visualization. It creates Shared RIDs that allow Godot's RenderingServer to display Compute Shader outputs (Textures) in the 3D viewport without expensive CPU readbacks.
🏔️ The Layer System
HeightLayer
Modifies the physical geometry.

Operations: Add, Subtract, Multiply, Overwrite.
Output: Modifies the R32F Heightmap.
TextureLayer
Modifies the surface material.

Logic: Uses masks to determine where to apply a specific Texture ID.
Output: Modifies the Base/Overlay bits of the R32UI Control Map.
PathLayer (Feature Layer)
A complex layer wrapping a Path3D.

Geometry: Can raise roads (embankments) or carve rivers (trenches) using Curve3D data.
Texturing: Applies distinct Texture IDs to the path center, embankment, and transition zone.
Rasterization: Converts 3D curves into GPU-friendly segment buffers for efficient carving.
🎭 The Mask System
Masks define the shape and intensity of a layer. The pipeline uses Action<long> delegates to inject complex command chains (Copy -> Barrier -> Dispatch) into a single compute list.

Procedural: NoiseMask (Perlin/Simplex), HeightRangeMask.
Image: StampMask. Features an Async Upload system to stream CPU images to the GPU without editor stalls.
Topological: SlopeMask, ConcavityMask, TerraceMask. These analyze the composite height data to apply logic (e.g., "Texture cliffs where slope > 45°").
Simulation: ErosionMask. A multi-pass GPU simulation handling:
Global Flow & Carving
Hydraulic Erosion (Sediment transport)
Thermal Weathering (Talus)
🛠️ Editor Tools & Debugging
Path Snapping: TerrainPath3D and PathTerrainSnapPlugin allow curves to auto-snap to the non-destructive terrain height.
Inspector Previews: TerrainLayerInspector runs one-shot async GPU tasks to render live mask previews inside the Inspector panel.
Debug Manager: A centralized system for:
Aggregation: Groups high-frequency logs to prevent console spam.
Categorization: Filters logs by subsystem (e.g., GpuResources, MaskGeneration, TerrainPush).
Visualization: Helpers to dump GPU Control Maps to CPU images for bitwise inspection.
