// /Layers/FeatureLayer.cs
using Godot;
using System;
using System.Collections.Generic;
using Terrain3DTools.Core;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Layers
{
    /// <summary>
    /// Base class for feature layers that process after height and texture layers.
    /// Feature layers can modify both height and texture data and typically represent
    /// complex environmental features like paths, rivers, structures, etc.
    /// </summary>
    [GlobalClass, Tool]
    public abstract partial class FeatureLayer : TerrainLayerBase
    {
        protected float _worldHeightScale;
        public void SetWorldHeightScale(float value)
        {
            _worldHeightScale = value;
        }

        #region Properties


        [Export(PropertyHint.Range, "0.0,2.0,0.1")]
        public float HeightInfluence { get; set; } = 1.0f;

        [Export(PropertyHint.Range, "0.0,2.0,0.1")] 
        public float TextureInfluence { get; set; } = 1.0f;

        [Export]
        public int ProcessingPriority { get; set; } = 0;
        #endregion

        public override LayerType GetLayerType() => LayerType.Feature;

        public override void _Ready()
        {
            base._Ready();
            if (string.IsNullOrEmpty(LayerName) || LayerName.StartsWith("New Layer"))
                LayerName = "Feature Layer " + IdGenerator.GenerateShortUid();
        }

        /// <summary>
        /// Feature layers can require both height and texture data since they process last
        /// </summary>
        public override void MarkPositionDirty()
        {
            // Feature layers always force a full dirty state when moved since they can affect both height and texture
            ForceDirty();
            base.MarkPositionDirty();
        }

        /// <summary>
        /// Creates GPU commands to apply this feature layer's height modifications to a region.
        /// Override this if the feature modifies terrain height.
        /// </summary>
        public virtual (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyHeightCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            return (null, new List<Rid>(), new List<string>());
        }

        /// <summary>
        /// Creates GPU commands to apply this feature layer's texture modifications to a region.
        /// Override this if the feature modifies terrain textures.
        /// </summary>
        public virtual (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyTextureCommands(
            Vector2I regionCoords,
            RegionData regionData, 
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            return (null, new List<Rid>(), new List<string>());
        }

        /// <summary>
        /// Default implementation delegates to height or texture commands based on what the feature modifies.
        /// Most feature layers will want to override the specific CreateApplyHeightCommands or CreateApplyTextureCommands instead.
        /// </summary>
        public override (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyRegionCommands(
            Vector2I regionCoords,
            RegionData regionData,
            int regionSize,
            Vector2 regionMinWorld,
            Vector2 regionSizeWorld)
        {
            // This is a fallback implementation - most feature layers should override the specific methods
            if (ModifiesHeight && !ModifiesTexture)
            {
                return CreateApplyHeightCommands(regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);
            }
            else if (ModifiesTexture && !ModifiesHeight)
            {
                return CreateApplyTextureCommands(regionCoords, regionData, regionSize, regionMinWorld, regionSizeWorld);
            }
            
            // If it modifies both, we need a more complex implementation in the derived class
            return (null, new List<Rid>(), new List<string>());
        }
    }
}