// /Editor/ManualEditLayerInspector.cs
#if TOOLS
using Godot;
using System;
using Terrain3DTools.Brushes;
using Terrain3DTools.Layers;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Custom inspector for ManualEditLayer.
    /// Provides brush tool selection and settings UI.
    /// </summary>
    public partial class ManualEditLayerInspector : EditorInspectorPlugin
    {
        private ManualEditLayer _currentLayer;
        private static terrain_3d_tools _pluginInstance;

        /// <summary>
        /// Called by terrain_3d_tools to register itself.
        /// </summary>
        public static void SetPluginInstance(terrain_3d_tools plugin)
        {
            _pluginInstance = plugin;
            //GD.Print($"[ManualEditLayerInspector] Plugin instance set: {plugin != null}");
        }

        public override bool _CanHandle(GodotObject @object)
        {
            return @object is ManualEditLayer;
        }

        public override bool _ParseProperty(GodotObject @object, Variant.Type type, string name,
            PropertyHint hintType, string hintString, PropertyUsageFlags usageFlags, bool wide)
        {
            if (@object is ManualEditLayer layer)
            {
                _currentLayer = layer;
            }

            // Let default handling happen for most properties
            return false;
        }

        public override void _ParseBegin(GodotObject @object)
        {
            if (@object is not ManualEditLayer layer) return;

            _currentLayer = layer;

            //GD.Print($"[ManualEditLayerInspector] ParseBegin for layer: {layer.LayerName}");
            //GD.Print($"[ManualEditLayerInspector] Plugin instance available: {_pluginInstance != null}");

            // Add brush tools section
            var toolsContainer = CreateBrushToolsSection();
            AddCustomControl(toolsContainer);
        }

        private VBoxContainer CreateBrushToolsSection()
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 8);

            // Section header
            var headerLabel = new Label();
            headerLabel.Text = "Brush Tools";
            headerLabel.AddThemeFontSizeOverride("font_size", 14);
            container.AddChild(headerLabel);

            // Status label
            var statusLabel = new Label();
            statusLabel.Name = "StatusLabel";
            statusLabel.Text = _pluginInstance != null ? "Plugin connected" : "Plugin NOT connected - tools won't work!";
            statusLabel.AddThemeColorOverride("font_color", _pluginInstance != null ? Colors.Green : Colors.Red);
            container.AddChild(statusLabel);

            // Tool buttons grid
            var toolGrid = new GridContainer();
            toolGrid.Columns = 4;
            toolGrid.AddThemeConstantOverride("h_separation", 4);
            toolGrid.AddThemeConstantOverride("v_separation", 4);

            // Height tools
            AddToolButton(toolGrid, "↑", "Raise Height", BrushToolType.HeightRaise);
            AddToolButton(toolGrid, "↓", "Lower Height", BrushToolType.HeightLower);
            AddToolButton(toolGrid, "≈", "Smooth Height", BrushToolType.HeightSmooth);
            AddToolButton(toolGrid, "🎨", "Paint Texture", BrushToolType.TexturePaint);

            // Instance tools
            AddToolButton(toolGrid, "+", "Place Instance", BrushToolType.InstancePlace);
            AddToolButton(toolGrid, "-", "Erase Instance", BrushToolType.InstanceErase);
            AddToolButton(toolGrid, "⊘", "Exclude Area", BrushToolType.InstanceExclude);
            AddToolButton(toolGrid, "○", "No Tool", BrushToolType.None);

            container.AddChild(toolGrid);

            // Separator
            container.AddChild(new HSeparator());

            // Brush settings
            var settingsLabel = new Label();
            settingsLabel.Text = "Brush Settings";
            settingsLabel.AddThemeFontSizeOverride("font_size", 12);
            container.AddChild(settingsLabel);

            // Size slider
            var sizeContainer = new HBoxContainer();
            var sizeLabel = new Label { Text = "Size:", CustomMinimumSize = new Vector2(60, 0) };
            var sizeSlider = new HSlider
            {
                MinValue = 1,
                MaxValue = 500,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.Size ?? 50,
                CustomMinimumSize = new Vector2(150, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var sizeValue = new Label { Text = sizeSlider.Value.ToString("0"), CustomMinimumSize = new Vector2(40, 0) };

            sizeSlider.ValueChanged += (value) =>
            {
                sizeValue.Text = value.ToString("0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.Size = (float)value;
                //GD.Print($"[ManualEditLayerInspector] Size changed to: {value}");
            };

            sizeContainer.AddChild(sizeLabel);
            sizeContainer.AddChild(sizeSlider);
            sizeContainer.AddChild(sizeValue);
            container.AddChild(sizeContainer);

            // Strength slider
            var strengthContainer = new HBoxContainer();
            var strengthLabel = new Label { Text = "Strength:", CustomMinimumSize = new Vector2(60, 0) };
            var strengthSlider = new HSlider
            {
                MinValue = 0,
                MaxValue = 1,
                Step = 0.01,
                Value = _pluginInstance?.GetBrushSettings()?.Strength ?? 0.5,
                CustomMinimumSize = new Vector2(150, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var strengthValue = new Label { Text = strengthSlider.Value.ToString("0.00"), CustomMinimumSize = new Vector2(40, 0) };

            strengthSlider.ValueChanged += (value) =>
            {
                strengthValue.Text = value.ToString("0.00");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.Strength = (float)value;
            };

            strengthContainer.AddChild(strengthLabel);
            strengthContainer.AddChild(strengthSlider);
            strengthContainer.AddChild(strengthValue);
            container.AddChild(strengthContainer);

            // Falloff slider
            var falloffContainer = new HBoxContainer();
            var falloffLabel = new Label { Text = "Falloff:", CustomMinimumSize = new Vector2(60, 0) };
            var falloffSlider = new HSlider
            {
                MinValue = 0,
                MaxValue = 1,
                Step = 0.01,
                Value = _pluginInstance?.GetBrushSettings()?.Falloff ?? 0.5,
                CustomMinimumSize = new Vector2(150, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var falloffValue = new Label { Text = falloffSlider.Value.ToString("0.00"), CustomMinimumSize = new Vector2(40, 0) };

            falloffSlider.ValueChanged += (value) =>
            {
                falloffValue.Text = value.ToString("0.00");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.Falloff = (float)value;
            };

            falloffContainer.AddChild(falloffLabel);
            falloffContainer.AddChild(falloffSlider);
            falloffContainer.AddChild(falloffValue);
            container.AddChild(falloffContainer);

            // Height delta slider (for height tools)
            var heightDeltaContainer = new HBoxContainer();
            var heightDeltaLabel = new Label { Text = "Delta:", CustomMinimumSize = new Vector2(60, 0) };
            var heightDeltaSlider = new HSlider
            {
                MinValue = 0.01,
                MaxValue = 1,
                Step = 0.01,
                Value = _pluginInstance?.GetBrushSettings()?.HeightDelta ?? 0.1,
                CustomMinimumSize = new Vector2(150, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var heightDeltaValue = new Label { Text = heightDeltaSlider.Value.ToString("0.00"), CustomMinimumSize = new Vector2(40, 0) };

            heightDeltaSlider.ValueChanged += (value) =>
            {
                heightDeltaValue.Text = value.ToString("0.00");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.HeightDelta = (float)value;
            };

            heightDeltaContainer.AddChild(heightDeltaLabel);
            heightDeltaContainer.AddChild(heightDeltaSlider);
            heightDeltaContainer.AddChild(heightDeltaValue);
            container.AddChild(heightDeltaContainer);

            // Separator
            container.AddChild(new HSeparator());

            // Layer actions
            var actionsLabel = new Label();
            actionsLabel.Text = "Layer Actions";
            actionsLabel.AddThemeFontSizeOverride("font_size", 12);
            container.AddChild(actionsLabel);

            var actionsContainer = new HBoxContainer();

            var clearButton = new Button { Text = "Clear All Edits" };
            clearButton.Pressed += () =>
            {
                if (_currentLayer != null)
                {
                    _currentLayer.ClearAllEdits();
                    //GD.Print("[ManualEditLayerInspector] Cleared all edits");
                }
            };
            actionsContainer.AddChild(clearButton);

            container.AddChild(actionsContainer);

            // Texture-specific settings (shown when texture tool is active)
            var textureSettingsContainer = new VBoxContainer();
            textureSettingsContainer.Name = "TextureSettings";

            var textureSectionLabel = new Label();
            textureSectionLabel.Text = "Texture Settings";
            textureSectionLabel.AddThemeFontSizeOverride("font_size", 12);
            textureSettingsContainer.AddChild(textureSectionLabel);

            // Texture ID
            var textureIdContainer = new HBoxContainer();
            var textureIdLabel = new Label { Text = "Texture ID:", CustomMinimumSize = new Vector2(80, 0) };
            var textureIdSpinBox = new SpinBox
            {
                MinValue = 0,
                MaxValue = 31,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.TextureId ?? 0,
                CustomMinimumSize = new Vector2(80, 0)
            };
            textureIdSpinBox.ValueChanged += (value) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.TextureId = (int)value;
            };
            textureIdContainer.AddChild(textureIdLabel);
            textureIdContainer.AddChild(textureIdSpinBox);
            textureSettingsContainer.AddChild(textureIdContainer);

            // Texture Mode
            var textureModeContainer = new HBoxContainer();
            var textureModeLabel = new Label { Text = "Mode:", CustomMinimumSize = new Vector2(80, 0) };
            var textureModeOption = new OptionButton();
            textureModeOption.AddItem("Paint Overlay", (int)TextureBrushMode.PaintOverlay);
            textureModeOption.AddItem("Paint Base", (int)TextureBrushMode.PaintBase);
            textureModeOption.AddItem("Adjust Blend", (int)TextureBrushMode.AdjustBlend);
            textureModeOption.AddItem("Full Replace", (int)TextureBrushMode.FullReplace);
            textureModeOption.Selected = (int)(_pluginInstance?.GetBrushSettings()?.TextureMode ?? TextureBrushMode.PaintOverlay);
            textureModeOption.ItemSelected += (index) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.TextureMode = (TextureBrushMode)index;
            };
            textureModeContainer.AddChild(textureModeLabel);
            textureModeContainer.AddChild(textureModeOption);
            textureSettingsContainer.AddChild(textureModeContainer);

            // Secondary Texture ID (for Full Replace mode)
            var secondaryTextureContainer = new HBoxContainer();
            var secondaryTextureLabel = new Label { Text = "Base Tex ID:", CustomMinimumSize = new Vector2(80, 0) };
            var secondaryTextureSpinBox = new SpinBox
            {
                MinValue = 0,
                MaxValue = 31,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.SecondaryTextureId ?? 0,
                CustomMinimumSize = new Vector2(80, 0)
            };
            secondaryTextureSpinBox.ValueChanged += (value) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.SecondaryTextureId = (int)value;
            };
            secondaryTextureContainer.AddChild(secondaryTextureLabel);
            secondaryTextureContainer.AddChild(secondaryTextureSpinBox);
            textureSettingsContainer.AddChild(secondaryTextureContainer);

            // Target Blend
            var targetBlendContainer = new HBoxContainer();
            var targetBlendLabel = new Label { Text = "Target Blend:", CustomMinimumSize = new Vector2(80, 0) };
            var targetBlendSlider = new HSlider
            {
                MinValue = 0,
                MaxValue = 255,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.TargetBlend ?? 255,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var targetBlendValue = new Label { Text = targetBlendSlider.Value.ToString("0"), CustomMinimumSize = new Vector2(40, 0) };
            targetBlendSlider.ValueChanged += (value) =>
            {
                targetBlendValue.Text = value.ToString("0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.TargetBlend = (int)value;
            };
            targetBlendContainer.AddChild(targetBlendLabel);
            targetBlendContainer.AddChild(targetBlendSlider);
            targetBlendContainer.AddChild(targetBlendValue);
            textureSettingsContainer.AddChild(targetBlendContainer);

            // Accumulate Blend checkbox
            var accumulateContainer = new HBoxContainer();
            var accumulateCheck = new CheckBox
            {
                Text = "Accumulate Blend",
                ButtonPressed = _pluginInstance?.GetBrushSettings()?.AccumulateBlend ?? true
            };
            accumulateCheck.Toggled += (pressed) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.AccumulateBlend = pressed;
            };
            accumulateContainer.AddChild(accumulateCheck);
            textureSettingsContainer.AddChild(accumulateContainer);

            // Blend Step
            var blendStepContainer = new HBoxContainer();
            var blendStepLabel = new Label { Text = "Blend Step:", CustomMinimumSize = new Vector2(80, 0) };
            var blendStepSlider = new HSlider
            {
                MinValue = 1,
                MaxValue = 50,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.BlendStep ?? 10,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var blendStepValue = new Label { Text = blendStepSlider.Value.ToString("0"), CustomMinimumSize = new Vector2(40, 0) };
            blendStepSlider.ValueChanged += (value) =>
            {
                blendStepValue.Text = value.ToString("0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.BlendStep = (int)value;
            };
            blendStepContainer.AddChild(blendStepLabel);
            blendStepContainer.AddChild(blendStepSlider);
            blendStepContainer.AddChild(blendStepValue);
            textureSettingsContainer.AddChild(blendStepContainer);

            container.AddChild(textureSettingsContainer);

            // Instance Exclusion settings
            var exclusionSettingsContainer = new VBoxContainer();
            exclusionSettingsContainer.Name = "ExclusionSettings";

            var exclusionSectionLabel = new Label();
            exclusionSectionLabel.Text = "Instance Exclusion Settings";
            exclusionSectionLabel.AddThemeFontSizeOverride("font_size", 12);
            exclusionSettingsContainer.AddChild(exclusionSectionLabel);

            // Exclusion Mode (Add/Remove toggle)
            var exclusionModeContainer = new HBoxContainer();
            var exclusionModeLabel = new Label { Text = "Mode:", CustomMinimumSize = new Vector2(80, 0) };
            var exclusionModeOption = new OptionButton();
            exclusionModeOption.AddItem("Add Exclusion (Block)", 0);
            exclusionModeOption.AddItem("Remove Exclusion (Allow)", 1);
            exclusionModeOption.Selected = (_pluginInstance?.GetBrushSettings()?.ExclusionMode ?? true) ? 0 : 1;
            exclusionModeOption.ItemSelected += (index) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.ExclusionMode = (index == 0);
            };
            exclusionModeContainer.AddChild(exclusionModeLabel);
            exclusionModeContainer.AddChild(exclusionModeOption);
            exclusionSettingsContainer.AddChild(exclusionModeContainer);

            // Exclusion Brush Size
            var exclusionSizeContainer = new HBoxContainer();
            var exclusionSizeLabel = new Label { Text = "Brush Size:", CustomMinimumSize = new Vector2(80, 0) };
            var exclusionSizeSlider = new HSlider
            {
                MinValue = 1,
                MaxValue = 100,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.ExclusionBrushSize ?? 10,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var exclusionSizeValue = new Label { Text = exclusionSizeSlider.Value.ToString("0"), CustomMinimumSize = new Vector2(40, 0) };
            exclusionSizeSlider.ValueChanged += (value) =>
            {
                exclusionSizeValue.Text = value.ToString("0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.ExclusionBrushSize = (float)value;
            };
            exclusionSizeContainer.AddChild(exclusionSizeLabel);
            exclusionSizeContainer.AddChild(exclusionSizeSlider);
            exclusionSizeContainer.AddChild(exclusionSizeValue);
            exclusionSettingsContainer.AddChild(exclusionSizeContainer);

            // Accumulate checkbox
            var exclusionAccumulateContainer = new HBoxContainer();
            var exclusionAccumulateCheck = new CheckBox
            {
                Text = "Accumulate (Gradual)",
                ButtonPressed = _pluginInstance?.GetBrushSettings()?.ExclusionAccumulate ?? true
            };
            exclusionAccumulateCheck.Toggled += (pressed) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.ExclusionAccumulate = pressed;
            };
            exclusionAccumulateContainer.AddChild(exclusionAccumulateCheck);
            exclusionSettingsContainer.AddChild(exclusionAccumulateContainer);

            container.AddChild(exclusionSettingsContainer);


            // Instance Placement settings
            var instanceSettingsContainer = new VBoxContainer();
            instanceSettingsContainer.Name = "InstancePlacementSettings";

            var instanceSectionLabel = new Label();
            instanceSectionLabel.Text = "Instance Placement Settings";
            instanceSectionLabel.AddThemeFontSizeOverride("font_size", 12);
            instanceSettingsContainer.AddChild(instanceSectionLabel);

            // Mesh ID
            var meshIdContainer = new HBoxContainer();
            var meshIdLabel = new Label { Text = "Mesh ID:", CustomMinimumSize = new Vector2(100, 0) };
            var meshIdSpinBox = new SpinBox
            {
                MinValue = 0,
                MaxValue = 100,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.InstanceMeshId ?? 0,
                CustomMinimumSize = new Vector2(80, 0)
            };
            meshIdSpinBox.ValueChanged += (value) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceMeshId = (int)value;
            };
            meshIdContainer.AddChild(meshIdLabel);
            meshIdContainer.AddChild(meshIdSpinBox);
            instanceSettingsContainer.AddChild(meshIdContainer);

            // Scale
            var scaleContainer = new HBoxContainer();
            var scaleLabel = new Label { Text = "Scale:", CustomMinimumSize = new Vector2(100, 0) };
            var scaleSlider = new HSlider
            {
                MinValue = 0.1,
                MaxValue = 10,
                Step = 0.1,
                Value = _pluginInstance?.GetBrushSettings()?.InstanceScale ?? 1.0,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var scaleValue = new Label { Text = scaleSlider.Value.ToString("0.0"), CustomMinimumSize = new Vector2(40, 0) };
            scaleSlider.ValueChanged += (value) =>
            {
                scaleValue.Text = value.ToString("0.0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceScale = (float)value;
            };
            scaleContainer.AddChild(scaleLabel);
            scaleContainer.AddChild(scaleSlider);
            scaleContainer.AddChild(scaleValue);
            instanceSettingsContainer.AddChild(scaleContainer);

            // Scale Variation
            var scaleVarContainer = new HBoxContainer();
            var scaleVarLabel = new Label { Text = "Scale Variation:", CustomMinimumSize = new Vector2(100, 0) };
            var scaleVarSlider = new HSlider
            {
                MinValue = 0,
                MaxValue = 1,
                Step = 0.01,
                Value = _pluginInstance?.GetBrushSettings()?.RandomScaleVariation ?? 0.1,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var scaleVarValue = new Label { Text = scaleVarSlider.Value.ToString("0.00"), CustomMinimumSize = new Vector2(40, 0) };
            scaleVarSlider.ValueChanged += (value) =>
            {
                scaleVarValue.Text = value.ToString("0.00");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.RandomScaleVariation = (float)value;
            };
            scaleVarContainer.AddChild(scaleVarLabel);
            scaleVarContainer.AddChild(scaleVarSlider);
            scaleVarContainer.AddChild(scaleVarValue);
            instanceSettingsContainer.AddChild(scaleVarContainer);

            // Random Rotation checkbox
            var rotationCheck = new CheckBox
            {
                Text = "Random Y Rotation",
                ButtonPressed = _pluginInstance?.GetBrushSettings()?.RandomRotation ?? true
            };
            rotationCheck.Toggled += (pressed) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.RandomRotation = pressed;
            };
            instanceSettingsContainer.AddChild(rotationCheck);

            // Align to Normal checkbox
            var alignCheck = new CheckBox
            {
                Text = "Align to Terrain Normal",
                ButtonPressed = _pluginInstance?.GetBrushSettings()?.InstanceAlignToNormal ?? false
            };
            alignCheck.Toggled += (pressed) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceAlignToNormal = pressed;
            };
            instanceSettingsContainer.AddChild(alignCheck);

            // Random Tilt
            var tiltContainer = new HBoxContainer();
            var tiltLabel = new Label { Text = "Random Tilt (°):", CustomMinimumSize = new Vector2(100, 0) };
            var tiltSlider = new HSlider
            {
                MinValue = 0,
                MaxValue = 45,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.InstanceRandomTilt ?? 0,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var tiltValue = new Label { Text = tiltSlider.Value.ToString("0"), CustomMinimumSize = new Vector2(40, 0) };
            tiltSlider.ValueChanged += (value) =>
            {
                tiltValue.Text = value.ToString("0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceRandomTilt = (float)value;
            };
            tiltContainer.AddChild(tiltLabel);
            tiltContainer.AddChild(tiltSlider);
            tiltContainer.AddChild(tiltValue);
            instanceSettingsContainer.AddChild(tiltContainer);

            // Vertical Offset
            var offsetContainer = new HBoxContainer();
            var offsetLabel = new Label { Text = "Vertical Offset:", CustomMinimumSize = new Vector2(100, 0) };
            var offsetSlider = new HSlider
            {
                MinValue = -10,
                MaxValue = 10,
                Step = 0.1,
                Value = _pluginInstance?.GetBrushSettings()?.InstanceVerticalOffset ?? 0,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var offsetValue = new Label { Text = offsetSlider.Value.ToString("0.0"), CustomMinimumSize = new Vector2(40, 0) };
            offsetSlider.ValueChanged += (value) =>
            {
                offsetValue.Text = value.ToString("0.0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceVerticalOffset = (float)value;
            };
            offsetContainer.AddChild(offsetLabel);
            offsetContainer.AddChild(offsetSlider);
            offsetContainer.AddChild(offsetValue);
            instanceSettingsContainer.AddChild(offsetContainer);

            // Scatter Mode section
            instanceSettingsContainer.AddChild(new HSeparator());

            var scatterCheck = new CheckBox
            {
                Text = "Scatter Mode (Drag to Place Multiple)",
                ButtonPressed = _pluginInstance?.GetBrushSettings()?.InstanceScatterMode ?? false
            };
            scatterCheck.Toggled += (pressed) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceScatterMode = pressed;
            };
            instanceSettingsContainer.AddChild(scatterCheck);

            // Scatter Count
            var scatterCountContainer = new HBoxContainer();
            var scatterCountLabel = new Label { Text = "Scatter Count:", CustomMinimumSize = new Vector2(100, 0) };
            var scatterCountSpinBox = new SpinBox
            {
                MinValue = 1,
                MaxValue = 50,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.InstanceScatterCount ?? 5,
                CustomMinimumSize = new Vector2(80, 0)
            };
            scatterCountSpinBox.ValueChanged += (value) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceScatterCount = (int)value;
            };
            scatterCountContainer.AddChild(scatterCountLabel);
            scatterCountContainer.AddChild(scatterCountSpinBox);
            instanceSettingsContainer.AddChild(scatterCountContainer);

            // Min Distance
            var minDistContainer = new HBoxContainer();
            var minDistLabel = new Label { Text = "Min Distance:", CustomMinimumSize = new Vector2(100, 0) };
            var minDistSlider = new HSlider
            {
                MinValue = 0.5,
                MaxValue = 50,
                Step = 0.5,
                Value = _pluginInstance?.GetBrushSettings()?.InstanceMinDistance ?? 2.0,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var minDistValue = new Label { Text = minDistSlider.Value.ToString("0.0"), CustomMinimumSize = new Vector2(40, 0) };
            minDistSlider.ValueChanged += (value) =>
            {
                minDistValue.Text = value.ToString("0.0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceMinDistance = (float)value;
            };
            minDistContainer.AddChild(minDistLabel);
            minDistContainer.AddChild(minDistSlider);
            minDistContainer.AddChild(minDistValue);
            instanceSettingsContainer.AddChild(minDistContainer);

            // Slope Limit
            var slopeLimitCheck = new CheckBox
            {
                Text = "Limit Slope",
                ButtonPressed = _pluginInstance?.GetBrushSettings()?.InstanceSlopeLimitEnabled ?? false
            };
            slopeLimitCheck.Toggled += (pressed) =>
            {
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceSlopeLimitEnabled = pressed;
            };
            instanceSettingsContainer.AddChild(slopeLimitCheck);

            var maxSlopeContainer = new HBoxContainer();
            var maxSlopeLabel = new Label { Text = "Max Slope (°):", CustomMinimumSize = new Vector2(100, 0) };
            var maxSlopeSlider = new HSlider
            {
                MinValue = 0,
                MaxValue = 90,
                Step = 1,
                Value = _pluginInstance?.GetBrushSettings()?.InstanceMaxSlope ?? 45,
                CustomMinimumSize = new Vector2(100, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            var maxSlopeValue = new Label { Text = maxSlopeSlider.Value.ToString("0"), CustomMinimumSize = new Vector2(40, 0) };
            maxSlopeSlider.ValueChanged += (value) =>
            {
                maxSlopeValue.Text = value.ToString("0");
                var settings = _pluginInstance?.GetBrushSettings();
                if (settings != null) settings.InstanceMaxSlope = (float)value;
            };
            maxSlopeContainer.AddChild(maxSlopeLabel);
            maxSlopeContainer.AddChild(maxSlopeSlider);
            maxSlopeContainer.AddChild(maxSlopeValue);
            instanceSettingsContainer.AddChild(maxSlopeContainer);

            container.AddChild(instanceSettingsContainer);

            return container;
        }

        private void AddToolButton(GridContainer grid, string icon, string tooltip, BrushToolType toolType)
        {
            var button = new Button
            {
                Text = icon,
                TooltipText = tooltip,
                CustomMinimumSize = new Vector2(32, 32),
                ToggleMode = true
            };

            button.Pressed += () =>
            {
                //GD.Print($"[ManualEditLayerInspector] Tool button pressed: {toolType}, ButtonPressed: {button.ButtonPressed}");

                // Deselect other buttons in this grid
                foreach (var child in grid.GetChildren())
                {
                    if (child is Button otherButton && otherButton != button)
                    {
                        otherButton.ButtonPressed = false;
                    }
                }

                // Set tool
                if (_pluginInstance != null)
                {
                    var newToolType = button.ButtonPressed ? toolType : BrushToolType.None;
                    _pluginInstance.SetBrushTool(newToolType);
                    //GD.Print($"[ManualEditLayerInspector] Set brush tool to: {newToolType}");
                }
                else
                {
                    GD.PrintErr("[ManualEditLayerInspector] Plugin instance is null!");
                }
            };

            grid.AddChild(button);
        }
    }
}
#endif