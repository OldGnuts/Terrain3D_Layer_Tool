using Godot;
using Terrain3DTools.Utils;

namespace Terrain3DTools.Visuals
{
    [Tool]
    public partial class RegionPreview : Node3D
    {
        [Export] public Vector2I RegionCoords { get; set; }

        // This RID is now the single source of truth for the texture data.
        // We will assign it a new value when the data is regenerated.
        public Rid LocalRegionRid { get; set; }

        [Export] public int RegionSize { get; set; } = 256;

        private MeshInstance3D _visualMesh;
        private PlaneMesh _planeMesh;
        private ShaderMaterial _shaderMat;

        private Texture2Drd _rdPreviewTexture;

        public override void _Ready()
        {
            if (Engine.IsEditorHint())
            {
                PositionPreviewInWorld();
                SetupVisualization();
            }
        }

        public override void _ExitTree()
        {
            base._ExitTree();
            if (Engine.IsEditorHint())
            {
                //GD.Print($"[DEBUG] EXIT_TREE for RegionPreview {RegionCoords}. Its HeightMap Rid was {LocalRegionRid.Id}. The associated Texture2Drd will now be freed by Godot.");
                if (IsInstanceValid(_rdPreviewTexture))
                    _rdPreviewTexture.TextureRdRid = new Rid();
            }
        }

        private void SetupVisualization()
        {
            _planeMesh = new PlaneMesh
            {
                Size = new Vector2(RegionSize, RegionSize),
                // Subdivision is only really necessary if you are displacing vertices.
                // If it's just a color preview, you can reduce this for performance.
                SubdivideDepth = RegionSize,
                SubdivideWidth = RegionSize
            };

            _shaderMat = new ShaderMaterial();
            var shader = GD.Load<Shader>("res://addons/terrain_3d_tools/Shaders/DebugPreviewShaderRegion.gdshader");
            _shaderMat.Shader = shader;

            _visualMesh = new MeshInstance3D
            {
                Mesh = _planeMesh,
                MaterialOverride = _shaderMat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off
            };

            _visualMesh.Position = new Vector3(RegionSize * 0.5f, 0, RegionSize * 0.5f);
            AddChild(_visualMesh);
        }

        private void PositionPreviewInWorld()
        {
            float worldX = RegionCoords.X * RegionSize;
            float worldZ = RegionCoords.Y * RegionSize;
            GlobalPosition = new Vector3(worldX, 0, worldZ);
        }

        /// <summary>
        /// This is the public method called by the GpuTaskManager's OnComplete callback.
        /// It directly updates the shader material to use the latest version of the region's
        /// texture RID, without any CPU readback.
        /// </summary>
        public void Refresh()
        {
            if (!IsInstanceValid(this) || !IsInstanceValid(_shaderMat))
            {
                return;
            }

            // Setup shared rd texture by sharing the resource handle from the local rendering device 
            // with the main rendering device and avoiding GPU -> CPU -> GPU copies
            if (LocalRegionRid.IsValid)
            {
                if (_rdPreviewTexture == null) _rdPreviewTexture = new Texture2Drd();
                if (!_rdPreviewTexture.TextureRdRid.IsValid)
                {
                    _rdPreviewTexture.TextureRdRid = TextureUtil.CreateSharedRenderingDeviceTextureRD(LocalRegionRid, (ulong)RegionSize, (ulong)RegionSize);
                    _shaderMat.SetShaderParameter("height_tex", _rdPreviewTexture);
                }
            }

        }
    }
}