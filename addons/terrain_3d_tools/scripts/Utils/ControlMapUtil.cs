using System;
using Godot;

namespace Terrain3DTools.Utils
{
    public static class ControlMapUtil
    {
        // Bit shifts
        private const int BASE_ID_SHIFT    = 27;
        private const int OVERLAY_ID_SHIFT = 22;
        private const int BLEND_SHIFT      = 14;
        private const int ANGLE_SHIFT      = 10;
        private const int SCALE_SHIFT      = 7;
        private const int HOLE_SHIFT       = 2;
        private const int NAV_SHIFT        = 1;
        private const int AUTO_SHIFT       = 0;

        // Masks
        private const uint BASE_ID_MASK    = 0x1Fu;
        private const uint OVERLAY_ID_MASK = 0x1Fu;
        private const uint BLEND_MASK      = 0xFFu;
        private const uint ANGLE_MASK      = 0xFu;
        private const uint SCALE_MASK      = 0x7u;
        private const uint HOLE_MASK       = 0x1u;
        private const uint NAV_MASK        = 0x1u;
        private const uint AUTO_MASK       = 0x1u;

        /// <summary>
        /// Encode a control map pixel into a packed 32-bit uint.
        /// </summary>
        public static uint Encode(
            uint baseId, uint overlayId, uint blend,
            uint angle, uint scale, uint hole, uint nav, uint autoFlag)
        {
            return ((baseId   & BASE_ID_MASK)    << BASE_ID_SHIFT)   |
                   ((overlayId & OVERLAY_ID_MASK) << OVERLAY_ID_SHIFT) |
                   ((blend    & BLEND_MASK)      << BLEND_SHIFT)    |
                   ((angle    & ANGLE_MASK)      << ANGLE_SHIFT)    |
                   ((scale    & SCALE_MASK)      << SCALE_SHIFT)    |
                   ((hole     & HOLE_MASK)       << HOLE_SHIFT)     |
                   ((nav      & NAV_MASK)        << NAV_SHIFT)      |
                   ((autoFlag & AUTO_MASK)       << AUTO_SHIFT);
        }

        /// <summary>
        /// Decode a control map pixel (packed uint).
        /// Always returns *actual field values* stored in the map.
        /// </summary>
        public static void Decode(uint packed,
            out uint baseId, out uint overlayId, out uint blend,
            out uint angle, out uint scale, out uint hole, out uint nav, out uint autoFlag)
        {
            baseId    = (packed >> BASE_ID_SHIFT)    & BASE_ID_MASK;
            overlayId = (packed >> OVERLAY_ID_SHIFT) & OVERLAY_ID_MASK;
            blend     = (packed >> BLEND_SHIFT)      & BLEND_MASK;
            angle     = (packed >> ANGLE_SHIFT)      & ANGLE_MASK;
            scale     = (packed >> SCALE_SHIFT)      & SCALE_MASK;
            hole      = (packed >> HOLE_SHIFT)       & HOLE_MASK;
            nav       = (packed >> NAV_SHIFT)        & NAV_MASK;
            autoFlag  = (packed)                     & AUTO_MASK;
        }

        /// <summary>
        /// Debug human-readable string for a *decoded pixel* (actual values).
        /// </summary>
        public static string ToDebugString(uint packed)
        {
            Decode(packed, out var baseId, out var overlayId, out var blend,
                          out var angle, out var scale, out var hole,
                          out var nav, out var autoFlag);

            return $"ControlPixel[ Base={baseId}, Overlay={overlayId}, Blend={blend}, " +
                   $"Angle={angle}, Scale={scale}, Hole={hole}, Nav={nav}, Auto={autoFlag} ]";
        }

        /// <summary>
        /// Debug string for push constant parameters.
        /// Interprets 0xFFFFFFFF as "Preserve" for UV/flags.
        /// </summary>
        public static string PushConstantDebug(
            uint baseId, uint overlayId, uint blendTarget,
            float strength,
            uint uvAngle, uint uvScale,
            uint hole, uint nav, uint autoFlag)
        {
            string formatField(uint val, string label) =>
                val == 0xFFFFFFFFu ? $"{label}=Preserve" : $"{label}={val}";

            return "TexturePush[" +
                   $"Base={baseId}, Overlay={overlayId}, BlendTarget={blendTarget}, Strength={strength}, " +
                   $"{formatField(uvAngle,"Angle")}, {formatField(uvScale,"Scale")}, " +
                   $"{formatField(hole,"Hole")}, {formatField(nav,"Nav")}, {formatField(autoFlag,"Auto")} ]";
        }

        // Float/uint reinterpret conversions
        public static uint FloatToUint(float val)
        {
            return BitConverter.ToUInt32(BitConverter.GetBytes(val), 0);
        }

        public static float UintToFloat(uint val)
        {
            return BitConverter.ToSingle(BitConverter.GetBytes(val), 0);
        }
    }
}