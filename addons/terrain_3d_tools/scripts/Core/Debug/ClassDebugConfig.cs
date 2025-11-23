using Godot;
using System.Linq;

namespace Terrain3DTools.Core.Debug
{
    /// <summary>
    /// Configuration resource for a single class's debug settings.
    /// This is what appears in the inspector as an array element.
    /// </summary>
    [GlobalClass, Tool]
    public partial class ClassDebugConfig : Resource
    {
        public ClassDebugConfig()
        {
            ResourceLocalToScene = true;
        }
        private string _className = "";
        private bool _enabled = true;
        private DebugCategory _enabledCategories = DebugCategory.None;

        public string ClassName
        {
            get => _className;
            set
            {
                _className = value;
                NotifyPropertyListChanged();
            }
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                NotifyPropertyListChanged();
            }
        }

        public DebugCategory EnabledCategories
        {
            get => _enabledCategories;
            set
            {
                _enabledCategories = value;
                NotifyPropertyListChanged();
            }
        }

        // Helper constructor for code
        public ClassDebugConfig(string className)
        {
            _className = className;
        }

        public bool IsCategoryEnabled(DebugCategory category)
        {
            if (!_enabled) return false;
            return (_enabledCategories & category) != 0;
        }

        // This method is called by Godot to allow custom property validation/hints
        public override Godot.Collections.Array<Godot.Collections.Dictionary> _GetPropertyList()
        {
            var properties = new Godot.Collections.Array<Godot.Collections.Dictionary>();

            // Get registered classes from DebugManager
            string enumHint = DebugManager.GetRegisteredClassesForHint();

            // Create property dictionary for ClassName with dropdown
            var classNameProp = new Godot.Collections.Dictionary
            {
                { "name", "ClassName" },
                { "type", (int)Variant.Type.String },
                { "usage", (int)PropertyUsageFlags.Default },
                { "hint", (int)PropertyHint.Enum },
                { "hint_string", enumHint }
            };
            properties.Add(classNameProp);

            // Add Enabled property
            var enabledProp = new Godot.Collections.Dictionary
            {
                { "name", "Enabled" },
                { "type", (int)Variant.Type.Bool },
                { "usage", (int)PropertyUsageFlags.Default }
            };
            properties.Add(enabledProp);

            // Add EnabledCategories with flags
            var categoriesProp = new Godot.Collections.Dictionary
            {
                { "name", "EnabledCategories" },
                { "type", (int)Variant.Type.Int },
                { "usage", (int)PropertyUsageFlags.Default },
                { "hint", (int)PropertyHint.Flags },
                { "hint_string", GetDebugCategoryFlagsString() }
            };
            properties.Add(categoriesProp);

            return properties;
        }

        // Get/Set for custom properties
        public override Variant _Get(StringName property)
        {
            string propName = property.ToString();

            return propName switch
            {
                "ClassName" => _className,
                "Enabled" => _enabled,
                "EnabledCategories" => (int)_enabledCategories,
                _ => default
            };
        }

        public override bool _Set(StringName property, Variant value)
        {
            string propName = property.ToString();

            switch (propName)
            {
                case "ClassName":
                    _className = value.AsString();
                    NotifyPropertyListChanged();
                    return true;
                case "Enabled":
                    _enabled = value.AsBool();
                    NotifyPropertyListChanged();
                    return true;
                case "EnabledCategories":
                    _enabledCategories = (DebugCategory)value.AsInt32();
                    NotifyPropertyListChanged();
                    return true;
                default:
                    return false;
            }
        }

        private string GetDebugCategoryFlagsString()
        {
            // Generate flags string from DebugCategory enum, sorting by value
            var categories = System.Enum.GetValues(typeof(DebugCategory))
                .Cast<DebugCategory>()
                .Where(c => c != DebugCategory.None)
                .Where(c => (c & (c - 1)) == 0)
                .OrderBy(c => (uint)c)
                .Select(c => c.ToString());
            string hintString = string.Join(",", categories);
            GD.Print($"Generated DebugCategory Flags String: {hintString}");

            return hintString;
        }
    }
}
