// /Editor/PathLayerInspector.Constraints.cs
using System.Collections.Generic;
using System.Linq;
using Godot;
using Terrain3DTools.Layers;
using Terrain3DTools.Layers.Path;
using Terrain3DTools.Editor.Utils;

namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Elevation constraint UI for PathLayerInspector.
    /// Displays grade and downhill constraint status, violations, and enforcement controls.
    /// </summary>
    public partial class PathLayerInspector
    {
        #region Constraint Fields
        private VBoxContainer _constraintContentContainer;
        private VBoxContainer _constraintStatusContainer;
        private Label _constraintStatusLabel;
        private VBoxContainer _violationListContainer;
        private VBoxContainer _feasibilityContainer;
        private Button _enforceButton;

        // Inline header warning labels
        private Label _headerGradeWarning;
        private Label _headerDownhillWarning;
        private HBoxContainer _headerWarningContainer;

        // Cached UI elements for updates
        private SpinBox _gradeSpinBox;
        private Label _flowDirectionLabel;
        private Label _elevationRangeLabel;
        #endregion

        #region Constraint Section
        private void AddConstraintSection()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Only show for path types that use constraints
            bool showGrade = layer.UsesGradeConstraint;
            bool showDownhill = layer.UsesDownhillConstraint;

            if (!showGrade && !showDownhill) return;

            // Build help content
            string helpContent = BuildConstraintHelpContent(showGrade, showDownhill);

            // Create custom collapsible with inline warnings
            var content = CreateConstraintCollapsibleSection(
                "Elevation Constraints",
                false,
                "Elevation Constraints Help",
                helpContent
            );

            if (content == null) return;

            // Info banner for rivers/waterways
            if (showDownhill)
            {
                AddWaterwayInfoBanner(content);
            }

            // Constraint toggles
            AddConstraintToggles(content, layer, showGrade, showDownhill);

            EditorUIUtils.AddSeparator(content, 8);

            // Status display container
            _constraintStatusContainer = new VBoxContainer();
            _constraintStatusContainer.AddThemeConstantOverride("separation", 6);
            content.AddChild(_constraintStatusContainer);

            // Status label row
            var statusRow = new HBoxContainer();
            statusRow.AddThemeConstantOverride("separation", 8);

            statusRow.AddChild(new Label
            {
                Text = "Status:",
                CustomMinimumSize = new Vector2(50, 0)
            });

            _constraintStatusLabel = new Label
            {
                Text = "Checking...",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            statusRow.AddChild(_constraintStatusLabel);
            _constraintStatusContainer.AddChild(statusRow);

            // Feasibility analysis container (populated dynamically)
            _feasibilityContainer = new VBoxContainer();
            _feasibilityContainer.AddThemeConstantOverride("separation", 4);
            _constraintStatusContainer.AddChild(_feasibilityContainer);

            // Violation details (inline collapsible)
            _violationListContainer = EditorUIUtils.CreateInlineCollapsible(
                _constraintStatusContainer,
                "Violation Details",
                false,
                "Constraint Violations",
                _sectionExpanded
            );

            EditorUIUtils.AddSeparator(content, 4);

            // Enforcement buttons
            AddEnforcementControls(content, layer);

            // Initial refresh
            RefreshConstraintStatus();
        }

        /// <summary>
        /// Creates a custom collapsible section with inline warning indicators in the header.
        /// </summary>
        private VBoxContainer CreateConstraintCollapsibleSection(
            string title,
            bool defaultExpanded,
            string helpTitle,
            string helpContent)
        {
            // Check for persisted state
            bool isExpanded = defaultExpanded;
            if (_sectionExpanded.TryGetValue(title, out bool savedState))
            {
                isExpanded = savedState;
            }

            var outerContainer = new VBoxContainer();
            outerContainer.AddThemeConstantOverride("separation", 4);

            // Header row (HBoxContainer to hold button + warnings + help)
            var headerRow = new HBoxContainer();
            headerRow.AddThemeConstantOverride("separation", 4);

            // Header button
            var headerButton = new Button
            {
                Text = (isExpanded ? "▼ " : "▶ ") + title,
                Flat = true,
                Alignment = HorizontalAlignment.Left,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            headerButton.AddThemeColorOverride("font_color", new Color(0.9f, 0.9f, 0.9f));
            headerButton.AddThemeFontSizeOverride("font_size", 14);
            headerRow.AddChild(headerButton);

            // Warning indicators container
            _headerWarningContainer = new HBoxContainer();
            _headerWarningContainer.AddThemeConstantOverride("separation", 6);

            // Grade warning label
            _headerGradeWarning = new Label
            {
                Text = "",
                Visible = false
            };
            _headerGradeWarning.AddThemeColorOverride("font_color", new Color(1.0f, 0.7f, 0.3f));
            _headerGradeWarning.AddThemeFontSizeOverride("font_size", 12);
            _headerWarningContainer.AddChild(_headerGradeWarning);

            // Downhill warning label
            _headerDownhillWarning = new Label
            {
                Text = "",
                Visible = false
            };
            _headerDownhillWarning.AddThemeColorOverride("font_color", new Color(0.5f, 0.8f, 1.0f));
            _headerDownhillWarning.AddThemeFontSizeOverride("font_size", 12);
            _headerWarningContainer.AddChild(_headerDownhillWarning);

            headerRow.AddChild(_headerWarningContainer);

            // Help button
            if (!string.IsNullOrEmpty(helpTitle) && !string.IsNullOrEmpty(helpContent))
            {
                var helpButton = EditorHelpTooltip.CreateHelpButtonStyled(helpTitle, helpContent);
                headerRow.AddChild(helpButton);
            }

            // Content container
            _constraintContentContainer = new VBoxContainer();
            _constraintContentContainer.AddThemeConstantOverride("separation", 4);
            _constraintContentContainer.Visible = isExpanded;

            headerButton.Pressed += () =>
            {
                bool newState = !_constraintContentContainer.Visible;
                _constraintContentContainer.Visible = newState;
                headerButton.Text = (newState ? "▼ " : "▶ ") + title;
                _sectionExpanded[title] = newState;
            };

            outerContainer.AddChild(headerRow);
            outerContainer.AddChild(_constraintContentContainer);
            _mainContainer.AddChild(outerContainer);

            // Store in section contents for consistency
            _sectionContents[title] = _constraintContentContainer;

            return _constraintContentContainer;
        }

        private string BuildConstraintHelpContent(bool showGrade, bool showDownhill)
        {
            var items = new List<(string, string)>();

            if (showGrade)
            {
                items.Add(("Max Grade", "Maximum allowed slope percentage. Roads typically use 6-12%, trails 10-15%, railways 2-4%."));
                items.Add(("Grade Violations", "Segments where the slope exceeds the maximum allowed grade."));
                items.Add(("Switchbacks", "When the path is too steep, the tool can add zigzag points to increase horizontal distance."));
            }

            if (showDownhill)
            {
                items.Add(("Downhill Constraint", "Ensures water always flows downhill (no uphill segments)."));
                items.Add(("Flow Direction", "Auto-detected based on endpoint elevations. First point should be the source (highest)."));
            }

            items.Add(("Enforce", "Automatically adjusts point heights to satisfy constraints. May add switchback points if needed. Use Undo (Ctrl+Z) to revert."));

            string description = showGrade && showDownhill
                ? "Control elevation changes along the path to ensure realistic grades or proper water flow."
                : showGrade
                    ? "Control the maximum slope percentage allowed along the path."
                    : "Ensure water features always flow downhill from source to mouth.";

            return EditorHelpTooltip.FormatHelpText(description, items,
                showDownhill ? "Tip: Create rivers starting from the highest point (source) for best results." : null);
        }

        private void AddWaterwayInfoBanner(VBoxContainer content)
        {
            var banner = new PanelContainer();
            var style = new StyleBoxFlat
            {
                BgColor = new Color(0.2f, 0.35f, 0.45f, 0.5f),
                CornerRadiusBottomLeft = 4,
                CornerRadiusBottomRight = 4,
                CornerRadiusTopLeft = 4,
                CornerRadiusTopRight = 4,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 4,
                ContentMarginBottom = 4
            };
            banner.AddThemeStyleboxOverride("panel", style);

            var label = new Label
            {
                Text = "💧 For best results, create waterways starting from the source (highest point) and work downstream.",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            banner.AddChild(label);

            content.AddChild(banner);
            EditorUIUtils.AddSeparator(content, 4);
        }

        private void AddConstraintToggles(VBoxContainer content, PathLayer layer, bool showGrade, bool showDownhill)
        {
            if (showGrade)
            {
                // Grade constraint toggle
                var gradeRow = new HBoxContainer();
                gradeRow.AddThemeConstantOverride("separation", 8);

                var gradeCheck = new CheckBox
                {
                    Text = "Limit Maximum Grade",
                    ButtonPressed = layer.EnableGradeConstraint,
                    TooltipText = "Enable to restrict the maximum slope of the path"
                };
                gradeCheck.Toggled += (enabled) =>
                {
                    var l = CurrentLayer;
                    if (l != null)
                    {
                        l.EnableGradeConstraint = enabled;
                        RefreshConstraintStatus();
                    }
                };
                gradeRow.AddChild(gradeCheck);

                // Max grade spinner
                var gradeLabel = new Label
                {
                    Text = "Max:",
                    CustomMinimumSize = new Vector2(35, 0)
                };
                gradeRow.AddChild(gradeLabel);

                _gradeSpinBox = new SpinBox
                {
                    MinValue = 0.5,
                    MaxValue = 30.0,
                    Step = 0.5,
                    Value = layer.MaxGradePercent,
                    CustomMinimumSize = new Vector2(70, 0),
                    TooltipText = "Maximum grade percentage (rise/run × 100)"
                };
                _gradeSpinBox.ValueChanged += (value) =>
                {
                    var l = CurrentLayer;
                    if (l != null)
                    {
                        l.MaxGradePercent = (float)value;
                        RefreshConstraintStatus();
                    }
                };
                gradeRow.AddChild(_gradeSpinBox);

                var percentLabel = new Label { Text = "%" };
                gradeRow.AddChild(percentLabel);

                content.AddChild(gradeRow);

                // Grade presets
                var presetRow = new HBoxContainer();
                presetRow.AddThemeConstantOverride("separation", 4);

                presetRow.AddChild(new Label
                {
                    Text = "Presets:",
                    CustomMinimumSize = new Vector2(55, 0)
                });

                var presets = new (string name, float value)[]
                {
                    ("Railway 3%", 3f),
                    ("Highway 6%", 6f),
                    ("Road 8%", 8f),
                    ("Trail 15%", 15f)
                };

                foreach (var (name, value) in presets)
                {
                    var btn = new Button
                    {
                        Text = name,
                        TooltipText = $"Set max grade to {value}%"
                    };
                    float capturedValue = value;
                    btn.Pressed += () =>
                    {
                        _gradeSpinBox.Value = capturedValue;
                    };
                    presetRow.AddChild(btn);
                }

                content.AddChild(presetRow);

                EditorUIUtils.AddSeparator(content, 4);

                var switchbackRow = new HBoxContainer();
                switchbackRow.AddThemeConstantOverride("separation", 8);

                var switchbackCheck = new CheckBox
                {
                    Text = "Allow Switchback Generation",
                    ButtonPressed = layer.AllowSwitchbackGeneration,
                    TooltipText = "When enabled, the tool can add zigzag points to steep segments.\n" +
                                 "When disabled, only existing point heights are adjusted."
                };
                switchbackCheck.Toggled += (enabled) =>
                {
                    var l = CurrentLayer;
                    if (l != null)
                    {
                        l.AllowSwitchbackGeneration = enabled;
                        RefreshConstraintStatus();
                    }
                };
                switchbackRow.AddChild(switchbackCheck);
                content.AddChild(switchbackRow);

                // Max switchback points (only show if switchbacks enabled)
                if (layer.AllowSwitchbackGeneration)
                {
                    var maxPointsRow = new HBoxContainer();
                    maxPointsRow.AddThemeConstantOverride("separation", 8);

                    maxPointsRow.AddChild(new Label
                    {
                        Text = "Max points to add:",
                        CustomMinimumSize = new Vector2(110, 0)
                    });

                    var maxPointsSpin = new SpinBox
                    {
                        MinValue = 2,
                        MaxValue = 50,
                        Step = 1,
                        Value = layer.MaxSwitchbackPoints,
                        CustomMinimumSize = new Vector2(70, 0),
                        TooltipText = "Maximum number of switchback points that can be added"
                    };
                    maxPointsSpin.ValueChanged += (value) =>
                    {
                        var l = CurrentLayer;
                        if (l != null)
                        {
                            l.MaxSwitchbackPoints = (int)value;
                            RefreshConstraintStatus();
                        }
                    };
                    maxPointsRow.AddChild(maxPointsSpin);
                    content.AddChild(maxPointsRow);
                }
            }

            if (showDownhill)
            {
                EditorUIUtils.AddSeparator(content, 4);

                var downhillCheck = new CheckBox
                {
                    Text = "Enforce Downhill Flow",
                    ButtonPressed = layer.EnableDownhillConstraint,
                    TooltipText = "Ensure water always flows downhill (no uphill segments)"
                };
                downhillCheck.Toggled += (enabled) =>
                {
                    var l = CurrentLayer;
                    if (l != null)
                    {
                        l.EnableDownhillConstraint = enabled;
                        RefreshConstraintStatus();
                    }
                };
                content.AddChild(downhillCheck);

                // Flow direction indicator
                var flowRow = new HBoxContainer();
                flowRow.AddThemeConstantOverride("separation", 8);

                flowRow.AddChild(new Label
                {
                    Text = "Flow Direction:",
                    CustomMinimumSize = new Vector2(100, 0)
                });

                string flowDir = layer.IsFlowDirectionForward() ? "Start → End" : "End → Start";
                _flowDirectionLabel = new Label { Text = flowDir };
                _flowDirectionLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.85f, 1.0f));
                flowRow.AddChild(_flowDirectionLabel);

                var (minElev, maxElev) = layer.GetElevationRange();
                _elevationRangeLabel = new Label
                {
                    Text = $"(Elevation: {minElev:F1}m to {maxElev:F1}m)"
                };
                _elevationRangeLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                flowRow.AddChild(_elevationRangeLabel);

                content.AddChild(flowRow);
            }
        }

        private void AddEnforcementControls(VBoxContainer content, PathLayer layer)
        {
            // Enforcement buttons
            var buttonRow = new HBoxContainer();
            buttonRow.AddThemeConstantOverride("separation", 8);

            _enforceButton = new Button
            {
                Text = "⚡ Enforce Constraints",
                TooltipText = "Automatically adjust point heights to satisfy all enabled constraints.\nMay add switchback points if needed.\nUse Ctrl+Z to undo.",
                CustomMinimumSize = new Vector2(160, 28)
            };
            _enforceButton.Pressed += OnEnforceConstraintsPressed;
            buttonRow.AddChild(_enforceButton);

            var refreshBtn = new Button
            {
                Text = "🔄",
                TooltipText = "Refresh constraint status",
                CustomMinimumSize = new Vector2(32, 28)
            };
            refreshBtn.Pressed += RefreshConstraintStatus;
            buttonRow.AddChild(refreshBtn);

            content.AddChild(buttonRow);
        }
        #endregion

        #region Constraint Status Updates
        private void RefreshConstraintStatus()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            // Update header warning indicators
            UpdateHeaderWarnings(layer);

            // Update status label
            if (IsInstanceValid(_constraintStatusLabel))
            {
                string status = layer.GetConstraintStatusSummary();
                _constraintStatusLabel.Text = status;

                // Color based on violations
                bool hasViolations = layer.HasConstraintViolations;
                _constraintStatusLabel.AddThemeColorOverride("font_color",
                    hasViolations ? new Color(1.0f, 0.7f, 0.3f) : new Color(0.5f, 1.0f, 0.5f));
            }

            // Update flow direction and elevation for waterways
            UpdateFlowDirectionDisplay(layer);

            // Update feasibility analysis
            UpdateFeasibilityDisplay(layer);

            // Update violation list
            RefreshViolationList();

            // Update enforce button state and text
            UpdateEnforceButton(layer);
        }

        private void UpdateHeaderWarnings(PathLayer layer)
        {
            // Update grade warning
            if (IsInstanceValid(_headerGradeWarning))
            {
                if (layer.EnableGradeConstraint)
                {
                    var gradeViolations = layer.GetGradeViolations();
                    if (gradeViolations.Count > 0)
                    {
                        _headerGradeWarning.Text = $"⚠ {gradeViolations.Count} grade";
                        _headerGradeWarning.TooltipText = $"{gradeViolations.Count} segment(s) exceed maximum grade of {layer.MaxGradePercent}%";
                        _headerGradeWarning.AddThemeColorOverride("font_color", new Color(1.0f, 0.7f, 0.3f));
                        _headerGradeWarning.Visible = true;
                    }
                    else
                    {
                        _headerGradeWarning.Text = "✓ grade";
                        _headerGradeWarning.TooltipText = $"All segments within {layer.MaxGradePercent}% grade limit";
                        _headerGradeWarning.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
                        _headerGradeWarning.Visible = true;
                    }
                }
                else
                {
                    _headerGradeWarning.Visible = false;
                }
            }

            // Update downhill warning
            if (IsInstanceValid(_headerDownhillWarning))
            {
                if (layer.EnableDownhillConstraint)
                {
                    var downhillViolations = layer.GetDownhillViolations();
                    if (downhillViolations.Count > 0)
                    {
                        _headerDownhillWarning.Text = $"⚠ {downhillViolations.Count} uphill";
                        _headerDownhillWarning.TooltipText = $"{downhillViolations.Count} point(s) flow uphill (should only flow downhill)";
                        _headerDownhillWarning.AddThemeColorOverride("font_color", new Color(1.0f, 0.7f, 0.3f));
                        _headerDownhillWarning.Visible = true;
                    }
                    else
                    {
                        string flowDir = layer.IsFlowDirectionForward() ? "→" : "←";
                        _headerDownhillWarning.Text = $"✓ flow {flowDir}";
                        _headerDownhillWarning.TooltipText = "All points flow downhill correctly";
                        _headerDownhillWarning.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
                        _headerDownhillWarning.Visible = true;
                    }
                }
                else
                {
                    _headerDownhillWarning.Visible = false;
                }
            }
        }

        private void UpdateFlowDirectionDisplay(PathLayer layer)
        {
            if (IsInstanceValid(_flowDirectionLabel))
            {
                string flowDir = layer.IsFlowDirectionForward() ? "Start → End" : "End → Start";
                _flowDirectionLabel.Text = flowDir;
            }

            if (IsInstanceValid(_elevationRangeLabel))
            {
                var (minElev, maxElev) = layer.GetElevationRange();
                _elevationRangeLabel.Text = $"(Elevation: {minElev:F1}m to {maxElev:F1}m)";
            }
        }

        private void UpdateFeasibilityDisplay(PathLayer layer)
        {
            if (!IsInstanceValid(_feasibilityContainer)) return;

            // Clear existing
            foreach (Node child in _feasibilityContainer.GetChildren())
            {
                child.QueueFree();
            }

            // Only show for grade constraints with violations
            if (!layer.EnableGradeConstraint) return;

            var violations = layer.GetGradeViolations();
            if (violations.Count == 0) return;

            var analysis = layer.AnalyzeGradeFeasibility();

            // Create analysis display panel
            var analysisPanel = new PanelContainer();
            var panelStyle = new StyleBoxFlat
            {
                BgColor = analysis.IsFeasible
                    ? new Color(0.2f, 0.3f, 0.2f, 0.5f)
                    : new Color(0.35f, 0.25f, 0.15f, 0.5f),
                CornerRadiusBottomLeft = 3,
                CornerRadiusBottomRight = 3,
                CornerRadiusTopLeft = 3,
                CornerRadiusTopRight = 3,
                ContentMarginLeft = 8,
                ContentMarginRight = 8,
                ContentMarginTop = 6,
                ContentMarginBottom = 6
            };
            analysisPanel.AddThemeStyleboxOverride("panel", panelStyle);

            var analysisVBox = new VBoxContainer();
            analysisVBox.AddThemeConstantOverride("separation", 4);

            // Header
            var headerLabel = new Label
            {
                Text = analysis.IsFeasible
                    ? "📊 Analysis: Can be solved by adjusting heights"
                    : "📊 Analysis: Needs switchback points"
            };
            headerLabel.AddThemeColorOverride("font_color",
                analysis.IsFeasible ? new Color(0.6f, 0.9f, 0.6f) : new Color(1.0f, 0.8f, 0.4f));
            headerLabel.AddThemeFontSizeOverride("font_size", 11);
            analysisVBox.AddChild(headerLabel);

            // Path metrics
            var metricsLabel = new Label
            {
                Text = $"Path: {analysis.TotalHorizontalDistance:F1}m horizontal, " +
                       $"{analysis.TotalVerticalDistance:F1}m vertical change",
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            metricsLabel.AddThemeFontSizeOverride("font_size", 10);
            metricsLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            analysisVBox.AddChild(metricsLabel);

            // Grade comparison
            var gradeLabel = new Label
            {
                Text = $"Current avg: {analysis.RequiredGradePercent:F1}% → " +
                       $"Max allowed: {layer.MaxGradePercent:F1}%"
            };
            gradeLabel.AddThemeFontSizeOverride("font_size", 10);
            gradeLabel.AddThemeColorOverride("font_color",
                analysis.RequiredGradePercent > layer.MaxGradePercent
                    ? new Color(1.0f, 0.6f, 0.4f)
                    : new Color(0.7f, 0.7f, 0.7f));
            analysisVBox.AddChild(gradeLabel);

            // Solution info (if needs switchbacks)
            if (!analysis.IsFeasible)
            {
                EditorUIUtils.AddSeparator(analysisVBox, 4);

                var solutionLabel = new Label
                {
                    Text = $"📍 Will add ~{analysis.SuggestedSwitchbacks} switchback point(s) to " +
                           $"create {analysis.AdditionalHorizontalNeeded:F1}m more horizontal distance",
                    AutowrapMode = TextServer.AutowrapMode.Word
                };
                solutionLabel.AddThemeFontSizeOverride("font_size", 10);
                solutionLabel.AddThemeColorOverride("font_color", new Color(0.9f, 0.8f, 0.5f));
                analysisVBox.AddChild(solutionLabel);
            }

            analysisPanel.AddChild(analysisVBox);
            _feasibilityContainer.AddChild(analysisPanel);
        }

        private void UpdateEnforceButton(PathLayer layer)
        {
            if (!IsInstanceValid(_enforceButton)) return;

            bool hasViolations = layer.HasConstraintViolations;
            _enforceButton.Disabled = !hasViolations;
            _enforceButton.Modulate = hasViolations ? Colors.White : new Color(1, 1, 1, 0.5f);

            if (hasViolations && layer.EnableGradeConstraint)
            {
                var analysis = layer.AnalyzeGradeFeasibility();
                if (!analysis.IsFeasible)
                {
                    _enforceButton.Text = $"⚡ Enforce (+{analysis.SuggestedSwitchbacks} pts)";
                    _enforceButton.TooltipText = $"Will add {analysis.SuggestedSwitchbacks} switchback point(s) " +
                                                  "and adjust heights to satisfy constraints.\nUse Ctrl+Z to undo.";
                }
                else
                {
                    _enforceButton.Text = "⚡ Enforce Constraints";
                    _enforceButton.TooltipText = "Adjust point heights to satisfy all enabled constraints.\nUse Ctrl+Z to undo.";
                }
            }
            else if (hasViolations && layer.EnableDownhillConstraint)
            {
                _enforceButton.Text = "⚡ Fix Flow Direction";
                _enforceButton.TooltipText = "Adjust point heights to ensure water flows downhill.\nUse Ctrl+Z to undo.";
            }
            else
            {
                _enforceButton.Text = "⚡ Enforce Constraints";
                _enforceButton.TooltipText = "No violations to fix.";
            }
        }

        private void RefreshViolationList()
        {
            if (!IsInstanceValid(_violationListContainer)) return;

            // Clear existing
            foreach (Node child in _violationListContainer.GetChildren())
            {
                child.QueueFree();
            }

            var layer = CurrentLayer;
            if (layer == null) return;

            bool hasAnyViolations = false;

            // Grade violations
            if (layer.EnableGradeConstraint)
            {
                var gradeViolations = layer.GetGradeViolations();
                if (gradeViolations.Count > 0)
                {
                    hasAnyViolations = true;

                    var header = new Label { Text = $"Grade Violations ({gradeViolations.Count}):" };
                    header.AddThemeColorOverride("font_color", new Color(1.0f, 0.6f, 0.3f));
                    header.AddThemeFontSizeOverride("font_size", 11);
                    _violationListContainer.AddChild(header);

                    // Show worst violations first
                    var sortedViolations = gradeViolations.OrderByDescending(v => v.ExcessGrade).Take(5);
                    foreach (var violation in sortedViolations)
                    {
                        var row = CreateViolationRow(
                            $"  Seg {violation.StartPointIndex}→{violation.EndPointIndex}",
                            $"{violation.ActualGradePercent:F1}% (+{violation.ExcessGrade:F1}% over limit)",
                            new Color(1.0f, 0.8f, 0.4f)
                        );
                        _violationListContainer.AddChild(row);
                    }

                    if (gradeViolations.Count > 5)
                    {
                        var moreLabel = new Label
                        {
                            Text = $"  ... and {gradeViolations.Count - 5} more"
                        };
                        moreLabel.AddThemeFontSizeOverride("font_size", 10);
                        moreLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                        _violationListContainer.AddChild(moreLabel);
                    }
                }
            }

            // Downhill violations
            if (layer.EnableDownhillConstraint)
            {
                var downhillViolations = layer.GetDownhillViolations();
                if (downhillViolations.Count > 0)
                {
                    hasAnyViolations = true;

                    if (layer.EnableGradeConstraint && layer.GetGradeViolations().Count > 0)
                    {
                        EditorUIUtils.AddSeparator(_violationListContainer, 4);
                    }

                    var header = new Label { Text = $"Uphill Violations ({downhillViolations.Count}):" };
                    header.AddThemeColorOverride("font_color", new Color(0.4f, 0.7f, 1.0f));
                    header.AddThemeFontSizeOverride("font_size", 11);
                    _violationListContainer.AddChild(header);

                    // Show worst violations first
                    var sortedViolations = downhillViolations.OrderByDescending(v => v.HeightIncrease).Take(5);
                    foreach (var violation in sortedViolations)
                    {
                        var row = CreateViolationRow(
                            $"  Point {violation.PointIndex}",
                            $"rises {violation.HeightIncrease:F2}m (from {violation.PreviousHeight:F1}m)",
                            new Color(0.6f, 0.85f, 1.0f)
                        );
                        _violationListContainer.AddChild(row);
                    }

                    if (downhillViolations.Count > 5)
                    {
                        var moreLabel = new Label
                        {
                            Text = $"  ... and {downhillViolations.Count - 5} more"
                        };
                        moreLabel.AddThemeFontSizeOverride("font_size", 10);
                        moreLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.6f, 0.6f));
                        _violationListContainer.AddChild(moreLabel);
                    }
                }
            }

            // No violations message
            if (!hasAnyViolations)
            {
                var noViolations = new Label
                {
                    Text = "✓ No violations detected",
                };
                noViolations.AddThemeColorOverride("font_color", new Color(0.5f, 0.9f, 0.5f));
                noViolations.AddThemeFontSizeOverride("font_size", 11);
                _violationListContainer.AddChild(noViolations);
            }
        }

        private HBoxContainer CreateViolationRow(string location, string details, Color detailColor)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var locationLabel = new Label
            {
                Text = location,
                CustomMinimumSize = new Vector2(100, 0)
            };
            locationLabel.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(locationLabel);

            var detailLabel = new Label { Text = details };
            detailLabel.AddThemeColorOverride("font_color", detailColor);
            detailLabel.AddThemeFontSizeOverride("font_size", 10);
            row.AddChild(detailLabel);

            return row;
        }
        #endregion

        #region Constraint Enforcement
        private void OnEnforceConstraintsPressed()
        {
            var layer = CurrentLayer;
            if (layer == null) return;

            var curve = layer.Curve;
            if (curve == null || curve.PointCount < 2) return;

            // Capture state for result reporting
            int originalPointCount = curve.PointCount;

            // Perform enforcement
            var result = layer.EnforceAllConstraints();

            // Log result
            GD.Print($"[PathLayer] Constraint enforcement: {result.Message}");

            // Show result notification
            if (result.PointsModified > 0 || result.PointsAdded > 0)
            {
                ShowEnforcementResult(result, originalPointCount);
            }

            // Refresh all displays
            RefreshConstraintStatus();
            RefreshCurveStats();
            UpdateStatLabels();
        }

        private void ShowEnforcementResult(PathLayer.EnforcementResult result, int originalPointCount)
        {
            // Build summary message
            var lines = new List<string>();

            if (result.Success)
            {
                lines.Add("✓ Constraints enforced successfully!");
            }
            else
            {
                lines.Add("⚠ Partial fix - some violations remain");
            }

            lines.Add($"Points modified: {result.PointsModified}");

            if (result.PointsAdded > 0)
            {
                lines.Add($"Switchback points added: {result.PointsAdded}");
                lines.Add($"Total points: {originalPointCount} → {originalPointCount + result.PointsAdded}");
            }

            lines.Add("");
            lines.Add("Use Edit → Undo (Ctrl+Z) to revert.");

            // Show dialog
            var dialog = new AcceptDialog
            {
                Title = result.Success ? "Constraints Enforced" : "Partial Enforcement",
                DialogText = string.Join("\n", lines),
                Size = new Vector2I(350, 200)
            };

            EditorInterface.Singleton.GetBaseControl().AddChild(dialog);
            dialog.PopupCentered();

            dialog.Confirmed += () => dialog.QueueFree();
            dialog.Canceled += () => dialog.QueueFree();

            // Also log details if available
            if (result.Details?.Count > 0)
            {
                GD.Print("[PathLayer] Enforcement details:");
                foreach (var detail in result.Details.Take(10))
                {
                    GD.Print($"  {detail}");
                }
                if (result.Details.Count > 10)
                {
                    GD.Print($"  ... and {result.Details.Count - 10} more");
                }
            }
        }
        #endregion
    }
}