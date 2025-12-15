using Godot;
/// <summary>
    /// Helper extension methods for safe Variant handling
    /// </summary>
    public static class VariantExtensions
    {
        public static string SafeAsString(this Variant variant, string fallback = "")
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsString() : fallback;
        }

        public static bool SafeAsBool(this Variant variant, bool fallback = false)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsBool() : fallback;
        }

        public static float SafeAsSingle(this Variant variant, float fallback = 0f)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsSingle() : fallback;
        }

        public static int SafeAsInt32(this Variant variant, int fallback = 0)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsInt32() : fallback;
        }

        public static uint SafeAsUInt32(this Variant variant, uint fallback = 0)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsUInt32() : fallback;
        }

        public static Vector3 SafeAsVector3(this Variant variant, Vector3 fallback = default)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsVector3() : fallback;
        }

        public static Vector2 SafeAsVector2(this Variant variant, Vector2 fallback = default)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsVector2() : fallback;
        }

        public static Vector2I SafeAsVector2I(this Variant variant, Vector2I fallback = default)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsVector2I() : fallback;
        }

        public static Color SafeAsColor(this Variant variant, Color fallback = default)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsColor() : fallback;
        }

        public static Aabb SafeAsAabb(this Variant variant, Aabb fallback = default)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.As<Aabb>() : fallback;
        }

        public static GodotObject SafeAsGodotObject(this Variant variant)
        {
            return variant.VariantType != Variant.Type.Nil ? variant.AsGodotObject() : null;
        }

        public static T SafeAs<[MustBeVariant] T>(this Variant variant, T fallback = default) where T : class
        {
            return variant.VariantType != Variant.Type.Nil ? variant.As<T>() : fallback;
        }

        public static T SafeAsGodotObject<T>(this Variant variant, T fallback = null) where T : GodotObject
        {
            if (variant.VariantType == Variant.Type.Nil)
                return fallback;

            var obj = variant.AsGodotObject();
            return obj is T result ? result : fallback;
        }
    }