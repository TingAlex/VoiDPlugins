using System;
using System.Numerics;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Tablet;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed class PrecisionControlTests
    {
        [Fact]
        public void GlobalHotkeyWorksWhenPenIsOutOfRange()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;

            filter.Consume(new TestTabletReport(new Vector2(90, 90)));
            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            Assert.Equal(1, overlay.ShowCount);
            AssertBounds(overlay.Bounds, -6, 36, 960, 540);
        }

        [Fact]
        public void GlobalHotkeyTogglesPrecisionWhilePenIsInRange()
        {
            using var filter = CreateFilter(out _, out var overlay);
            var lastPosition = Vector2.Zero;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            Assert.Equal(1, overlay.ShowCount);
            AssertBounds(overlay.Bounds, 75, 75, 960, 540);

            filter.Consume(new TestTabletReport(new Vector2(120, 100)));

            Assert.Equal(new Vector2(105, 100), lastPosition);
            Assert.Equal(1, overlay.ShowCount);
        }

        [Fact]
        public void GlobalHotkeyImmediatelyTogglesAfterTipIsLiftedIntoHover()
        {
            using var filter = CreateFilter(out _, out var overlay);

            filter.Consume(new TestTabletReport(new Vector2(100, 100), 512));
            filter.Consume(new TestTabletReport(new Vector2(104, 106), 0));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            Assert.Equal(1, overlay.ShowCount);
            Assert.True(overlay.Bounds.Left <= 104);
            Assert.True(overlay.Bounds.Top <= 106);
            Assert.True(overlay.Bounds.Right >= 104);
            Assert.True(overlay.Bounds.Bottom >= 106);
        }

        [Fact]
        public void ToggleRemainsAvailableWhilePenIsWriting()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.Consume(new TestTabletReport(new Vector2(100, 100), 512));

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            Assert.Equal(1, overlay.ShowCount);

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            Assert.Equal(1, overlay.HideCount);
        }

        [Fact]
        public void DisabledGlobalHotkeyDoesNotToggleItsFilter()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.EnableGlobalHotkey = false;
            filter.Consume(new TestTabletReport(new Vector2(100, 100)));

            Assert.False(PrecisionControlCoordinator.TryQueueGlobalToggle());
            Assert.Equal(0, overlay.ShowCount);
        }

        [Fact]
        public void BorderIsHiddenWhenPrecisionIsToggledOff()
        {
            using var filter = CreateFilter(out _, out var overlay);

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            Assert.Equal(1, overlay.ShowCount);

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            Assert.Equal(1, overlay.ShowCount);
            Assert.Equal(1, overlay.HideCount);
        }

        [Fact]
        public void BorderCanBeDisabled()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.ShowBorder = false;

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            Assert.Equal(0, overlay.ShowCount);
            Assert.Equal(1, overlay.HideCount);
        }

        [Fact]
        public void BorderDefaultsWorkWithExistingSettingsFiles()
        {
            var filter = new VoiDPlugins.Filter.PrecisionControl();

            Assert.True(filter.ShowBorder);
            Assert.Equal(2.0f, filter.BorderThickness);
            Assert.Equal("#000000", filter.BorderColor);
            Assert.Equal(0.4f, filter.BorderOpacity);
            Assert.Equal(
                VoiDPlugins.Filter.PrecisionControl.ScreenRelativeMode,
                filter.PositioningMode);
            Assert.Equal(10f, filter.PointerPositionXPercent);
            Assert.Equal(10f, filter.PointerPositionYPercent);
            Assert.Equal(
                VoiDPlugins.Filter.PrecisionControl.LastActivePointerAnchor,
                filter.ActivationAnchorSource);
            Assert.True(filter.EnableGlobalHotkey);
            Assert.Equal("P", filter.GlobalHotkeyKey);
            Assert.True(filter.HotkeyCtrl);
            Assert.True(filter.HotkeyAlt);
            Assert.True(filter.HotkeyShift);
            Assert.False(filter.HotkeyWindows);
            Assert.True(filter.EnableRepositionHotkey);
            Assert.Equal("R", filter.RepositionHotkeyKey);
            Assert.True(filter.EnableNudgeHotkeys);
            Assert.Equal("Up", filter.NudgeUpKey);
            Assert.Equal("Down", filter.NudgeDownKey);
            Assert.Equal("Left", filter.NudgeLeftKey);
            Assert.Equal("Right", filter.NudgeRightKey);
            Assert.Equal(20f, filter.HorizontalNudgePercent);
            Assert.Equal(20f, filter.VerticalNudgePercent);
        }

        [Theory]
        [InlineData("#000000", 0x00000000)]
        [InlineData("#D6F2FF", 0x00FFF2D6)]
        [InlineData("123456", 0x00563412)]
        public void BorderColorsConvertToWin32ColorReferences(
            string value,
            uint expected)
        {
            Assert.True(BorderColorParser.TryParse(value, out var actual));
            Assert.Equal(expected, actual);
        }

        [Theory]
        [InlineData("")]
        [InlineData("#123")]
        [InlineData("#GG0000")]
        public void InvalidBorderColorsFallBackToBlack(string value)
        {
            Assert.False(BorderColorParser.TryParse(value, out var actual));
            Assert.Equal(0u, actual);
        }

        [Fact]
        public void GlobalHotkeyIsConfiguredOnTheFilterInsteadOfASeparateTool()
        {
            Assert.DoesNotContain(
                typeof(VoiDPlugins.Filter.PrecisionControl).Assembly.GetTypes(),
                type => type.IsClass && typeof(ITool).IsAssignableFrom(type));
        }

        [Fact]
        public void ExistingHoldBindingStillControlsTheMatchingTablet()
        {
            using var filter = CreateFilter(out var tablet, out var overlay);
            var binding = new PrecisionControlBinding { Mode = "Hold" };
            var lastPosition = Vector2.Zero;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            binding.Press(tablet, new TestTabletReport(new Vector2(100, 100)));
            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            filter.Consume(new TestTabletReport(new Vector2(120, 100)));

            Assert.Equal(new Vector2(105, 100), lastPosition);

            binding.Release(tablet, new TestTabletReport(new Vector2(120, 100)));
            filter.Consume(new TestTabletReport(new Vector2(140, 100)));

            Assert.Equal(new Vector2(140, 100), lastPosition);
            Assert.Equal(1, overlay.ShowCount);
            Assert.Equal(1, overlay.HideCount);
        }

        [Fact]
        public void PrecisionBoundsScaleAroundTheActivationPoint()
        {
            var output = new OverlayBounds(0, 0, 3840, 2160);

            var centered = PrecisionBoundsCalculator.CalculateScreenRelative(
                output,
                new Vector2(1920, 1080),
                0.25f);
            var offset = PrecisionBoundsCalculator.CalculateScreenRelative(
                output,
                new Vector2(100, 200),
                0.25f);

            AssertBounds(centered, 1440, 810, 960, 540);
            AssertBounds(offset, 75, 150, 960, 540);
        }

        [Fact]
        public void PrecisionBoundsSupportNegativeMonitorCoordinates()
        {
            var output = new OverlayBounds(-1920, 0, 1920, 1080);

            var result = PrecisionBoundsCalculator.CalculateScreenRelative(
                output,
                new Vector2(-960, 540),
                0.5f);

            AssertBounds(result, -1440, 270, 960, 540);
        }

        [Theory]
        [InlineData(10, 10, 904, 746)]
        [InlineData(50, 50, 520, 530)]
        [InlineData(0, 100, 1000, 260)]
        public void PointerRelativeBoundsPlacePointerAtConfiguredPercentage(
            float horizontalPercent,
            float verticalPercent,
            int expectedLeft,
            int expectedTop)
        {
            var result = PrecisionBoundsCalculator.CalculatePointerRelative(
                new OverlayBounds(0, 0, 3840, 2160),
                new Vector2(1000, 800),
                0.25f,
                horizontalPercent,
                verticalPercent);

            AssertBounds(result, expectedLeft, expectedTop, 960, 540);
        }

        [Theory]
        [InlineData(10, 10, -86, -44)]
        [InlineData(3830, 2150, 3734, 2096)]
        public void PointerAnchoredBoundsKeepTheirExactRelationshipAtDisplayEdges(
            float pointerX,
            float pointerY,
            int expectedLeft,
            int expectedTop)
        {
            var result = PrecisionBoundsCalculator.CalculatePointerRelative(
                new OverlayBounds(0, 0, 3840, 2160),
                new Vector2(pointerX, pointerY),
                0.25f,
                10,
                10);

            AssertBounds(result, expectedLeft, expectedTop, 960, 540);
        }

        [Fact]
        public void PointerRelativeBoundsSupportNegativeDisplayCoordinates()
        {
            var result = PrecisionBoundsCalculator.CalculatePointerRelative(
                new OverlayBounds(-1920, 0, 1920, 1080),
                new Vector2(-1910, 10),
                0.5f,
                10,
                10);

            AssertBounds(result, -2006, -44, 960, 540);
        }

        [Fact]
        public void PointerAnchoredBoundsMayExceedTheCurrentDisplay()
        {
            var result = PrecisionBoundsCalculator.CalculatePointerRelative(
                new OverlayBounds(0, 0, 1920, 1080),
                new Vector2(960, 540),
                1.5f,
                50,
                50);

            AssertBounds(result, -480, -270, 2880, 1620);
        }

        [Fact]
        public void PointerAnchoredModeMapsTabletPositionIntoItsBounds()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            filter.PointerPositionXPercent = 10;
            filter.PointerPositionYPercent = 10;
            var lastPosition = Vector2.Zero;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            AssertBounds(overlay.Bounds, 904, 746, 960, 540);

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.Equal(new Vector2(1154, 946), lastPosition);

            filter.Consume(new TestTabletReport(new Vector2(1040, 840)));
            Assert.Equal(new Vector2(1164, 956), lastPosition);

            filter.Consume(new TestTabletReport(new Vector2(10000, 10000)));
            Assert.Equal(new Vector2(1863, 1285), lastPosition);
        }

        [Fact]
        public void PreviousPointerRelativeSettingUsesCurrentPenPosition()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode = "Pointer Relative";
            filter.PointerPositionXPercent = 10;
            filter.PointerPositionYPercent = 10;

            filter.Consume(new TestTabletReport(new Vector2(1200, 900)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            AssertBounds(overlay.Bounds, 1104, 846, 960, 540);
        }

        [Fact]
        public void PreviousPenCursorRelativeSettingRemainsCompatible()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode = "Pen Cursor Relative";

            filter.Consume(new TestTabletReport(new Vector2(1200, 900)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            AssertBounds(overlay.Bounds, 1104, 846, 960, 540);
        }

        [Fact]
        public void LastActivePointerUsesNewerMouseActivity()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            filter.ActivationAnchorSource =
                VoiDPlugins.Filter.PrecisionControl.LastActivePointerAnchor;
            filter.MouseActivityProvider = () =>
                new PointerActivitySample(
                    new Vector2(2000, 1200),
                    long.MaxValue);

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            AssertBounds(overlay.Bounds, 1904, 1146, 960, 540);
        }

        [Fact]
        public void LastPenAnchorIgnoresNewerMouseActivity()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            filter.ActivationAnchorSource =
                VoiDPlugins.Filter.PrecisionControl.LastPenPositionAnchor;
            filter.MouseActivityProvider = () =>
                new PointerActivitySample(
                    new Vector2(2000, 1200),
                    long.MaxValue);

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            AssertBounds(overlay.Bounds, 904, 746, 960, 540);
        }

        [Fact]
        public void MissingPenPositionFallsBackToMouse()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            filter.ActivationAnchorSource =
                VoiDPlugins.Filter.PrecisionControl.LastPenPositionAnchor;
            filter.MouseActivityProvider = () =>
                new PointerActivitySample(
                    new Vector2(1500, 900),
                    1);

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            AssertBounds(overlay.Bounds, 1404, 846, 960, 540);
        }

        [Fact]
        public void RepositionShowsHiddenAreaAtCurrentMousePosition()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            filter.MouseActivityProvider = () =>
                new PointerActivitySample(
                    new Vector2(2000, 1200),
                    long.MaxValue);

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.Reposition));

            Assert.Equal(1, overlay.ShowCount);
            Assert.Equal(0, overlay.HideCount);
            AssertBounds(overlay.Bounds, 1904, 1146, 960, 540);
        }

        [Fact]
        public void RepositionReplacesVisibleAreaWithoutHidingItFirst()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            filter.ActivationAnchorSource =
                VoiDPlugins.Filter.PrecisionControl.LastPenPositionAnchor;

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            AssertBounds(overlay.Bounds, 904, 746, 960, 540);

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.NudgeRight));
            AssertBounds(overlay.Bounds, 1096, 746, 960, 540);

            filter.MouseActivityProvider = () =>
                new PointerActivitySample(
                    new Vector2(2000, 1200),
                    long.MaxValue);
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.Reposition));

            Assert.Equal(3, overlay.ShowCount);
            Assert.Equal(0, overlay.HideCount);
            AssertBounds(overlay.Bounds, 1904, 1146, 960, 540);
        }

        [Fact]
        public void RepositionIsBlockedWhilePenIsWriting()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            filter.Consume(new TestTabletReport(new Vector2(1000, 800), 512));

            Assert.False(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.Reposition));
            Assert.Equal(0, overlay.ShowCount);
        }

        [Fact]
        public void NudgeDefersCursorMovementUntilTheNextPenReport()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
            var lastPosition = Vector2.Zero;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            filter.Consume(new TestTabletReport(new Vector2(3600, 2000)));
            Assert.Equal(new Vector2(1804, 1246), lastPosition);

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.NudgeRight));
            AssertBounds(overlay.Bounds, 1096, 746, 960, 540);
            Assert.Equal(new Vector2(1804, 1246), lastPosition);

            filter.Consume(new TestTabletReport(new Vector2(3600, 2000)));
            Assert.Equal(new Vector2(1996, 1246), lastPosition);
        }

        [Theory]
        [InlineData(0, 0, 904, 746)]
        [InlineData(1920, 1080, 1384, 1016)]
        [InlineData(3840, 2160, 1863, 1285)]
        public void ReturningPenUsesItsAbsoluteTabletRatio(
            float penX,
            float penY,
            float expectedX,
            float expectedY)
        {
            using var filter = CreateFilter(out _);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            Vector2 lastPosition = default;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };
            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
            filter.Consume(new TestTabletReport(new Vector2(penX, penY)));

            Assert.Equal(new Vector2(expectedX, expectedY), lastPosition);
        }

        [Fact]
        public void NudgeWorksAfterPenLeavesRange()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.NudgeDown));
            AssertBounds(overlay.Bounds, 904, 854, 960, 540);
        }

        [Fact]
        public void NudgeIsBlockedOnlyWhilePenIsWriting()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.PositioningMode =
                VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;

            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            filter.Consume(new TestTabletReport(new Vector2(1100, 850), 512));

            Assert.False(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.NudgeLeft));
            AssertBounds(overlay.Bounds, 904, 746, 960, 540);

            filter.Consume(new TestTabletReport(new Vector2(1100, 850), 0));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.NudgeLeft));
            AssertBounds(overlay.Bounds, 712, 746, 960, 540);
        }

        [Theory]
        [InlineData("P", 0x50)]
        [InlineData("f24", 0x87)]
        [InlineData("ScrollLock", 0x91)]
        [InlineData("Up", 0x26)]
        [InlineData("Right", 0x27)]
        public void SupportedHotkeyNamesMapToVirtualKeys(string key, uint expected)
        {
            Assert.True(GlobalHotkeyKeyMap.TryGetVirtualKey(key, out var actual));
            Assert.Equal(expected, actual);
        }

        [Fact]
        public void DirectionHotkeysResolveToTheirConfiguredActions()
        {
            using var filter = CreateFilter(out _);
            var gesture = new HotkeyGesture(
                HotkeyModifiers.Control |
                HotkeyModifiers.Alt |
                HotkeyModifiers.Shift |
                HotkeyModifiers.NoRepeat,
                0x27,
                "Ctrl+Alt+Shift+Right");

            Assert.True(PrecisionControlGlobalHotkeyManager.TryMatch(
                filter,
                gesture,
                out var action));
            Assert.Equal(PrecisionControlAction.NudgeRight, action);
        }

        [Fact]
        public void RepositionHotkeyResolvesToItsAction()
        {
            using var filter = CreateFilter(out _);
            var gesture = new HotkeyGesture(
                HotkeyModifiers.Control |
                HotkeyModifiers.Alt |
                HotkeyModifiers.Shift |
                HotkeyModifiers.NoRepeat,
                0x52,
                "Ctrl+Alt+Shift+R");

            Assert.True(PrecisionControlGlobalHotkeyManager.TryMatch(
                filter,
                gesture,
                out var action));
            Assert.Equal(PrecisionControlAction.Reposition, action);
        }

        private static VoiDPlugins.Filter.PrecisionControl CreateFilter(
            out TabletReference tablet)
        {
            return CreateFilter(out tablet, out _);
        }

        private static VoiDPlugins.Filter.PrecisionControl CreateFilter(
            out TabletReference tablet,
            out TestOverlay overlay)
        {
            tablet = new TabletReference();
            overlay = new TestOverlay();
            var filter = new VoiDPlugins.Filter.PrecisionControl
            {
                Scale = 0.25f,
                ShowBorder = true,
                BorderThickness = 2,
                BorderColor = "#000000",
                BorderOpacity = 0.4f,
                EnableGlobalHotkey = true,
                Tablet = tablet
            };
            filter.Overlay = overlay;
            filter.OutputAreaProvider = _ =>
                new OverlayBounds(0, 0, 3840, 2160);
            filter.MouseActivityProvider = () =>
                new PointerActivitySample(Vector2.Zero, 0, false);
            PrecisionControlCoordinator.Register(filter);
            return filter;
        }

        private static void AssertBounds(
            OverlayBounds actual,
            int left,
            int top,
            int width,
            int height)
        {
            Assert.Equal(left, actual.Left);
            Assert.Equal(top, actual.Top);
            Assert.Equal(width, actual.Width);
            Assert.Equal(height, actual.Height);
        }

        private sealed class TestOverlay : IPrecisionControlOverlay
        {
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public OverlayBounds Bounds { get; private set; }

            public void Show(OverlayBounds bounds, float thickness, float opacity)
            {
                ShowCount++;
                Bounds = bounds;
            }

            public void Hide()
            {
                HideCount++;
            }

            public void Dispose()
            {
            }
        }

        private struct TestTabletReport : ITabletReport
        {
            public TestTabletReport(Vector2 position, uint pressure = 0)
            {
                Position = position;
                Pressure = pressure;
                PenButtons = Array.Empty<bool>();
                Raw = Array.Empty<byte>();
            }

            public byte[] Raw { get; set; }
            public Vector2 Position { get; set; }
            public uint Pressure { get; set; }
            public bool[] PenButtons { get; set; }
        }
    }
}
