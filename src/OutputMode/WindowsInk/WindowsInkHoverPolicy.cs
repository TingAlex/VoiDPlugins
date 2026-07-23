namespace VoiDPlugins.OutputMode
{
    public static class WindowsInkHoverPolicy
    {
        public static bool ShouldReportAsInk(bool touchpadFriendlyHover, bool tipPressed)
        {
            return !touchpadFriendlyHover || tipPressed;
        }

        public static bool ShouldKeepInRangeAfterButtonRelease(
            bool touchpadFriendlyHover,
            bool tipPressed,
            bool barrelPressed)
        {
            return !touchpadFriendlyHover || tipPressed || barrelPressed;
        }
    }
}
