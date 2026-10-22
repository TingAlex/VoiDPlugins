using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin.Tablet;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Fact]
        public void HoverTapClosesAreaAfterOpeningDespiteSmallVisiblePenMovement()
        {
            using var h = new NavigationHarness(active: false);
            h.At(0, down: true);
            h.At(100);
            Assert.Equal(1, h.Overlay.ShowCount);
            h.At(200, down: true);
            // Scale=.25: sixteen original output pixels would be only four mapped pixels.
            // This is a hovering button tap, not a contact drag or long-press scroll.
            h.At(300, x: 1016);
            Assert.Equal(1, h.Overlay.HideCount);
            h.At(310, x: 1016, pressure: 512);
            Assert.Equal(1016, h.Trace.Last.Position.X);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void ShortTapTogglesActualMappingOffAndOnOncePerRelease()
        {
            using var h = new NavigationHarness();
            var shows = h.Overlay.ShowCount;
            h.At(0, down: true);
            Assert.Equal(0, h.Overlay.HideCount);
            h.At(100);
            Assert.Equal(1, h.Overlay.HideCount);
            h.At(110);
            Assert.Equal(1, h.Overlay.HideCount);
            h.At(120, x: 1500, y: 1000, pressure: 512);
            Assert.Equal(1500, h.Trace.Last.Position.X); // full-size mapping restored
            Assert.Equal(512u, h.Trace.Last.Pressure);
            h.At(130, x: 1500, y: 1000);
            h.At(140, x: 1500, y: 1000, down: true);
            h.At(240, x: 1500, y: 1000);
            Assert.Equal(shows + 1, h.Overlay.ShowCount);
            h.At(250, x: 1500, y: 1000, pressure: 512);
            Assert.NotEqual(1500, h.Trace.Last.Position.X); // actual precision mapping resumed
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void TapCanCreateAreaWhenInitiallyInactive()
        {
            using var h = new NavigationHarness(active: false);
            h.At(0, down: true);
            h.At(100);
            Assert.Equal(1, h.Overlay.ShowCount);
            Assert.Equal(0, h.Trace.TipDownCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LongHoldDoesNotToggleEvenWithoutMovementOrIntermediateReports(bool intermediate)
        {
            using var h = new NavigationHarness();
            var shows = h.Overlay.ShowCount;
            h.At(0, down: true);
            if (intermediate)
                h.At(400, down: true);
            h.At(500);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.Equal(shows, h.Overlay.ShowCount);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void FastHoverMovementIsNotMisreadAsTapAndIsNotReplayedAfterHold()
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.At(100, x: 1100, down: true);
            Assert.Empty(h.Scrolls);
            h.At(300, x: 1200, down: true);
            Assert.Empty(h.Scrolls); // crossing threshold sets a fresh baseline
            h.At(310, x: 1210, y: 820, down: true);
            Assert.Equal((-30, 60), Assert.Single(h.Scrolls));
            h.At(320, x: 1210, y: 820);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void HoverMovementBeforeThresholdDoesNotCancelShortTap()
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.At(100, x: 1050);
            Assert.Equal(1, h.Overlay.HideCount);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void RepeatedHoverTapsAlternateMappingDespitePenJitter()
        {
            using var h = new NavigationHarness(active: false);
            for (var i = 0; i < 10; i++)
            {
                h.At(i * 200, x: 1000, down: true);
                h.At(i * 200 + 100, x: 1016 + i * 2);
                Assert.Equal((i + 2) / 2, h.Overlay.ShowCount);
                Assert.Equal((i + 1) / 2, h.Overlay.HideCount);
            }
            Assert.Empty(h.Scrolls);
        }

        [Theory]
        [InlineData(true, false, -30, 60)]
        [InlineData(false, false, 30, -60)]
        [InlineData(true, true, -30, 60)]
        public void HoverScrollHasTwoAxesAndWorksWithoutPrecisionMapping(
            bool natural, bool inactive, int expectedX, int expectedY)
        {
            using var h = new NavigationHarness(active: !inactive);
            h.Filter.PenScrollNaturalDirection = natural;
            var bounds = h.Overlay.Bounds;
            h.At(0, down: true);
            var pinned = h.Trace.Last.Position;
            h.At(300, down: true);
            h.At(310, x: 1010, y: 820, down: true);
            Assert.Equal((expectedX, expectedY), Assert.Single(h.Scrolls));
            Assert.Equal(bounds, h.Overlay.Bounds);
            Assert.Equal(pinned, h.Trace.Last.Position);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            Assert.False(h.Trace.Last.PenButtons[2]);
        }

        [Fact]
        public void HoverScrollAccumulatesSmallDeltasAndDropsRemainderOnRelease()
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.At(300, down: true);
            for (var i = 1; i <= 10; i++)
                h.At(300 + i, x: 1000 + i * 0.1f, down: true);
            Assert.Equal(-3, h.Scrolls.Sum(s => s.Item1));
            h.At(320, x: 1001);
            var count = h.Scrolls.Count;
            h.At(330, x: 1001.2f);
            Assert.Equal(count, h.Scrolls.Count);
        }

        [Fact]
        public void ContactDragRemainsAvailableAndNeverTriggersTapOrScroll()
        {
            using var h = new NavigationHarness();
            var original = h.Overlay.Bounds;
            h.At(0, down: true);
            h.At(10, pressure: 512, down: true);
            h.At(20, x: 1030, y: 820, pressure: 512, down: true);
            AssertBounds(h.Overlay.Bounds, original.Left + 30, original.Top + 20,
                original.Width, original.Height);
            h.At(30, x: 1040, y: 830, pressure: 512);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            h.At(40, x: 1040, y: 830);
            h.At(50, x: 1040, y: 830, pressure: 512);
            Assert.Equal(1, h.Trace.TipDownCount);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void SwitchingBetweenContactAndHoverResetsBothBaselines()
        {
            using var h = new NavigationHarness();
            var original = h.Overlay.Bounds;
            h.At(0, down: true);
            h.At(300, down: true);
            h.At(310, y: 810, down: true);
            Assert.Single(h.Scrolls);
            h.At(320, x: 1200, y: 900, pressure: 512, down: true);
            Assert.Equal(original, h.Overlay.Bounds);
            h.At(330, x: 1210, y: 910, pressure: 512, down: true);
            var moved = h.Overlay.Bounds;
            Assert.Equal(original.Left + 10, moved.Left);
            Assert.Single(h.Scrolls);
            h.At(340, x: 1600, y: 1200, down: true);
            Assert.Single(h.Scrolls); // lift starts a new hover baseline
            h.At(350, x: 1610, y: 1210, down: true);
            Assert.Equal((-30, 30), h.Scrolls[1]);
            Assert.Equal(moved, h.Overlay.Bounds);
            h.At(360, x: 1610, y: 1210);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void HeldButtonCannotStealAnExistingStroke()
        {
            using var h = new NavigationHarness();
            h.At(0, pressure: 512);
            h.At(10, pressure: 512, down: true);
            Assert.Equal(512u, h.Trace.Last.Pressure);
            h.At(400, down: true);
            h.At(410, down: true);
            h.At(420, y: 900, pressure: 512, down: true);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            h.At(430);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void FocusChangeCancelsGestureAndReleaseDoesNotToggle()
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.Window = new IntPtr(2);
            h.At(100, down: true);
            h.At(400, y: 900, down: true);
            h.At(410, y: 900);
            Assert.Empty(h.Scrolls);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void FailedScrollStopsUntilReleaseWithoutRetryOrTap()
        {
            using var h = new NavigationHarness();
            h.SendSucceeds = false;
            h.At(0, down: true);
            h.At(300, down: true);
            h.At(310, y: 810, down: true);
            h.At(320, y: 820, down: true);
            h.At(330, y: 820);
            Assert.Single(h.Scrolls); // one attempted send, never replayed
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void OutOfRangeDiscardsOldTapAndScrollState()
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.Filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
            h.At(400, y: 900, down: true);
            h.At(500, y: 1000, down: true);
            h.At(510, y: 1000);
            Assert.Empty(h.Scrolls);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CancelDuringContactSuppressesTailAndRestoresNextStroke(bool disableFeature)
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.At(10, pressure: 512, down: true);
            if (disableFeature)
                h.Filter.EnablePenNavigation = false;
            else
                h.Filter.QueueGlobalToggle();
            h.At(20, pressure: 512, down: true);
            h.At(30, pressure: 512);
            Assert.Equal(0, h.Trace.TipDownCount);
            h.At(40);
            h.At(50, pressure: 512);
            Assert.Equal(1, h.Trace.TipDownCount);
            Assert.Empty(h.Scrolls);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(2000f)]
        public void InvalidOrHugeHoverMovementCannotGenerateScroll(float y)
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.At(300, down: true);
            h.At(310, y: y, down: true);
            h.At(320, y: 810, down: true);
            h.At(330, y: 810);
            Assert.Empty(h.Scrolls);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void MissingButtonReportCancelsPendingTap()
        {
            using var h = new NavigationHarness();
            h.At(0, down: true);
            h.Filter.Consume(new TestTabletReport(new System.Numerics.Vector2(1000, 800)));
            h.At(100);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void PendingAndHoverNavigationExcludeOtherMappingMoves()
        {
            using var h = new NavigationHarness();
            var original = h.Overlay.Bounds;
            h.At(0, down: true);
            h.Filter.MoveFromDial("Right");
            Assert.False(h.Filter.QueueGlobalAction(PrecisionControlAction.Reposition));
            h.At(300, down: true);
            h.Filter.Queue(PrecisionControlAction.NudgeDown);
            h.At(310, y: 810, down: true);
            h.At(320, y: 810);
            Assert.Equal(original, h.Overlay.Bounds);
        }

        [Fact]
        public void WheelPacketsContainOnlyWheelFlagsAndCorrectNativeSizes()
        {
            var packets = PenPageScroller.BuildInputs(-30, 60);
            Assert.Equal(2, packets.Length);
            Assert.Equal(0x1000u, packets[0].Mouse.Flags);
            Assert.Equal(unchecked((uint)-30), packets[0].Mouse.Data);
            Assert.Equal(0x0800u, packets[1].Mouse.Flags);
            Assert.Equal(60u, packets[1].Mouse.Data);
            Assert.All(packets, p =>
            {
                Assert.Equal(0u, p.Type);
                Assert.Equal(0, p.Mouse.X);
                Assert.Equal(0, p.Mouse.Y);
                Assert.Equal(UIntPtr.Zero, p.Mouse.ExtraInfo);
            });
            Assert.Equal(IntPtr.Size == 8 ? 40 : 28, Marshal.SizeOf<PenPageScroller.WheelInput>());
            Assert.Empty(PenPageScroller.BuildInputs(0, 0));
        }

        [Fact]
        public void HoldThresholdAndScrollSensitivityUseConfiguredValues()
        {
            using var h = new NavigationHarness();
            h.Filter.PenNavigationHoldMilliseconds = 500;
            h.Filter.PenScrollSensitivity = 0.5f;
            h.At(0, down: true);
            h.At(300, y: 810, down: true);
            h.At(490, y: 820, down: true);
            Assert.Empty(h.Scrolls);
            h.At(500, y: 820, down: true);
            h.At(510, y: 840, down: true);
            Assert.Equal((0, 30), Assert.Single(h.Scrolls));
        }

        [Fact]
        public void SwitchingFromLegacyDragToNavigationReleasesOldCapture()
        {
            using var h = new NavigationHarness();
            h.Filter.EnablePenNavigation = false;
            h.At(0);
            h.At(10, down: true);
            h.At(20, pressure: 512, down: true);
            h.Filter.EnablePenNavigation = true;
            h.At(30, pressure: 512, down: true);
            h.At(40, pressure: 512);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            h.At(50);
            Assert.True(h.Filter.QueueGlobalAction(PrecisionControlAction.NudgeRight));
            h.At(60, down: true);
            h.At(160);
            Assert.Equal(1, h.Overlay.HideCount);
        }

        private sealed class NavigationHarness : IDisposable
        {
            internal readonly VoiDPlugins.Filter.PrecisionControl Filter;
            internal readonly TestOverlay Overlay;
            internal readonly PenOutputTrace Trace;
            internal readonly List<(int, int)> Scrolls = new();
            internal IntPtr Window = new(1);
            internal bool SendSucceeds = true;
            private long _now;
            internal NavigationHarness(bool active = true)
            {
                Filter = CreateFilter(out _, out var overlay);
                Overlay = overlay;
                Filter.PositioningMode = VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode;
                Filter.EnablePenDrag = true;
                Filter.EnablePenNavigation = true;
                Filter.EnablePenPageNavigation = true; // opt in only for retained experiment tests
                Filter.PenNavigationClock = () => _now;
                Filter.PenNavigationContext = () => Window;
                Filter.PenScrollSender = (x, y, target) =>
                {
                    Assert.Equal(Window, target);
                    Scrolls.Add((x, y));
                    return SendSucceeds;
                };
                Filter.Consume(DragSample(1000, 800));
                if (active)
                    Filter.QueueGlobalToggle();
                Trace = new PenOutputTrace(Filter);
                Filter.Consume(DragSample(1000, 800));
            }
            internal void At(long time, float x = 1000, float y = 800,
                uint pressure = 0, bool down = false)
            {
                _now = time;
                Filter.Consume(DragSample(x, y, pressure, down));
            }
            public void Dispose() => Filter.Dispose();
        }
    }
}
