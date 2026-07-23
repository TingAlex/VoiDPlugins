using System;
using System.Numerics;
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
            using var filter = CreateFilter(out _);
            var lastPosition = Vector2.Zero;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };

            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));

            Assert.False(PrecisionControlCoordinator.TryQueueGlobalToggle());

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

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            filter.Consume(new TestTabletReport(new Vector2(120, 100)));

            Assert.Equal(new Vector2(105, 100), lastPosition);
            Assert.Equal(1, overlay.ShowCount);
            Assert.Equal(new Vector2(100, 100), overlay.Anchor);
            Assert.Equal(0.25f, overlay.Scale);
        }

        [Fact]
        public void LeavingRangeDropsAPendingGlobalToggle()
        {
            using var filter = CreateFilter(out _);
            var lastPosition = Vector2.Zero;
            filter.Emit += report =>
            {
                if (report is ITabletReport tabletReport)
                    lastPosition = tabletReport.Position;
            };

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());

            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
            filter.Consume(new TestTabletReport(new Vector2(200, 200)));
            filter.Consume(new TestTabletReport(new Vector2(220, 200)));

            Assert.Equal(new Vector2(220, 200), lastPosition);
        }

        [Fact]
        public void BorderIsHiddenWhenPrecisionIsToggledOff()
        {
            using var filter = CreateFilter(out _, out var overlay);

            filter.Consume(new TestTabletReport(new Vector2(100, 100)));
            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            filter.Consume(new TestTabletReport(new Vector2(100, 100)));

            Assert.True(PrecisionControlCoordinator.TryQueueGlobalToggle());
            filter.Consume(new TestTabletReport(new Vector2(100, 100)));

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
            filter.Consume(new TestTabletReport(new Vector2(100, 100)));

            Assert.Equal(0, overlay.ShowCount);
            Assert.Equal(1, overlay.HideCount);
        }

        [Fact]
        public void BorderDefaultsWorkWithExistingSettingsFiles()
        {
            var filter = new VoiDPlugins.Filter.PrecisionControl();

            Assert.True(filter.ShowBorder);
            Assert.Equal(2.0f, filter.BorderThickness);
            Assert.Equal(0.55f, filter.BorderOpacity);
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
                BorderOpacity = 0.55f,
                Tablet = tablet
            };
            filter.Overlay = overlay;
            filter.Initialize();
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
            public TestTabletReport(Vector2 position)
            {
                Position = position;
                Pressure = 0;
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
