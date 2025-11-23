// /Masks/TerrainMask.cs 
using Godot;
using System;
using System.Collections.Generic;

namespace Terrain3DTools.Masks
{
    public enum MaskRequirements
    {
        None = 0,
        RequiresHeightData = 1,
        RequiresTextureData = 2,
        RequiredHeightAndTextureData = 3,
    }
    public enum MaskBlendType
    {
        Mix = 0,
        Multiply = 1,
        Add = 2,
        Subtract = 3
    }

    [GlobalClass, Tool]
    public abstract partial class TerrainMask : Resource
    {   
        #region Private Fields
        private bool _invert = false;
        private float _layerMix = 1.0f;
        private MaskBlendType _blendType = MaskBlendType.Multiply;
        #endregion

        #region Exported Properties
        [Export]
        public bool Invert
        {
            get => _invert;
            set => SetProperty(ref _invert, value);
        }

        [Export(PropertyHint.Range, "0.0,1.0,0.01")]
        public float LayerMix
        {
            get => _layerMix;
            set => SetProperty(ref _layerMix, value);
        }

        [Export]
        public MaskBlendType BlendType
        {
            get => _blendType;
            set => SetProperty(ref _blendType, value);
        }
        #endregion

        protected void SetProperty<T>(ref T field, T value)
        {
            if (!Equals(field, value))
            {
                field = value;
                EmitChanged();
            }
        }

        #region Abstract Mask Application
        
        /// <summary>
        /// Creates the GPU commands to apply this mask's effect to a target texture.
        /// This method now returns an Action that accepts a compute list handle.
        /// </summary>
        /// <returns>A tuple containing the GPU command Action<long> and a list of any temporary RIDs created and the shaderPath.</returns>
        public abstract (Action<long> commands, List<Rid> tempRids, List<string>) CreateApplyCommands(Rid targetMaskTexture, int maskWidth, int maskHeight, Rid stitchedHeightmap = new Rid());
        
        /// <summary>
        /// Each mask must declare the type of data it requires to function.
        /// </summary>
        public abstract MaskRequirements MaskDataRequirements();
        #endregion

        /// <summary>
        /// A virtual method for a mask to declare if it requires the base terrain height data.
        /// </summary>
        public virtual bool RequiresBaseHeightData() => false;
    }

}
