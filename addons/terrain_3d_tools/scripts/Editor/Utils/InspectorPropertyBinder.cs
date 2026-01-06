// /Editor/Utils/InspectorPropertyBinder.cs
using System;
using System.Collections.Generic;
using Godot;

namespace Terrain3DTools.Editor.Utils
{
    /// <summary>
    /// Provides a declarative way to bind UI controls to object properties.
    /// Reduces boilerplate in custom inspectors by handling:
    /// - Control creation with consistent styling
    /// - Two-way binding (property → control, control → property)
    /// - Automatic refresh when properties change externally
    /// - Proper cleanup
    /// 
    /// Usage:
    ///   var binder = new InspectorPropertyBinder();
    ///   binder.AddSlider(parent, "Strength", () => layer.Strength, v => layer.Strength = v, 0, 1);
    ///   // Later:
    ///   binder.RefreshAll();
    ///   binder.Clear();
    /// </summary>
    public class InspectorPropertyBinder
    {
        #region Binding Records

        private abstract class BindingBase
        {
            public abstract void Refresh();
            public abstract void Cleanup();
        }

        private class SliderBinding : BindingBase
        {
            public HSlider Slider;
            public Label ValueLabel;
            public Func<float> Getter;
            public string Format;

            public override void Refresh()
            {
                if (Slider != null && IsInstanceValid(Slider))
                {
                    Slider.SetValueNoSignal(Getter());
                }
                if (ValueLabel != null && IsInstanceValid(ValueLabel))
                {
                    ValueLabel.Text = Getter().ToString(Format);
                }
            }

            public override void Cleanup()
            {
                Slider = null;
                ValueLabel = null;
            }

            private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
        }

        private class SpinBoxBinding : BindingBase
        {
            public SpinBox SpinBox;
            public Func<int> Getter;

            public override void Refresh()
            {
                if (SpinBox != null && IsInstanceValid(SpinBox))
                {
                    SpinBox.SetValueNoSignal(Getter());
                }
            }

            public override void Cleanup()
            {
                SpinBox = null;
            }

            private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
        }

        private class CheckBoxBinding : BindingBase
        {
            public CheckBox CheckBox;
            public Func<bool> Getter;

            public override void Refresh()
            {
                if (CheckBox != null && IsInstanceValid(CheckBox))
                {
                    CheckBox.SetPressedNoSignal(Getter());
                }
            }

            public override void Cleanup()
            {
                CheckBox = null;
            }

            private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
        }

        private class DropdownBinding : BindingBase
        {
            public OptionButton Dropdown;
            public Func<int> Getter;

            public override void Refresh()
            {
                if (Dropdown != null && IsInstanceValid(Dropdown))
                {
                    Dropdown.Selected = Getter();
                }
            }

            public override void Cleanup()
            {
                Dropdown = null;
            }

            private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
        }

        private class LabelBinding : BindingBase
        {
            public Label Label;
            public Func<string> Getter;

            public override void Refresh()
            {
                if (Label != null && IsInstanceValid(Label))
                {
                    Label.Text = Getter();
                }
            }

            public override void Cleanup()
            {
                Label = null;
            }

            private static bool IsInstanceValid(GodotObject obj) => GodotObject.IsInstanceValid(obj);
        }

        #endregion

        #region Fields

        private readonly List<BindingBase> _bindings = new();

        #endregion

        #region Slider Methods

