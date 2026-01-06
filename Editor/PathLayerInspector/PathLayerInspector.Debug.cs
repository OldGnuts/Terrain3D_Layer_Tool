// /Editor/PathLayerInspector.Debug.cs
using Godot;
using Terrain3DTools.Editor.Utils;

namespace Terrain3DTools.Editor
{
    public partial class PathLayerInspector
    {
        #region Debug Section
        private void AddDebugSection()
        {
            CreateCollapsibleSection("Debug", false);
            var content = GetSectionContent("Debug");
            var layer = CurrentLayer;

            if (content == null || layer == null) return;

            // Show Profile Cross Section
            var showProfileCheck = new CheckBox
            {
                Text = "Show Profile Cross Section",
                ButtonPressed = layer.ShowProfileCrossSection,
                TooltipText = "Display the profile cross-section visualization in the viewport"
            };
            showProfileCheck.Toggled += (enabled) =>
            {
                var l = CurrentLayer;
                if (l != null) l.ShowProfileCrossSection = enabled;
            };
            content.AddChild(showProfileCheck);

            // Layer Info
            EditorUIUtils.AddSeparator(content, 4);

            var infoLabel = new Label { Text = "Layer Information:" };
            infoLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            content.AddChild(infoLabel);

            var infoGrid = new GridContainer { Columns = 2 };
            infoGrid.AddThemeConstantOverride("h_separation", 16);
            infoGrid.AddThemeConstantOverride("v_separation", 4);

            EditorUIUtils.AddStatRow(infoGrid, "Modifies Height:", layer.ModifiesHeight ? "Yes" : "No");
            EditorUIUtils.AddStatRow(infoGrid, "Modifies Texture:", layer.ModifiesTexture ? "Yes" : "No");
            EditorUIUtils.AddStatRow(infoGrid, "Layer Type:", layer.LayerTypeName());

            content.AddChild(infoGrid);
        }
        #endregion
    }
}