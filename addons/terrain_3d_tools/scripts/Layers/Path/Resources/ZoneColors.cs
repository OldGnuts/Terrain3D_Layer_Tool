// /Layers/Path/Resources/ZoneColors.cs
using Godot;

namespace Terrain3DTools.Layers.Path
{
    /// <summary>
    /// Centralized zone color definitions used across viewport visualization,
    /// inspector UI, and profile previews.
    /// </summary>
    public static class ZoneColors
    {
        /// <summary>
        /// Context determines alpha and slight color adjustments
        /// </summary>
        public enum ColorContext
        {
            /// <summary>3D viewport visualization (higher alpha for visibility)</summary>
            Viewport,
            /// <summary>Inspector buttons and UI elements</summary>
            Inspector,
            /// <summary>Profile cross-section preview</summary>
            Preview
        }

        /// <summary>
        /// Get the display color for a zone type.
        /// </summary>
        public static Color GetColor(ZoneType type, ColorContext context = ColorContext.Inspector)
        {
            float alpha = context switch
            {
                ColorContext.Viewport => 0.85f,
                ColorContext.Inspector => 0.7f,
                ColorContext.Preview => 0.7f,
                _ => 0.7f
            };

            Color baseColor = type switch
            {
                ZoneType.Center => new Color(0.25f, 0.65f, 1.0f, alpha),      // Blue
                ZoneType.Inner => new Color(0.35f, 0.55f, 0.95f, alpha),      // Light blue
                ZoneType.Shoulder => new Color(0.25f, 0.85f, 0.4f, alpha),    // Green
                ZoneType.Edge => new Color(0.55f, 0.75f, 0.3f, alpha),        // Yellow-green
                ZoneType.Wall => new Color(0.85f, 0.45f, 0.2f, alpha),        // Orange
                ZoneType.Rim => new Color(0.85f, 0.65f, 0.2f, alpha),         // Gold
                ZoneType.Slope => new Color(0.65f, 0.5f, 0.35f, alpha),       // Brown
                ZoneType.Transition => new Color(0.5f, 0.5f, 0.5f, alpha * 0.7f), // Gray (more transparent)
                _ => new Color(0.45f, 0.45f, 0.45f, alpha)
            };

            return baseColor;
        }

        /// <summary>
        /// Get color array for indexed access (useful for shaders/previews).
        /// Returns colors for zones 0-7.
        /// </summary>
        public static Color[] GetColorPalette(ColorContext context = ColorContext.Preview)
        {
            return new Color[]
            {
                GetColor(ZoneType.Center, context),
                GetColor(ZoneType.Shoulder, context),
                GetColor(ZoneType.Edge, context),
                new Color(0.8f, 0.2f, 0.6f, 0.7f),   // Pink (fallback)
                new Color(0.6f, 0.4f, 0.8f, 0.7f),   // Purple (fallback)
                new Color(0.4f, 0.8f, 0.8f, 0.7f),   // Cyan (fallback)
                new Color(0.8f, 0.8f, 0.4f, 0.7f),   // Yellow (fallback)
                new Color(0.6f, 0.6f, 0.6f, 0.7f),   // Gray (fallback)
            };
        }

        /// <summary>
        /// Get a dimmed version of a zone color (for disabled zones).
        /// </summary>
        public static Color GetDisabledColor(ZoneType type, ColorContext context = ColorContext.Inspector)
        {
            var color = GetColor(type, context);
            return color with { A = color.A * 0.4f };
        }

        /// <summary>
        /// Get a highlighted version of a zone color (for selection/hover).
        /// </summary>
        public static Color GetHighlightedColor(ZoneType type, ColorContext context = ColorContext.Inspector)
        {
            var color = GetColor(type, context);
            return color.Lightened(0.2f) with { A = Mathf.Min(1f, color.A + 0.15f) };
        }
    }
}