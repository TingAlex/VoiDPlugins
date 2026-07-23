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
        public void GlobalHotkeyIsIgnoredWhenPenIsOutOfRange()
        {
            using var filter = CreateFilter(out _, out var overlay);
            var lastPosition = Vector2.Zero;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };

            filter.Consume(new TestTabletReport(new Vector2(90, 90)));
            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));

            Assert.False(PrecisionControlCoordinator.TryQueueGlobalToggle());
            Assert.Equal(0, overlay.ShowCount);

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            filter.Consume(new TestTabletReport(new Vector2(120, 100)));

            Assert.Equal(new Vector2(120, 100), lastPosition);
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
            Assert.Equal(new Vector2(100, 100), overlay.Anchor);
            Assert.Equal(0.25f, overlay.Scale);

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
            Assert.Equal(new Vector2(104, 106), overlay.Anchor);
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
            Assert.True(filter.EnableGlobalHotkey);
            Assert.Equal("P", filter.GlobalHotkeyKey);
            Assert.True(filter.HotkeyCtrl);
            Assert.True(filter.HotkeyAlt);
            Assert.True(filter.HotkeyShift);
            Assert.False(filter.HotkeyWindows);
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

            var centered = PrecisionBoundsCalculator.Calculate(
                output,
                new Vector2(1920, 1080),
                0.25f);
            var offset = PrecisionBoundsCalculator.Calculate(
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

            var result = PrecisionBoundsCalculator.Calculate(
                output,
                new Vector2(-960, 540),
                0.5f);

            AssertBounds(result, -1440, 270, 960, 540);
        }

        [Theory]
        [InlineData("P", 0x50)]
        [InlineData("f24", 0x87)]
        [InlineData("ScrollLock", 0x91)]
        public void SupportedHotkeyNamesMapToVirtualKeys(string key, uint expected)
        {
            Assert.True(GlobalHotkeyKeyMap.TryGetVirtualKey(key, out var actual));
            Assert.Equal(expected, actual);
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
            public Vector2 Anchor { get; private set; }
            public float Scale { get; private set; }

            public void Show(Vector2 anchor, float scale, float thickness, float opacity)
            {
                ShowCount++;
                Anchor = anchor;
                Scale = scale;
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
