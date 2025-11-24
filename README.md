***

# Terrain3DTools

**A High-Performance, Non-Destructive Layer System for [Terrain3D](https://github.com/TokisanGames/Terrain3D) in Godot 4.5+ (C#).**

Terrain3DTools transforms the standard destructive terrain workflow into a flexible, object-oriented layer system. Instead of permanently painting geometry or textures, you define mountains, rivers, biomes, and paths as independent nodes. The system composites these layers in real-time using an asynchronous GPU pipeline, synchronizing the result directly to the Terrain3D storage.

## 🌟 Key Features

*   **Non-Destructive Workflow:** Treat terrain features as objects. Move a "Mountain" node, and the geometry and texturing update dynamically.
*   **Async GPU Pipeline:** All masking, simulation, and compositing occur on the GPU via Godot's `RenderingDevice`, ensuring the editor remains responsive.
*   **Control Map Architecture:** Manipulates Terrain3D's native bit-packed Control Map format (R32UI) rather than standard splatmaps, enabling complex material blending and property management.
*   **Smart Region Optimization:** Divides the world into grid regions and employs dependency tracking (`RegionDependencyManager`) to process only the specific regions affected by "dirty" layers.
*   **Cascading Dependencies:** Moving a Height Layer automatically triggers updates for dependent Texture Layers (e.g., slope-based texturing) and Feature Layers (e.g., paths that conform to new heights).
*   **Advanced Simulation:** Features a full GPU-based Hydraulic Erosion system, Thermal Weathering, and Global Flow simulation.

---

## 🏛️ System Architecture

The project is structured into three tiers: **Management**, **Pipeline**, and a low-level **GPU Backend**.

### 1. Management & Logic
*   **`TerrainLayerManager`:** The central coordinator. Uses a state machine (`Idle` → `Processing` → `ReadyToPush`) to manage the update loop.
*   **`UpdateScheduler`:** Manages editor performance by differentiating between **Interactive Updates** (low latency while dragging) and **Full Updates** (high quality on mouse release).
*   **`RegionMapManager`:** Manages the lifecycle of GPU resources for terrain chunks and handles zero-copy preview generation.

### 2. The Update Pipeline
The **`TerrainUpdateProcessor`** orchestrates a 6-phase pipeline, resolving dependencies so geometry exists before slope analysis occurs.

1.  **Height Mask Phase:** Generates influence masks for height layers.
2.  **Height Composite Phase:** Blends layers into the Region Heightmap (R32F).
3.  **Texture Mask Phase:** Generates masks for texture layers. Can utilize composite height data for topological rules (Slope/Concavity).
4.  **Texture Composite Phase:** Blends Texture IDs into the Region Control Map (R32UI).
5.  **Feature Mask Phase:** Calculates geometry/masks for complex features (Paths).
6.  **Feature Application Phase:** Applies final modifications to both height and control maps.

### 3. Data & Storage (The Control Map)
Unlike standard splatmaps, this system writes to Terrain3D's specific **Control Map** format. This is a 32-bit unsigned integer texture where every pixel packs multiple data points. The system utilizes `ControlMapUtil` for bitwise encoding/decoding:
*   **Base Texture ID:** 5 bits
*   **Overlay Texture ID:** 5 bits
*   **Blend Weight:** 8 bits
*   **UV Angle:** 4 bits
*   **UV Scale:** 3 bits
*   **Flags:** Hole, Navigation, Auto-Shader (1 bit each)

### 4. GPU Backend
The system bypasses high-level nodes for direct `RenderingDevice` control.
*   **`AsyncGpuTaskManager`:** Batches compute tasks, manages DAG dependencies (Wait/Signal), and handles `Sync()` calls.
*   **`AsyncComputeOperation`:** A builder for compute dispatches, managing Uniform Sets and strictly aligned `std430` Push Constants.
*   **`TextureUtil`:** Implements **Zero-Copy Visualization**. It creates Shared RIDs that allow Godot's `RenderingServer` to display Compute Shader outputs in the 3D viewport without expensive CPU readbacks.

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

Masks define the *shape* and *intensity* of a layer. The pipeline uses `Action<long>` delegates to inject complex command chains (Copy -> Barrier -> Dispatch) into a single compute list.

*   **Procedural:** `NoiseMask` (Perlin/Simplex/Cellular), `HeightRangeMask`.
*   **Image:** `StampMask`. Features an **Async Upload** system to stream CPU images to the GPU without editor stalls.
*   **Topological:** `SlopeMask`, `ConcavityMask`, `TerraceMask`. These analyze the composite height data to apply logic (e.g., "Texture cliffs where slope > 45°").
*   **Simulation:** `ErosionMask`. A multi-pass GPU simulation handling Global Flow, Hydraulic Erosion (sediment transport), and Thermal Weathering (talus generation).

---

## 🛠️ Editor Tools & Debugging

*   **Path Snapping:** `TerrainPath3D` and `PathTerrainSnapPlugin` allow curves to auto-snap to the non-destructive terrain height using `TerrainHeightQuery`.
*   **Inspector Previews:** `TerrainLayerInspector` runs one-shot async GPU tasks to render live mask previews inside the Inspector panel.
*   **Debug Manager:** A centralized system for:
    *   **Aggregation:** Groups high-frequency logs to prevent console spam.
    *   **Categorization:** Filters logs by subsystem (e.g., `GpuResources`, `MaskGeneration`, `TerrainPush`).
    *   **Performance:** Tracks execution timing for pipeline phases.
