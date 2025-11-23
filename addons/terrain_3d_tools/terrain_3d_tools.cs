#if TOOLS
using Godot;
using Terrain3DTools.Editor;

namespace Terrain3DTools
{
    [Tool]
    public partial class terrain_3d_tools : EditorPlugin
    {
        TerrainLayerInspector terrainLayerInspector;

        public override void _EnterTree()
        {
            terrainLayerInspector = new TerrainLayerInspector();
            AddInspectorPlugin(terrainLayerInspector);
        }

        public override void _ExitTree()
        {
            RemoveInspectorPlugin(terrainLayerInspector);
            terrainLayerInspector = null;
        }
    }
}
#endif