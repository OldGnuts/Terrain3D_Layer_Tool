// /Core/LayerDependencyManager.cs
using Godot;
using Godot.Collections;
using System.Collections.Generic;
using System.Linq;
using Terrain3DTools.Layers;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Core
{
    /// <summary>
    /// Enhanced dependency manager that handles feature layer dependencies.
    /// Feature layers process after height and texture layers and can depend on both.
    /// </summary>
    public static class LayerDependencyManager
    {
        /// <summary>
        /// Enhanced dependency rules that include feature layer interactions.
        /// </summary>
        public static bool DoesLayerAffect(TerrainLayerBase source, TerrainLayerBase target)
        {
            // Height layers affect texture layers (existing rule)
            if (source.GetLayerType() == LayerType.Height && target.GetLayerType() == LayerType.Texture)
                return true;

            // Height layers affect feature layers that modify height
            if (source.GetLayerType() == LayerType.Height && target.GetLayerType() == LayerType.Feature)
            {
                var featureTarget = target as FeatureLayer;
                return featureTarget?.ModifiesHeight == true;
            }

            // Texture layers affect feature layers that modify texture
            if (source.GetLayerType() == LayerType.Texture && target.GetLayerType() == LayerType.Feature)
            {
                var featureTarget = target as FeatureLayer;
                return featureTarget?.ModifiesTexture == true;
            }

            // Feature layers can affect other feature layers based on processing priority
            if (source.GetLayerType() == LayerType.Feature && target.GetLayerType() == LayerType.Feature)
            {
                var featureSource = source as FeatureLayer;
                var featureTarget = target as FeatureLayer;
                
                if (featureSource == null || featureTarget == null) return false;
                
                // Higher priority processes first and can affect lower priority
                return featureSource.ProcessingPriority > featureTarget.ProcessingPriority;
            }

            return false;
        }

        /// <summary>
        /// Enhanced dirty state propagation with feature layer support.
        /// Processing order: Height → Texture → Feature (by priority)
        /// </summary>
        public static HashSet<TerrainLayerBase> PropagateDirtyState(Array<TerrainLayerBase> terrainLayers)
        {
            var allDirtyLayers = new HashSet<TerrainLayerBase>();

            // Separate layers by type for ordered processing
            var heightLayers = terrainLayers.Where(l => l?.GetLayerType() == LayerType.Height).ToList();
            var textureLayers = terrainLayers.Where(l => l?.GetLayerType() == LayerType.Texture).ToList();
            var featureLayers = terrainLayers.Where(l => l?.GetLayerType() == LayerType.Feature)
                                           .Cast<FeatureLayer>()
                                           .OrderByDescending(f => f.ProcessingPriority)
                                           .Cast<TerrainLayerBase>()
                                           .ToList();

            // Process in dependency order: Height → Texture → Feature
            var processingOrder = new List<TerrainLayerBase>();
            processingOrder.AddRange(heightLayers);
            processingOrder.AddRange(textureLayers);
            processingOrder.AddRange(featureLayers);

            // Propagate dirty state through the processing chain
            for (int i = 0; i < processingOrder.Count; i++)
            {
                var src = processingOrder[i];
                if (src == null) continue;

                // A layer is a source for propagation if it was dirty initially OR made dirty by a previous layer
                bool isSourceDirty = src.IsDirty || allDirtyLayers.Contains(src);

                if (!isSourceDirty) continue;

                // Ensure the dirty source is in our final set
                if (allDirtyLayers.Add(src))
                {
                    //GD.Print($"[LayerDependencyManager] Processing dirty layer: {src.LayerName}");
                }

                // Check all subsequent layers for dependencies
                for (int j = i + 1; j < processingOrder.Count; j++)
                {
                    var tgt = processingOrder[j];
                    if (tgt == null) continue;

                    // Check if the target is affected and layers overlap
                    if (TerrainCoordinateHelper.LayersOverlap(src, tgt) && DoesLayerAffect(src, tgt))
                    {
                        if (allDirtyLayers.Add(tgt))
                        {
                            //GD.Print($"[LayerDependencyManager] {src.LayerName} affects {tgt.LayerName}, propagating dirty state.");
                        }
                    }
                }
            }

            return allDirtyLayers;
        }

        /// <summary>
        /// Enhanced movement-based dirty propagation with feature layer support.
        /// </summary>
        public static void PropagateDirtyStateFromMovement(
            HashSet<TerrainLayerBase> positionDirtyLayers,
            IEnumerable<TerrainLayerBase> allLayers,
            HashSet<TerrainLayerBase> maskDirtyLayers)
        {
            // Height layer movement affects texture layers (existing logic)
            var movedHeightLayers = positionDirtyLayers.Where(l => l.GetLayerType() == LayerType.Height).ToList();
            var allTextureLayers = allLayers.Where(l => l.GetLayerType() == LayerType.Texture).ToList();

            if (movedHeightLayers.Any())
            {
                foreach (var movedHeightLayer in movedHeightLayers)
                {
                    foreach (var textureLayer in allTextureLayers)
                    {
                        if (!maskDirtyLayers.Contains(textureLayer))
                        {
                            if (LayerOverlap.DoLayersOverlap(movedHeightLayer, textureLayer))
                            {
                                if (textureLayer.DoesAnyMaskRequireHeightData())
                                {
                                    textureLayer.ForceDirty();
                                    maskDirtyLayers.Add(textureLayer);
                                    //GD.Print($"'{textureLayer.LayerName}' marked dirty because overlapping height layer '{movedHeightLayer.LayerName}' moved.");
                                }
                            }
                        }
                    }
                }
            }

            // NEW: Handle feature layer movement propagation
            var movedFeatureLayers = positionDirtyLayers.Where(l => l.GetLayerType() == LayerType.Feature).Cast<FeatureLayer>().ToList();
            var allFeatureLayers = allLayers.Where(l => l.GetLayerType() == LayerType.Feature).Cast<FeatureLayer>().ToList();

            foreach (var movedFeatureLayer in movedFeatureLayers)
            {
                // Feature layer movement affects height-dependent texture layers
                if (movedFeatureLayer.ModifiesHeight)
                {
                    foreach (var textureLayer in allTextureLayers)
                    {
                        if (!maskDirtyLayers.Contains(textureLayer))
                        {
                            if (LayerOverlap.DoLayersOverlap(movedFeatureLayer, textureLayer))
                            {
                                if (textureLayer.DoesAnyMaskRequireHeightData())
                                {
                                    textureLayer.ForceDirty();
                                    maskDirtyLayers.Add(textureLayer);
                                    //GD.Print($"'{textureLayer.LayerName}' marked dirty because overlapping feature layer '{movedFeatureLayer.LayerName}' moved and modifies height.");
                                }
                            }
                        }
                    }
                }

                // Feature layer movement affects other lower-priority feature layers
                foreach (var otherFeatureLayer in allFeatureLayers)
                {
                    if (otherFeatureLayer != movedFeatureLayer && 
                        !maskDirtyLayers.Contains(otherFeatureLayer) &&
                        movedFeatureLayer.ProcessingPriority > otherFeatureLayer.ProcessingPriority)
                    {
                        if (LayerOverlap.DoLayersOverlap(movedFeatureLayer, otherFeatureLayer))
                        {
                            // Check if the moved feature layer affects the other feature layer
                            bool affects = false;
                            
                            if (movedFeatureLayer.ModifiesHeight && otherFeatureLayer.ModifiesHeight)
                                affects = true;
                            if (movedFeatureLayer.ModifiesTexture && otherFeatureLayer.ModifiesTexture)
                                affects = true;

                            if (affects)
                            {
                                otherFeatureLayer.ForceDirty();
                                maskDirtyLayers.Add(otherFeatureLayer);
                                //GD.Print($"'{otherFeatureLayer.LayerName}' marked dirty because higher-priority feature layer '{movedFeatureLayer.LayerName}' moved.");
                            }
                        }
                    }
                }
            }

            // Handle height/texture layer movement affecting feature layers
            var allMovedBaseLayers = positionDirtyLayers.Where(l => 
                l.GetLayerType() == LayerType.Height || l.GetLayerType() == LayerType.Texture).ToList();

            foreach (var movedBaseLayer in allMovedBaseLayers)
            {
                foreach (var featureLayer in allFeatureLayers)
                {
                    if (!maskDirtyLayers.Contains(featureLayer))
                    {
                        bool shouldMarkDirty = false;

                        // Height layer movement affects height-dependent feature layers
                        if (movedBaseLayer.GetLayerType() == LayerType.Height && featureLayer.ModifiesHeight)
                            shouldMarkDirty = true;

                        // Texture layer movement affects texture-dependent feature layers  
                        if (movedBaseLayer.GetLayerType() == LayerType.Texture && featureLayer.ModifiesTexture)
                            shouldMarkDirty = true;

                        if (shouldMarkDirty && LayerOverlap.DoLayersOverlap(movedBaseLayer, featureLayer))
                        {
                            featureLayer.ForceDirty();
                            maskDirtyLayers.Add(featureLayer);
                            //GD.Print($"'{featureLayer.LayerName}' marked dirty because overlapping {movedBaseLayer.GetLayerType().ToString().ToLower()} layer '{movedBaseLayer.LayerName}' moved.");
                        }
                    }
                }
            }
        }
    }

}