        /// <summary>
        /// Adds a labeled slider with value display.
        /// </summary>
        public HSlider AddSlider(
            Control parent,
            string label,
            Func<float> getter,
            Action<float> setter,
            float min,
            float max,
            float step = 0.01f,
            string format = "F2",
            string tooltip = null,
            string leftLabel = null,
            string rightLabel = null)
        {
            var container = new VBoxContainer();
            container.AddThemeConstantOverride("separation", 2);

            // Label row
            var labelRow = new HBoxContainer();
            labelRow.AddThemeConstantOverride("separation", 4);

            var nameLabel = new Label
            {
                Text = label,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };
            labelRow.AddChild(nameLabel);

            var valueLabel = new Label
            {
                Text = getter().ToString(format),
                CustomMinimumSize = new Vector2(45, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = new Color(0.8f, 0.8f, 0.8f)
            };
            labelRow.AddChild(valueLabel);

            container.AddChild(labelRow);

            // Slider row
            var sliderRow = new HBoxContainer();
            sliderRow.AddThemeConstantOverride("separation", 4);

            if (!string.IsNullOrEmpty(leftLabel))
            {
                var left = new Label
                {
                    Text = leftLabel,
                    Modulate = new Color(0.6f, 0.6f, 0.6f)
                };
                left.AddThemeFontSizeOverride("font_size", 11);
                sliderRow.AddChild(left);
            }

            var slider = new HSlider
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = getter(),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tooltip ?? ""
            };

            slider.ValueChanged += (value) =>
            {
                setter((float)value);
                valueLabel.Text = ((float)value).ToString(format);
            };

            sliderRow.AddChild(slider);

            if (!string.IsNullOrEmpty(rightLabel))
            {
                var right = new Label
                {
                    Text = rightLabel,
                    Modulate = new Color(0.6f, 0.6f, 0.6f)
                };
                right.AddThemeFontSizeOverride("font_size", 11);
                sliderRow.AddChild(right);
            }

            container.AddChild(sliderRow);
            parent.AddChild(container);

            // Register binding
            _bindings.Add(new SliderBinding
            {
                Slider = slider,
                ValueLabel = valueLabel,
                Getter = getter,
                Format = format
            });

            return slider;
        }

        /// <summary>
        /// Adds a slider inline within an existing HBoxContainer (no separate label row).
        /// </summary>
        public void AddSliderInline(
            HBoxContainer parent,
            string label,
            Func<float> getter,
            Action<float> setter,
            float min,
            float max,
            float step,
            string format = "F2")
        {
            var labelNode = new Label
            {
                Text = label,
                CustomMinimumSize = new Vector2(130, 0),
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin
            };
            parent.AddChild(labelNode);

            var slider = new HSlider
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = getter(),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(80, 0)
            };

            var valueLabel = new Label
            {
                Text = getter().ToString(format),
                CustomMinimumSize = new Vector2(40, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                Modulate = new Color(0.8f, 0.8f, 0.8f)
            };

            slider.ValueChanged += (value) =>
            {
                setter((float)value);
                valueLabel.Text = ((float)value).ToString(format);
            };

            parent.AddChild(slider);
            parent.AddChild(valueLabel);

            // Register binding using existing pattern
            _bindings.Add(new SliderBinding
            {
                Slider = slider,
                ValueLabel = valueLabel,
                Getter = getter,
                Format = format
            });
        }

