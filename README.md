***

# Terrain3DTools (WIP) 

**A High-Performance, Non-Destructive Layer System for [Terrain3D](https://github.com/TokisanGames/Terrain3D) in Godot 4.5+ (C#).**

Terrain3DTools transforms the standard destructive terrain workflow into a flexible, object-oriented layer system. Instead of permanently painting geometry or textures, you define mountains, rivers, biomes, and paths as independent nodes. The system composites these layers in real-time using an semi-asynchronous GPU pipeline, synchronizing the result directly to the Terrain3D storage.

## 🌟 Key Features

*   **Non-Destructive Workflow:** Treat terrain features as objects. Move a "Mountain" node, and the geometry and texturing update dynamically.
*   **GPU Pipeline:** All masking, simulation, and compositing occur on the GPU via Godot's `RenderingDevice`, ensuring the editor remains responsive.
*   **Control Map Architecture:** Manipulates Terrain3D's native bit-packed Control Map format (R32UI) rather than standard splatmaps, enabling complex material blending and property management per pixel.
*   **Smart Region Optimization:** Divides the world into grid regions and employs dependency tracking to process only the specific regions affected by "dirty" layers.
*   **Cascading Dependencies:** Moving a Height Layer automatically triggers updates for dependent Texture Layers (e.g., slope-based texturing) and Feature Layers (e.g., paths that conform to new heights).
*   **Advanced Simulation:** Features a full GPU-based Hydraulic Erosion system, Thermal Weathering, and Global Flow simulation.

---

## 🔄 Data Flow & Processing Lifecycle

To understand Terrain3DTools, one must follow the data as it transforms from high-level C# objects into low-level GPU commands. The system operates on a "Pull" architecture triggered by dirty states. There may be confusion about async operations here. In fact the nature of the architecture is synchronous, however some operations are ran asynchrounsly. Internally a DAG is built, and that dictates how synchronous the flow of data is. It is more common when looking at profile that the system run synchronously, where batching is executed across many frames to reduce editor lag. This is a point of discussion, it will become obvious to anyone who understands the system.

### 1. The Trigger (Input & Scheduling)
*   **User Action:** A user moves a `HeightLayer` or tweaks a `Mask` property.
*   **Dirty Flags:** The node marks itself as `IsDirty`.
*   **The Scheduler:** The `UpdateScheduler` monitors these flags. It intelligently differentiates between **Interactive Updates** (low latency while dragging) and **Full Updates** (high quality on mouse release), prioritizing editor frame rate.

### 2. Dependency Resolution (CPU Logic)
Before touching the GPU, the system calculates the "Blast Radius" of the change:
1.  **Spatial Calculation:** `RegionDependencyManager` calculates which grid regions overlap with the dirty layer's AABB.
2.  **Cascading Logic:** `LayerDependencyManager` checks for logical dependencies. *Example:* If a Height Layer moves, any Texture Layer with a `SlopeMask` in that area is forcibly marked dirty, because the underlying geometry has changed.
3.  **Culling:** Regions with no active data are discarded to save processing time.

### 3. The Micro-Pipeline (`LayerMaskPipeline`)
This is the factory that generates the specific GPU work for a single layer. If a layer is simply moved, it will not regenerate its mask chain unless the layer requires height data. IE: No mask exists in the mask chain that requires height data. Commonly texture layers require height data and they will always need regenation when moved. The dependency system determines when masks should be regenerated, regions will always need regeneration when a layer is moved or a layer mask is changed. When a layer updates, this pipeline builds an atomic `AsyncGpuTask` containing a sophisticated chain of command buffers:

1.  **Clear:** A compute dispatch clears the layer's internal temporary R32F texture.
2.  **Context Stitching:** To calculate slope or erosion correctly, the layer needs to know about the terrain *outside* its bounds. The pipeline dispatches a "Stitch" shader that samples neighbor regions to build a seamless context buffer. If the layer does not need new height data, it will still stictch in order to present a visualization when required. This can be optimized for when a layer is selected in the editor, however it does not seem necessay.
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

***

# 🕸️ The Dependency & Propagation System

One of the most complex challenges in a non-destructive terrain system is determining **what** needs to update when a user modifies a layer. Rebuilding the entire terrain for every small change would be prohibitively slow.

Terrain3DTools uses a **Three-Tier Dependency System** to ensure minimal processing while maintaining data consistency.

## 1. Spatial Dependencies ("The Where")
**Managed by:** `RegionDependencyManager`

Todo: The system does not take into account the the vertex spacing. It does use the region size, however we have not implemented changing the underlying Terrain3D terrain specification. ...
The terrain is divided into a grid of 256x256 regions. The first step is determining which of these regions are affected by a change.

*   **AABB Intersection:** When a layer moves or resizes, the system calculates its Axis-Aligned Bounding Box (AABB) in world space.
*   **Grid Mapping:** It overlays this AABB onto the region grid to generate a list of **Dirty Regions**.
*   **Culling:** Regions that contain no active layers (or layers with 0 opacity) are flagged as "Inactive" and skipped by the GPU entirely.
*   **Boundary Cleaning:** If a layer moves *out* of a region, that region is marked dirty one last time so the system can run a "Clear" operation to remove the layer's old data.

## 2. Causal Dependencies ("The What")
**Managed by:** `LayerDependencyManager`

Just because we know *where* a change happened doesn't mean we know *who* is affected. Layers often rely on data generated by other layers. The system implements a strict logical propagation chain:

*   **The Hierarchy of Truth:**
    1.  **Height Layers** (Geometry)
    2.  **Texture Layers** (Material)
    3.  **Feature Layers** (Paths/Objects)

*   **Propagation Rules:**
    *   **Height → Texture:** If a Height Layer changes, the underlying geometry changes. Texture layers that use `SlopeMask`, `ConcavityMask`, or `TerraceMask` depend on this geometry. Therefore, if a Height Layer is marked dirty, overlapping Texture Layers are *automatically* marked dirty.
    *   **Height/Texture → Features:** Feature layers (like Paths) often conform to the terrain height or interact with biomes. If either Height or Texture layers change, overlapping Feature layers are marked dirty.
    *   **Priority Sorting:** Within Feature Layers, a `ProcessingPriority` int determines order. Higher priority paths (e.g., a Highway) will force lower priority paths (e.g., a Dirt Trail) to update if they overlap.

## 3. Execution Dependencies ("The When")
**Managed by:** `AsyncGpuTaskManager` & `TerrainUpdateProcessor`

Once the system knows *what* to update, it builds a Directed Acyclic Graph (DAG) of GPU tasks. A task cannot start until its dependencies are resolved. This is why the architecture cannot be fully async. Because it is non-destructive, it is synchrounous in nature. Perhaps a talking point is renaming the ASYNC classes to exclude the name ASYNC. Because we have all said that in discussions.

*   **Wait/Signal Architecture:** Every `AsyncGpuTask` has a list of dependencies. The Task Manager holds a task in a "Pending" state until all its dependencies report `Completed`.
*   **The Processing Chain:**
    1.  **Height Composite Task:** Runs first.
    2.  **Texture Mask Task:** Waits for the Height Composite to finish. Why? Because the `SlopeMask` shader needs to sample the *result* of the Height Composite to calculate steepness.
    3.  **Texture Composite Task:** Waits for the Texture Mask to finish.
    4.  **Feature Mask Task:** Waits for both Height and Texture composites.

## 📉 Example Scenario: Moving a Mountain

To illustrate, here is what happens when you drag a **Height Layer** node that sits underneath a **Texture Layer** (configured with a Slope Mask):

1.  **Input:** User drags the Height Layer.
2.  **Spatial:** `RegionDependencyManager` identifies that regions `(0,0)` and `(1,0)` are under the mountain.
3.  **Causal:** `LayerDependencyManager` sees a Texture Layer in those same regions. It knows the Texture Layer uses a `SlopeMask`. It marks the Texture Layer as dirty because the mountain's movement changes the slopes.
4.  **Graph Building:**
    *   **Task A:** Generate Height Mask for Mountain.
    *   **Task B:** Composite Region Height (Depends on A).
    *   **Task C:** Generate Texture Mask (Depends on B). *The shader reads Task B's output to find the new slopes.*
    *   **Task D:** Composite Region Control Map (Depends on C).
5.  **Execution:** The GPU executes A → B. Once B is done, C unlocks and executes, followed by D.
6.  **Result:** The mountain moves, and the rock texture dynamically repaints itself to stay on the steep sides of the moving mountain.
