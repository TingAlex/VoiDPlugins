using VoiDPlugins.OutputMode;
using Xunit;

namespace WindowsInk.Tests
{
    public class WindowsInkHoverPolicyTests
    {
        [Theory]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, true, true)]
        public void ReportsInkOnlyForContactInTouchpadFriendlyMode(
            bool touchpadFriendlyHover,
            bool tipPressed,
            bool expected)
        {
            Assert.Equal(
                expected,
                WindowsInkHoverPolicy.ShouldReportAsInk(touchpadFriendlyHover, tipPressed));
        }

        [Theory]
        [InlineData(false, false, false, true)]
        [InlineData(true, false, false, false)]
        [InlineData(true, true, false, true)]
        [InlineData(true, false, true, true)]
        public void KeepsInRangeForActiveInkButtons(
            bool touchpadFriendlyHover,
            bool tipPressed,
            bool barrelPressed,
            bool expected)
        {
            Assert.Equal(
                expected,
                WindowsInkHoverPolicy.ShouldKeepInRangeAfterButtonRelease(
                    touchpadFriendlyHover,
                    tipPressed,
                    barrelPressed));
        }
    }
}
