using System;
using OpenTabletDriver.Plugin.Tablet;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        private static NavigationHarness ContactNavigation(bool active = true)
        {
            var h = new NavigationHarness(active);
            h.Filter.EnablePenDoubleTapPagePan = true;
            h.Filter.PenPageJitterCoefficient = 0; // preserve original exact-displacement fixtures
            h.Filter.PenPageSettleScheduler = tick => new ManualPageSettleTimer(tick);
            h.At(0); // released baseline for the newly selected interaction
            return h;
        }

        private static void DoubleTap(NavigationHarness h, long start)
        {
            h.At(start, down: true);
            h.At(start + 50);
            h.At(start + 140, down: true);
            h.At(start + 190);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void DoubleTapEntersAndExitsPageModeWithoutTogglingMapping(bool active)
        {
            using var h = ContactNavigation(active);
            var shows = h.Overlay.ShowCount;
            var bounds = h.Overlay.Bounds;
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            DoubleTap(h, 10);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(shows, h.Overlay.ShowCount);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.Equal(bounds, h.Overlay.Bounds);
            Assert.Equal(0, pan.BeginCount); // double click itself never injects contacts
            DoubleTap(h, 400);
            Assert.False(h.Filter.IsPenPageDragMode);
            h.At(1000);
            Assert.Equal(shows, h.Overlay.ShowCount);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.Empty(h.Scrolls);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(20)]
        public void PageModeMovesOnlyDuringContactAndClutchesWithoutInk(int frames)
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            var bounds = h.Overlay.Bounds;
            DoubleTap(h, 10);
            h.At(210, x: 1600, y: 1200);
            Assert.Equal(0, pan.BeginCount);
            h.At(220, x: 1600, y: 1200, pressure: 512);
            var pinned = h.Trace.Last.Position;
            for (var i = 1; i <= frames; i++)
                h.At(220 + i * 5, x: 1600 + 80f * i / frames, y: 1200 + 40f * i / frames, pressure: 512);
            Assert.Equal((80d, 40d), pan.Positions[^1]);
            Assert.Equal(pinned, h.Trace.Last.Position);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            h.At(350, x: 1680, y: 1240);
            Assert.Equal(0, pan.EndCount); // lift holds stationary before any UP
            Assert.Equal(1, pan.HoldCount);
            var updates = pan.Positions.Count;
            h.At(360, x: 700, y: 500); // reposition in the air, no page movement
            Assert.Equal(updates, pan.Positions.Count);
            h.At(370, x: 700, y: 500, pressure: 512);
            Assert.Equal(1, pan.BeginCount); // rapid re-contact reuses stationary contacts
            Assert.Equal(1, pan.RebaseCount);
            h.At(380, x: 720, y: 510, pressure: 512);
            Assert.Equal((20d, 10d), pan.Positions[^1]);
            h.At(390, x: 720, y: 510);
            Assert.Equal(0, pan.EndCount);
            Assert.Equal(0, h.Trace.TipDownCount);
            Assert.Equal(bounds, h.Overlay.Bounds);
            Assert.Empty(h.Scrolls);
            h.At(520); // stationary period complete, contacts now released
            Assert.Equal(1, pan.EndCount);
            DoubleTap(h, 600);
            h.At(810, pressure: 512);
            Assert.Equal(1, h.Trace.TipDownCount);
            Assert.Equal(512u, h.Trace.Last.Pressure);
        }

        [Fact]
        public void SingleTapWaitsForDoubleTapIntervalThenTogglesOnce()
        {
            using var h = ContactNavigation();
            h.At(10, down: true);
            h.At(60);
            h.At(359);
            Assert.Equal(0, h.Overlay.HideCount);
            h.At(360);
            Assert.Equal(1, h.Overlay.HideCount);
            h.At(1000);
            Assert.Equal(1, h.Overlay.HideCount);
            Assert.False(h.Filter.IsPenPageDragMode);
        }

        [Fact]
        public void PendingSingleResolvesBeforeFirstInkPointAndNeverMidStroke()
        {
            using var h = ContactNavigation();
            h.At(10, down: true);
            h.At(60);
            h.At(100, x: 1500, pressure: 512);
            Assert.Equal(1, h.Overlay.HideCount);
            Assert.Equal(1500, h.Trace.Last.Position.X); // first point already uses full mapping
            Assert.Equal(512u, h.Trace.Last.Pressure);
            h.At(500, x: 1600, pressure: 512);
            Assert.Equal(1, h.Overlay.HideCount);
            Assert.False(h.Filter.IsPenPageDragMode);
        }

        [Fact]
        public void SecondPressWithinIntervalReservesDoubleTapUntilItsRelease()
        {
            using var h = ContactNavigation();
            h.At(10, down: true);
            h.At(60);
            h.At(350, down: true);
            h.At(500, down: true); // first tap's deadline passes while second remains held
            h.At(550);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void PressAtDeadlineStartsNewTapInsteadOfDoubleTap()
        {
            using var h = ContactNavigation();
            h.At(10, down: true);
            h.At(60);
            h.At(360, down: true);
            h.At(400);
            Assert.False(h.Filter.IsPenPageDragMode);
            Assert.Equal(1, h.Overlay.HideCount);
        }

        [Fact]
        public void HoverJitterDoesNotInvalidateEitherTap()
        {
            using var h = ContactNavigation();
            h.At(10, down: true);
            h.At(60, x: 1050);
            h.At(150, x: 1100, down: true);
            h.At(200, x: 1150);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void LongSecondPressDoesNotEnterModeOrReplayFirstTap()
        {
            using var h = ContactNavigation();
            h.At(10, down: true);
            h.At(60);
            h.At(150, down: true);
            h.At(500);
            h.At(1000);
            Assert.False(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.Empty(h.Scrolls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void LongHoverHoldsNeverPanInNewInteraction(bool pageMode)
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            if (pageMode) DoubleTap(h, 10);
            h.At(300, down: true);
            h.At(650, x: 1200, y: 1000, down: true);
            h.At(700, x: 1400, y: 1200);
            Assert.Equal(pageMode, h.Filter.IsPenPageDragMode);
            Assert.Equal(0, pan.BeginCount);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void PageModeSingleTapDoesNotHideMappingOrExitMode()
        {
            using var h = ContactNavigation();
            DoubleTap(h, 10);
            h.At(300, down: true);
            h.At(350);
            h.At(700);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Theory]
        [InlineData("focus")]
        [InlineData("disable")]
        [InlineData("interaction")]
        [InlineData("toggle")]
        [InlineData("missing")]
        public void CancellingPageModeEndsContactsAndSuppressesStrokeTail(string reason)
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            switch (reason)
            {
                case "focus": h.Window = new IntPtr(2); break;
                case "disable": h.Filter.EnablePenNavigation = false; break;
                case "interaction": h.Filter.EnablePenDoubleTapPagePan = false; break;
                case "toggle": h.Filter.QueueGlobalToggle(); break;
                case "missing": h.Filter.Consume(new TestTabletReport(new System.Numerics.Vector2(1000, 800), 512)); break;
            }
            h.At(230, pressure: 512);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            Assert.Equal(0, h.Trace.TipDownCount);
            Assert.Equal(reason is "focus" or "missing", h.Filter.IsPenPageDragMode);
            Assert.Equal(1, pan.EndCount);
            h.At(240);
            h.At(250, pressure: 512);
            Assert.Equal(reason is "focus" or "missing" ? 0u : 512u, h.Trace.Last.Pressure);
            Assert.Empty(h.Scrolls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void FailedContactPanRetainsModeWithoutInkOrWheelFallback(bool beginSucceeds)
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan { BeginSucceeds = beginSucceeds, MoveSucceeds = false };
            h.Filter.PenDirectPanFactory = () => pan;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, y: 820, pressure: 512);
            h.At(240, y: 840, pressure: 512);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(1, pan.BeginCount);
            Assert.Equal(1, pan.EndCount);
            Assert.Equal(0, h.Trace.TipDownCount);
            Assert.Empty(h.Scrolls);
            h.At(250);
            pan.BeginSucceeds = pan.MoveSucceeds = true;
            h.At(260, pressure: 512);
            Assert.Equal(0, h.Trace.TipDownCount);
            Assert.Equal(2, pan.BeginCount);
            h.At(270);
            DoubleTap(h, 500);
            h.At(710, pressure: 512);
            Assert.Equal(1, h.Trace.TipDownCount);
        }

        [Theory]
        [InlineData("focus")]
        [InlineData("range")]
        [InlineData("disable")]
        [InlineData("toggle")]
        public void CancelledPendingSingleNeverTogglesLater(string reason)
        {
            using var h = ContactNavigation();
            h.At(10, down: true);
            h.At(60);
            switch (reason)
            {
                case "focus": h.Window = new IntPtr(2); break;
                case "range": h.Filter.Consume(new OutOfRangeReport(Array.Empty<byte>())); break;
                case "disable": h.Filter.EnablePenNavigation = false; break;
                case "toggle": h.Filter.QueueGlobalToggle(); break;
            }
            var hidden = h.Overlay.HideCount;
            h.At(1000);
            h.At(1010);
            Assert.Equal(hidden, h.Overlay.HideCount);
            Assert.False(h.Filter.IsPenPageDragMode);
        }

        [Fact]
        public void OrdinaryModeRetainsHoldContactMappingDrag()
        {
            using var h = ContactNavigation();
            var bounds = h.Overlay.Bounds;
            h.At(10, down: true);
            h.At(20, pressure: 512, down: true);
            h.At(30, x: 1030, y: 820, pressure: 512, down: true);
            Assert.Equal(bounds.Left + 30, h.Overlay.Bounds.Left);
            Assert.Equal(bounds.Top + 20, h.Overlay.Bounds.Top);
            h.At(40, pressure: 512);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            h.At(50);
            h.At(500);
            Assert.Equal(0, h.Overlay.HideCount);
            Assert.False(h.Filter.IsPenPageDragMode);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void PageModeExcludesOtherMappingMovesAndRestoresThemOnExit()
        {
            using var h = ContactNavigation();
            var bounds = h.Overlay.Bounds;
            DoubleTap(h, 10);
            h.Filter.MoveFromDial("Right");
            Assert.False(h.Filter.QueueGlobalAction(PrecisionControlAction.Reposition));
            Assert.Equal(bounds, h.Overlay.Bounds);
            DoubleTap(h, 300);
            h.Filter.MoveFromDial("Right");
            Assert.Equal(bounds.Left + 40, h.Overlay.Bounds.Left);
        }

        [Fact]
        public void PageModeDirectionAndGainAreFixedForEachContact()
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            h.Filter.PenScrollSensitivity = 0.5f;
            h.Filter.PenScrollNaturalDirection = false;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, x: 1080, y: 840, pressure: 512);
            Assert.Equal((-40d, -20d), pan.Positions[^1]);
            h.Filter.PenScrollSensitivity = 2;
            h.At(240, x: 1080, y: 840, pressure: 512);
            Assert.Equal((-40d, -20d), pan.Positions[^1]);
        }

        [Fact]
        public void LeavingSensingRangeClutchesButDoesNotInvertNextExitDoubleTap()
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, y: 820, pressure: 512);
            h.Filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
            Assert.Equal(0, pan.EndCount);
            Assert.Equal(1, pan.HoldCount);
            Assert.True(h.Filter.IsPenPageDragMode);
            h.At(300, x: 700, y: 500); // released-button baseline after re-entry
            h.At(310, x: 700, y: 500, pressure: 512);
            h.At(320, x: 710, y: 505, pressure: 512);
            Assert.Equal((10d, 5d), pan.Positions[^1]);
            Assert.Equal(0, h.Trace.TipDownCount);
            h.At(330);
            DoubleTap(h, 400);
            Assert.False(h.Filter.IsPenPageDragMode);
            h.At(610, pressure: 512);
            Assert.Equal(1, h.Trace.TipDownCount);
        }

        [Fact]
        public void ThirdButtonDuringOrdinaryStrokeDoesNotStealInkOrCreateDoubleTap()
        {
            using var h = ContactNavigation();
            h.At(10, pressure: 512);
            h.At(20, pressure: 512, down: true);
            h.At(30, pressure: 512);
            Assert.Equal(512u, h.Trace.Last.Pressure);
            h.At(40);
            h.At(50, down: true);
            h.At(100);
            h.At(500);
            Assert.False(h.Filter.IsPenPageDragMode);
            Assert.Equal(1, h.Overlay.HideCount); // only the later standalone tap
        }

        [Fact]
        public void ConfiguredDoubleTapIntervalAllowsSlowerSecondTap()
        {
            using var h = ContactNavigation();
            h.Filter.PenDoubleTapIntervalMilliseconds = 500;
            h.At(10, down: true);
            h.At(60);
            h.At(450, down: true);
            h.At(500);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void DisposingPageModeEndsLiveContacts()
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.Filter.Dispose();
            Assert.Equal(1, pan.EndCount);
            Assert.False(h.Filter.IsPenPageDragMode);
        }

        private sealed class ManualPageSettleTimer : IDisposable
        {
            private readonly Action _tick;
            internal bool Disposed;
            internal ManualPageSettleTimer(Action tick) => _tick = tick;
            internal void Fire(bool evenIfDisposed = false) { if (!Disposed || evenIfDisposed) _tick(); }
            public void Dispose() => Disposed = true;
        }
    }
}
