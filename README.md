***

# Terrain3DTools (WIP) 

**A High-Performance, Non-Destructive Layer System for [Terrain3D](https://github.com/TokisanGames/Terrain3D) in Godot 4.5+ (C#).**

Terrain3DTools transforms the standard destructive terrain workflow into a flexible, object-oriented layer system. Instead of permanently painting geometry or textures, you define mountains, rivers, biomes, and paths as independent nodes. The system composites these layers in real-time using an asynchronous GPU pipeline, synchronizing the result directly to the Terrain3D storage.

## 🌟 Key Features

*   **Non-Destructive Workflow:** Treat terrain features as objects. Move a "Mountain" node, and the geometry and texturing update dynamically.
*   **Async GPU Pipeline:** All masking, simulation, and compositing occur on the GPU via Godot's `RenderingDevice`, ensuring the editor remains responsive.
*   **Control Map Architecture:** Manipulates Terrain3D's native bit-packed Control Map format (R32UI) rather than standard splatmaps, enabling complex material blending and property management per pixel.
*   **Smart Region Optimization:** Divides the world into grid regions and employs dependency tracking to process only the specific regions affected by "dirty" layers.
*   **Cascading Dependencies:** Moving a Height Layer automatically triggers updates for dependent Texture Layers (e.g., slope-based texturing) and Feature Layers (e.g., paths that conform to new heights).
*   **Advanced Simulation:** Features a full GPU-based Hydraulic Erosion system, Thermal Weathering, and Global Flow simulation.

---

## 🔄 Data Flow & Processing Lifecycle

To understand Terrain3DTools, one must follow the data as it transforms from high-level C# objects into low-level GPU commands. The system operates on a "Pull" architecture triggered by dirty states.

### 1. The Trigger (Input & Scheduling)
*   **User Action:** A user moves a `HeightLayer` or tweaks a `NoiseMask` property.
*   **Dirty Flags:** The node marks itself as `IsDirty`.
*   **The Scheduler:** The `UpdateScheduler` monitors these flags. It intelligently differentiates between **Interactive Updates** (low latency while dragging) and **Full Updates** (high quality on mouse release), prioritizing editor frame rate.

### 2. Dependency Resolution (CPU Logic)
Before touching the GPU, the system calculates the "Blast Radius" of the change:
1.  **Spatial Calculation:** `RegionDependencyManager` calculates which grid regions overlap with the dirty layer's AABB.
2.  **Cascading Logic:** `LayerDependencyManager` checks for logical dependencies. *Example:* If a Height Layer moves, any Texture Layer with a `SlopeMask` in that area is forcibly marked dirty, because the underlying geometry has changed.
3.  **Culling:** Regions with no active data are discarded to save processing time.

### 3. The Micro-Pipeline (`LayerMaskPipeline`)
This is the factory that generates the specific GPU work for a single layer. When a layer updates, this pipeline builds an atomic `AsyncGpuTask` containing a sophisticated chain of command buffers:

1.  **Clear:** A compute dispatch clears the layer's internal temporary R32F texture.
2.  **Context Stitching:** To calculate slope or erosion correctly, the layer needs to know about the terrain *outside* its bounds. The pipeline dispatches a "Stitch" shader that samples neighbor regions to build a seamless context buffer.
3.  **Mask Stacking:** It iterates through the layer's `Masks`. Each mask injects its own compute shader commands into the chain. **Barriers** are automatically injected to prevent Read-After-Write hazards.
4.  **Falloff:** A final dispatch applies the edge fade curve.

**Result:** A fully rasterized, isolated texture representing *only* that layer's influence.

### 4. The Macro-Pipeline (`TerrainUpdateProcessor`)
Once individual layer masks are generated, the Processor blends them into the final Region Data via a 6-phase pass:

1.  **Height Mask Phase:** Generates influence masks for dirty height layers.
2.  **Height Composite Phase:** Blends valid height masks into the **Region Heightmap (R32F)** using the layer's operation (Add/Subtract/Multiply).
3.  **Texture Mask Phase:** Generates masks for texture layers. Crucially, this phase can read the *result* of Phase 2 to apply topological rules (Slope/Concavity).
4.  **Texture Composite Phase:** Blends Texture IDs into the **Region Control Map (R32UI)** using bitwise operations.
5.  **Feature Mask Phase:** Calculates geometry for complex features (Paths).
6.  **Feature Application Phase:** Applies final modifications to both height and control maps.

### 5. Synchronization & Output
1.  **GPU Sync:** The `AsyncGpuTaskManager` calls `RenderingDevice.Sync()`, blocking briefly to ensure all compute operations are finished.
2.  **Zero-Copy Preview:** The `RegionMapManager` updates `RegionPreview` meshes. These use **Shared RIDs** (`TextureUtil`), allowing the viewport to display Compute Shader outputs immediately without copying data back to the CPU.
3.  **Terrain3D Push:** Finally, the `TerrainLayerManager` reads the final textures and pushes the raw byte data into Terrain3D's native storage.

---

## 🏛️ System Architecture

The project is structured into three primary tiers:

### Management
*   **`TerrainLayerManager`:** The central coordinator. Uses a state machine (`Idle` → `Processing` → `ReadyToPush`) to manage the update loop.
*   **`LayerCollectionManager`:** Monitors the SceneTree to automatically detect added, removed, or reordered layer nodes.
*   **`RegionMapManager`:** Manages the lifecycle of GPU resources (`Rid`) for terrain chunks.

### Data & Storage (The Control Map)
Unlike standard splatmaps, this system writes to Terrain3D's specific **Control Map** format. This is a 32-bit unsigned integer texture where every pixel packs multiple data points. The system utilizes `ControlMapUtil` for bitwise encoding/decoding:
*   **Base Texture ID:** 5 bits
*   **Overlay Texture ID:** 5 bits
*   **Blend Weight:** 8 bits
*   **UV Angle:** 4 bits
*   **UV Scale:** 3 bits
*   **Flags:** Hole, Navigation, Auto-Shader (1 bit each)

### GPU Backend
The system bypasses high-level nodes for direct `RenderingDevice` control.
*   **`AsyncGpuTaskManager`:** Batches compute tasks, manages DAG dependencies (Wait/Signal), and handles execution.
*   **`AsyncComputeOperation`:** A builder for compute dispatches, managing Uniform Sets and strictly aligned `std430` Push Constants.

---

## 🏔️ The Layer System

Layers are `Node3D` objects that can be placed anywhere in the scene.

### `HeightLayer`
Modifies the physical geometry.
*   **Operations:** Add, Subtract, Multiply, Overwrite.
*   **Output:** Modifies the R32F Heightmap.

### `TextureLayer`
Modifies surface materials.
*   **Logic:** Uses masks to determine where to apply specific Base and Overlay Texture IDs.
*   **Output:** Modifies the specific bits of the R32UI Control Map.

### `PathLayer` (Feature Layer)
A specialized layer wrapping a `Path3D`.
*   **Geometry:** Can raise roads (embankments) or carve rivers (trenches) using Curve3D data.
*   **Texturing:** Applies distinct Texture IDs to the path center, embankment, and transition zone.
*   **Rasterization:** Converts 3D curves into GPU-friendly segment buffers for efficient carving.

---

## 🎭 The Mask System

Masks define the *shape* and *intensity* of a layer.

*   **Procedural:** `NoiseMask` (Perlin/Simplex/Cellular), `HeightRangeMask`.
*   **Image:** `StampMask`. Features an **Async Upload** system to stream CPU images to the GPU without editor stalls.
*   **Topological:** `SlopeMask`, `ConcavityMask`, `TerraceMask`. These analyze the **Stitched Context** height data to apply logic (e.g., "Texture cliffs where slope > 45°").
*   **Simulation:** `ErosionMask`. A multi-pass GPU simulation handling Global Flow, Hydraulic Erosion (sediment transport), and Thermal Weathering (talus generation).

---

## 🛠️ Editor Tools & Debugging

*   **Path Snapping:** `TerrainPath3D` and `PathTerrainSnapPlugin` allow curves to auto-snap to the non-destructive terrain height using `TerrainHeightQuery`.
*   **Inspector Previews:** `TerrainLayerInspector` leverages the `LayerMaskPipeline` to run one-shot async GPU tasks, rendering live mask previews inside the Inspector panel.
*   **Debug Manager:** A centralized system for:
    *   **Aggregation:** Groups high-frequency logs to prevent console spam.
    *   **Categorization:** Filters logs by subsystem (e.g., `GpuResources`, `MaskGeneration`, `TerrainPush`).
    *   **Performance:** Tracks execution timing for pipeline phases.
