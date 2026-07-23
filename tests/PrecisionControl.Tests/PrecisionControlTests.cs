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
            using var filter = CreateFilter(out _);
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
        public void ExistingHoldBindingStillControlsTheMatchingTablet()
        {
            using var filter = CreateFilter(out var tablet);
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
            tablet = new TabletReference();
            var filter = new VoiDPlugins.Filter.PrecisionControl
            {
                Scale = 0.25f,
                Tablet = tablet
            };
            filter.Initialize();
            return filter;
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
