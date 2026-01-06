// /Editor/EditorConstants.cs
namespace Terrain3DTools.Editor
{
    /// <summary>
    /// Shared constants used across editor UI components.
    /// Centralizes magic numbers to avoid duplication and ensure consistency.
    /// </summary>
    public static class EditorConstants
    {
        #region Thumbnail Sizes
        public const int THUMBNAIL_SIZE = 48;
        public const int THUMBNAIL_SIZE_SMALL = 32;
        public const int THUMBNAIL_SIZE_LARGE = 64;
        #endregion

        #region Grid Layouts
        public const int TEXTURE_GRID_COLUMNS = 6;
        public const int ZONE_BUTTON_MIN_WIDTH = 60;
        public const int ZONE_BUTTON_MIN_HEIGHT = 32;
        #endregion

        #region Preview Dimensions
        public const float PROFILE_PREVIEW_HEIGHT = 120f;
        public const float CURVE_PREVIEW_HEIGHT = 40f;
        public const float POINT_LIST_HEIGHT = 120f;
        #endregion

        #region Spacing
        public const int SECTION_SPACING = 8;
        public const int ITEM_SPACING = 4;
        public const int LABEL_MIN_WIDTH = 100;
        #endregion

        #region Curve Editor
        public const float CURVE_POINT_RADIUS = 6f;
        public const float CURVE_HANDLE_RADIUS = 4f;
        public const float CURVE_EDITOR_PADDING = 30f;
        #endregion
    }
}