        /// <summary>
        /// Adds a compact slider without the separate label row.
        /// </summary>
        public HSlider AddCompactSlider(
            Control parent,
            string label,
            Func<float> getter,
            Action<float> setter,
            float min,
            float max,
            float step = 0.01f,
            string format = "F2")
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            row.AddChild(new Label
            {
                Text = label + ":",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var slider = new HSlider
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = getter(),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
            };

            var valueLabel = new Label
            {
                Text = getter().ToString(format),
                CustomMinimumSize = new Vector2(45, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };

            slider.ValueChanged += (value) =>
            {
                setter((float)value);
                valueLabel.Text = ((float)value).ToString(format);
            };

            row.AddChild(slider);
            row.AddChild(valueLabel);
            parent.AddChild(row);

            _bindings.Add(new SliderBinding
            {
                Slider = slider,
                ValueLabel = valueLabel,
                Getter = getter,
                Format = format
            });

            return slider;
        }

        #endregion

        #region SpinBox Methods

        /// <summary>
        /// Adds an integer spin box.
        /// </summary>
        public SpinBox AddSpinBox(
            Control parent,
            string label,
            Func<int> getter,
            Action<int> setter,
            int min,
            int max,
            int step = 1,
            string tooltip = null)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            row.AddChild(new Label
            {
                Text = label + ":",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var spinBox = new SpinBox
            {
                MinValue = min,
                MaxValue = max,
                Step = step,
                Value = getter(),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tooltip ?? ""
            };

            spinBox.ValueChanged += (value) => setter((int)value);

            row.AddChild(spinBox);
            parent.AddChild(row);

            _bindings.Add(new SpinBoxBinding
            {
                SpinBox = spinBox,
                Getter = getter
            });

            return spinBox;
        }

        /// <summary>
        /// Adds a spin box with a randomize button.
        /// </summary>
        public SpinBox AddSpinBoxWithRandomize(
            Control parent,
            string label,
            Func<int> getter,
            Action<int> setter,
            int min = int.MinValue,
            int max = int.MaxValue,
            string tooltip = null)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            row.AddChild(new Label
            {
                Text = label + ":",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var spinBox = new SpinBox
            {
                MinValue = min,
                MaxValue = max,
                Value = getter(),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tooltip ?? ""
            };

            spinBox.ValueChanged += (value) => setter((int)value);

            row.AddChild(spinBox);

            var randomBtn = new Button
            {
                Text = "🎲",
                TooltipText = "Randomize"
            };
            randomBtn.Pressed += () =>
            {
                int newValue = (int)(GD.Randi() % 100000);
                setter(newValue);
                spinBox.SetValueNoSignal(newValue);
            };
            row.AddChild(randomBtn);

            parent.AddChild(row);

            _bindings.Add(new SpinBoxBinding
            {
                SpinBox = spinBox,
                Getter = getter
            });

            return spinBox;
        }

        #endregion

        #region CheckBox Methods

        /// <summary>
        /// Adds a checkbox toggle.
        /// </summary>
        public CheckBox AddCheckBox(
            Control parent,
            string label,
            Func<bool> getter,
            Action<bool> setter,
            string tooltip = null,
            Action<bool> onChanged = null)
        {
            var checkBox = new CheckBox
            {
                Text = label,
                ButtonPressed = getter(),
                TooltipText = tooltip ?? ""
            };

            checkBox.Toggled += (pressed) =>
            {
                setter(pressed);
                onChanged?.Invoke(pressed);
            };

            parent.AddChild(checkBox);

            _bindings.Add(new CheckBoxBinding
            {
                CheckBox = checkBox,
                Getter = getter
            });

            return checkBox;
        }

        #endregion

        #region Dropdown Methods

        /// <summary>
        /// Adds an enum dropdown.
        /// </summary>
        public OptionButton AddEnumDropdown<T>(
            Control parent,
            string label,
            Func<T> getter,
            Action<T> setter,
            string tooltip = null) where T : struct, Enum
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            row.AddChild(new Label
            {
                Text = label + ":",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var dropdown = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tooltip ?? ""
            };

            foreach (T value in Enum.GetValues(typeof(T)))
            {
                dropdown.AddItem(FormatEnumName(value.ToString()), Convert.ToInt32(value));
            }

            dropdown.Selected = Convert.ToInt32(getter());
            dropdown.ItemSelected += (index) => setter((T)(object)(int)index);

            row.AddChild(dropdown);
            parent.AddChild(row);

            _bindings.Add(new DropdownBinding
            {
                Dropdown = dropdown,
                Getter = () => Convert.ToInt32(getter())
            });

            return dropdown;
        }

        /// <summary>
        /// Adds a custom dropdown with specified items.
        /// </summary>
        public OptionButton AddDropdown(
            Control parent,
            string label,
            string[] items,
            Func<int> getter,
            Action<int> setter,
            string tooltip = null)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            row.AddChild(new Label
            {
                Text = label + ":",
                CustomMinimumSize = new Vector2(EditorConstants.LABEL_MIN_WIDTH, 0)
            });

            var dropdown = new OptionButton
            {
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                TooltipText = tooltip ?? ""
            };

            for (int i = 0; i < items.Length; i++)
            {
                dropdown.AddItem(items[i], i);
            }

            dropdown.Selected = getter();
            dropdown.ItemSelected += (index) => setter((int)index);

            row.AddChild(dropdown);
            parent.AddChild(row);

            _bindings.Add(new DropdownBinding
            {
                Dropdown = dropdown,
                Getter = getter
            });

            return dropdown;
        }

        #endregion

        #region Label Methods

        /// <summary>
        /// Adds a dynamic label that updates on refresh.
        /// </summary>
        public Label AddDynamicLabel(
            Control parent,
            Func<string> getter,
            Color? color = null,
            int? fontSize = null)
        {
            var label = new Label
            {
                Text = getter(),
                AutowrapMode = TextServer.AutowrapMode.Word
            };

            if (color.HasValue)
            {
                label.Modulate = color.Value;
            }

            if (fontSize.HasValue)
            {
                label.AddThemeFontSizeOverride("font_size", fontSize.Value);
            }

            parent.AddChild(label);

            _bindings.Add(new LabelBinding
            {
                Label = label,
                Getter = getter
            });

            return label;
        }

        /// <summary>
        /// Adds a static description label (not bound).
        /// </summary>
        public Label AddDescription(Control parent, string text)
        {
            var label = new Label
            {
                Text = text,
                Modulate = new Color(0.6f, 0.6f, 0.6f),
                AutowrapMode = TextServer.AutowrapMode.Word
            };
            label.AddThemeFontSizeOverride("font_size", 11);
            parent.AddChild(label);
            return label;
        }

        /// <summary>
        /// Adds a section header label.
        /// </summary>
        public Label AddSectionHeader(Control parent, string text, Color? color = null)
        {
            var label = new Label
            {
                Text = text,
                Modulate = color ?? new Color(0.9f, 0.9f, 0.9f)
            };
            label.AddThemeFontSizeOverride("font_size", 13);
            parent.AddChild(label);
            return label;
        }

        #endregion

        #region Layout Helpers

        /// <summary>
        /// Adds a horizontal separator.
        /// </summary>
        public void AddSeparator(Control parent, int height = 8)
        {
            var sep = new HSeparator
            {
                CustomMinimumSize = new Vector2(0, height)
            };
            parent.AddChild(sep);
        }

        /// <summary>
        /// Adds vertical spacing.
        /// </summary>
        public void AddSpacing(Control parent, int height = 8)
        {
            var spacer = new Control
            {
                CustomMinimumSize = new Vector2(0, height)
            };
            parent.AddChild(spacer);
        }

        /// <summary>
        /// Creates a margin container for indenting content.
        /// </summary>
        public MarginContainer CreateIndent(Control parent, int leftMargin = 16)
        {
            var margin = new MarginContainer();
            margin.AddThemeConstantOverride("margin_left", leftMargin);
            parent.AddChild(margin);
            return margin;
        }

        #endregion

        #region Refresh and Cleanup

        /// <summary>
        /// Refreshes all bound controls from their property getters.
        /// </summary>
        public void RefreshAll()
        {
            foreach (var binding in _bindings)
            {
                binding.Refresh();
            }
        }

        /// <summary>
        /// Clears all bindings and releases references.
        /// </summary>
        public void Clear()
        {
            foreach (var binding in _bindings)
            {
                binding.Cleanup();
            }
            _bindings.Clear();
        }

        /// <summary>
        /// Gets the number of active bindings.
        /// </summary>
        public int BindingCount => _bindings.Count;

        #endregion

        #region Utility

        private static string FormatEnumName(string name)
        {
            // Convert PascalCase to "Pascal Case"
            var result = new System.Text.StringBuilder();
            foreach (char c in name)
            {
                if (char.IsUpper(c) && result.Length > 0)
                {
                    result.Append(' ');
                }
                result.Append(c);
            }
            return result.ToString();
        }

        #endregion
    }
}