using System;
using System.Collections.Generic;
using System.Numerics;
using OpenTabletDriver.Plugin.Tablet;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Fact]
        public void PenDragIsOptInAndLeavesExistingPenInputUnchanged()
        {
            using var filter = CreateFilter(out _);
            Assert.False(filter.EnablePenDrag);
            var trace = new PenOutputTrace(filter);
            filter.Consume(DragSample(100, 100, 512, true, true, true));
            Assert.Equal(512u, trace.Last.Pressure);
            Assert.Equal(new[] { true, true, true }, trace.Last.PenButtons);
            Assert.Equal(new Vector2(100, 100), trace.Last.Position);
        }

        [Theory]
        [InlineData(VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode)]
        [InlineData(VoiDPlugins.Filter.PrecisionControl.ScreenRelativeMode)]
        public void PenDragMovesActualMappingAndPreservesAreaSize(string mode)
        {
            using var filter = PreparePenDrag(out var overlay, mode);
            var trace = new PenOutputTrace(filter);
            filter.Consume(DragSample(1000, 800));
            var originalPen = trace.Last.Position;
            var originalBounds = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 0, true));
            filter.Consume(DragSample(1200, 900, 512, true));
            Assert.Equal(originalBounds, overlay.Bounds); // first contact is only a baseline
            filter.Consume(DragSample(1320, 960, 512, true));
            AssertBounds(overlay.Bounds, originalBounds.Left + 120,
                originalBounds.Top + 60, originalBounds.Width, originalBounds.Height);
            Assert.Equal(originalPen, trace.Last.Position); // cursor doesn't double the motion
            Assert.Equal(0u, trace.Last.Pressure);

            filter.Consume(DragSample(1320, 960, 0, false));
            filter.Consume(DragSample(1000, 800, 512, false));
            Assert.Equal(512u, trace.Last.Pressure);
            Assert.Equal(originalPen + new Vector2(120, 60), trace.Last.Position);
        }

        [Fact]
        public void SimultaneousButtonAndTipContactDoesNotLeakFirstFrame()
        {
            using var filter = PreparePenDrag(out var overlay);
            var trace = new PenOutputTrace(filter);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1030, 820, 512, true));
            Assert.Equal(0, trace.TipDownCount);
            Assert.All(trace.Reports, r => Assert.False(r.PenButtons[2]));
            AssertBounds(overlay.Bounds, original.Left + 30, original.Top + 20,
                original.Width, original.Height);
        }

        [Fact]
        public void HoverDoesNotDragAndRecontactHasNoPositionJump()
        {
            using var filter = PreparePenDrag(out var overlay);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 0, true));
            filter.Consume(DragSample(2000, 1500, 0, true));
            Assert.Equal(original, overlay.Bounds);
            filter.Consume(DragSample(2000, 1500, 512, true));
            filter.Consume(DragSample(2100, 1550, 512, true));
            var afterFirstContact = overlay.Bounds;
            filter.Consume(DragSample(2100, 1550, 0, true));
            filter.Consume(DragSample(100, 100, 0, true));
            filter.Consume(DragSample(100, 100, 512, true));
            Assert.Equal(afterFirstContact, overlay.Bounds);
            filter.Consume(DragSample(120, 130, 512, true));
            AssertBounds(overlay.Bounds, original.Left + 120, original.Top + 80,
                original.Width, original.Height);
        }

        [Fact]
        public void ReleasingAndRepressingWhileTipIsDownWaitsForLift()
        {
            using var filter = PreparePenDrag(out var overlay);
            var trace = new PenOutputTrace(filter);
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1100, 850, 512, true));
            var committed = overlay.Bounds;
            filter.Consume(DragSample(1200, 900, 512, false));
            filter.Consume(DragSample(1300, 950, 512, true));
            filter.Consume(DragSample(1400, 1000, 512, false));
            Assert.Equal(0, trace.TipDownCount);
            Assert.Equal(committed, overlay.Bounds);
            filter.Consume(DragSample(1400, 1000));
            filter.Consume(DragSample(1500, 1100, 512));
            Assert.Equal(1, trace.TipDownCount);
            Assert.Equal(committed, overlay.Bounds);
        }

        [Fact]
        public void ButtonPressedDuringWritingDoesNotStealStrokeOrReplayDrag()
        {
            using var filter = PreparePenDrag(out var overlay);
            var trace = new PenOutputTrace(filter);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512));
            filter.Consume(DragSample(1100, 850, 512, true));
            Assert.Equal(512u, trace.Last.Pressure);
            filter.Consume(DragSample(1100, 850, 0, true));
            filter.Consume(DragSample(1200, 900, 512, true));
            Assert.Equal(0u, trace.Last.Pressure);
            Assert.Equal(original, overlay.Bounds);
            filter.Consume(DragSample(1200, 900));
            filter.Consume(DragSample(1200, 900, 512, true));
            filter.Consume(DragSample(1210, 910, 512, true));
            Assert.Equal(0u, trace.Last.Pressure);
            Assert.Equal(original.Left + 10, overlay.Bounds.Left);
        }

        [Fact]
        public void DragDoesNotCreateInactiveAreaOrReuseHeldButtonOnActivation()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.EnablePenDrag = true;
            var trace = new PenOutputTrace(filter);
            filter.Consume(DragSample(1000, 800));
            filter.Consume(DragSample(1100, 800, 512, true));
            Assert.Equal(0, overlay.ShowCount);
            Assert.Equal(512u, trace.Last.Pressure);
            filter.Consume(DragSample(1100, 800, 0, true));
            filter.QueueGlobalToggle();
            var original = overlay.Bounds;
            filter.Consume(DragSample(1300, 800, 512, true));
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(0u, trace.Last.Pressure);
        }

        [Fact]
        public void SubpixelDragDisplacementsAccumulateFromContactOrigin()
        {
            using var filter = PreparePenDrag(out var overlay);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512, true));
            for (var i = 1; i <= 10; i++)
                filter.Consume(DragSample(1000 + 0.1f * i, 800 + 0.1f * i, 512, true));
            AssertBounds(overlay.Bounds, original.Left + 1, original.Top + 1,
                original.Width, original.Height);
        }

        [Theory]
        [InlineData(0.5f, 50, 25)]
        [InlineData(2f, 200, 100)]
        public void SensitivityIsIndependentOfWritingScale(float gain, int dx, int dy)
        {
            using var filter = PreparePenDrag(out var overlay);
            filter.PenDragSensitivity = gain;
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1100, 850, 512, true));
            AssertBounds(overlay.Bounds, original.Left + dx, original.Top + dy,
                original.Width, original.Height);
            Assert.Equal(0.25f, filter.Scale);
        }

        [Fact]
        public void SensitivityChangeIsAppliedAtNextGestureWithoutJump()
        {
            using var filter = PreparePenDrag(out var overlay);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1100, 800, 512, true));
            filter.PenDragSensitivity = 2;
            filter.Consume(DragSample(1110, 800, 512, true));
            Assert.Equal(original.Left + 110, overlay.Bounds.Left);
            filter.Consume(DragSample(1110, 800));
            filter.Consume(DragSample(1110, 800, 512, true));
            filter.Consume(DragSample(1120, 800, 512, true));
            Assert.Equal(original.Left + 130, overlay.Bounds.Left);
        }

        [Fact]
        public void ArmedDragExcludesDialAndQueuedOrGlobalRepositioning()
        {
            using var filter = PreparePenDrag(out var overlay);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 0, true));
            filter.MoveFromDial("Right");
            Assert.False(filter.QueueGlobalAction(PrecisionControlAction.Reposition));
            Assert.False(filter.QueueGlobalAction(PrecisionControlAction.NudgeRight));
            filter.Queue(PrecisionControlAction.NudgeDown);
            filter.Queue(PrecisionControlAction.Reposition);
            filter.Consume(DragSample(1000, 800, 0, true));
            Assert.Equal(original, overlay.Bounds);
            filter.Consume(DragSample(1000, 800));
            filter.Consume(DragSample(1000, 800));
            Assert.Equal(original, overlay.Bounds); // no delayed movement
            filter.MoveFromDial("Right");
            Assert.Equal(original.Left + 40, overlay.Bounds.Left);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DisablingDragOrMappingDuringContactDoesNotLeakTail(bool hideMapping)
        {
            using var filter = PreparePenDrag(out var overlay);
            var trace = new PenOutputTrace(filter);
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1100, 850, 512, true));
            var committed = overlay.Bounds;
            if (hideMapping)
                filter.QueueGlobalToggle();
            else
                filter.EnablePenDrag = false;
            filter.Consume(DragSample(1200, 900, 512, true));
            Assert.Equal(0u, trace.Last.Pressure);
            Assert.False(trace.Last.PenButtons[2]);
            filter.Consume(DragSample(1200, 900, 512));
            Assert.Equal(0, trace.TipDownCount);
            Assert.Equal(committed, overlay.Bounds);
            filter.Consume(DragSample(1200, 900));
            filter.Consume(DragSample(1200, 900, 512));
            Assert.Equal(1, trace.TipDownCount);
        }

        [Fact]
        public void OutOfRangeDiscardsBaselineAndRequiresButtonRelease()
        {
            using var filter = PreparePenDrag(out var overlay);
            var trace = new PenOutputTrace(filter);
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1100, 850, 512, true));
            var committed = overlay.Bounds;
            filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
            filter.Consume(DragSample(2500, 1800, 0, true));
            filter.Consume(DragSample(2500, 1800, 512, true));
            Assert.Equal(committed, overlay.Bounds);
            Assert.Equal(0u, trace.Last.Pressure);
            filter.Consume(DragSample(2500, 1800));
            filter.Consume(DragSample(2500, 1800, 512, true));
            filter.Consume(DragSample(2510, 1810, 512, true));
            Assert.Equal(committed.Left + 10, overlay.Bounds.Left);
        }

        [Fact]
        public void InitiallyHeldButtonRequiresReleaseWithoutAccidentalInk()
        {
            using var filter = CreateFilter(out _, out var overlay);
            filter.EnablePenDrag = true;
            filter.QueueGlobalToggle();
            var trace = new PenOutputTrace(filter);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1100, 900, 512, true));
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(0, trace.TipDownCount);
            filter.Consume(DragSample(1100, 900));
            filter.Consume(DragSample(1100, 900, 512, true));
            filter.Consume(DragSample(1120, 910, 512, true));
            Assert.Equal(original.Left + 20, overlay.Bounds.Left);
        }

        [Fact]
        public void MissingThirdButtonCancelsDragWithoutFallbackOrArrayMutation()
        {
            using var filter = PreparePenDrag(out var overlay);
            var trace = new PenOutputTrace(filter);
            var buttons = new[] { false, true, true };
            var sample = DragSample(1000, 800, 512, true);
            sample.PenButtons = buttons;
            filter.Consume(sample);
            Assert.True(buttons[2]);
            Assert.True(trace.Last.PenButtons[1]);
            Assert.False(trace.Last.PenButtons[2]);
            var original = overlay.Bounds;
            filter.Consume(new TestTabletReport(new Vector2(1100, 850), 512)
                { PenButtons = new[] { true, true } });
            Assert.Equal(0u, trace.Last.Pressure);
            filter.Consume(DragSample(1200, 900, 512, true));
            Assert.Equal(0u, trace.Last.Pressure);
            Assert.Equal(original, overlay.Bounds);
            filter.Consume(DragSample(1200, 900));
            filter.Consume(DragSample(1200, 900, 512));
            Assert.Equal(512u, trace.Last.Pressure);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(0f)]
        [InlineData(6f)]
        public void InvalidSensitivityCannotStartDrag(float gain)
        {
            using var filter = PreparePenDrag(out var overlay);
            filter.PenDragSensitivity = gain;
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(1100, 850, 512, true));
            Assert.Equal(original, overlay.Bounds);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.MaxValue)]
        public void InvalidOrOverflowingPositionCancelsWithoutMappingOrInk(float x)
        {
            using var filter = PreparePenDrag(out var overlay);
            var trace = new PenOutputTrace(filter);
            var original = overlay.Bounds;
            filter.Consume(DragSample(1000, 800, 512, true));
            filter.Consume(DragSample(x, 900, 512, true));
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(0u, trace.Last.Pressure);
            Assert.True(float.IsFinite(trace.Last.Position.X));
            filter.Consume(DragSample(1300, 950, 512, true));
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(0u, trace.Last.Pressure);
        }

        private static VoiDPlugins.Filter.PrecisionControl PreparePenDrag(
            out TestOverlay overlay,
            string mode = VoiDPlugins.Filter.PrecisionControl.PointerAnchoredMode)
        {
            var filter = CreateFilter(out _, out overlay);
            filter.EnablePenDrag = true;
            filter.PositioningMode = mode;
            filter.Consume(DragSample(1000, 800));
            filter.QueueGlobalToggle();
            filter.Consume(DragSample(1000, 800));
            return filter;
        }

        private static TestTabletReport DragSample(float x, float y, uint pressure = 0,
            bool thirdButton = false, bool firstButton = false, bool secondButton = false)
        {
            return new TestTabletReport(new Vector2(x, y), pressure)
                { PenButtons = new[] { firstButton, secondButton, thirdButton } };
        }

        // Model the downstream tip transition contract, not the drag implementation.
        // It detects a single leaking contact frame as well as a delayed tail stroke.
        private sealed class PenOutputTrace
        {
            public readonly List<ITabletReport> Reports = new();
            public ITabletReport Last => Reports[^1];
            public int TipDownCount { get; private set; }
            private bool _tipDown;

            public PenOutputTrace(VoiDPlugins.Filter.PrecisionControl filter)
            {
                filter.Emit += report =>
                {
                    if (report is OutOfRangeReport)
                        _tipDown = false;
                    if (report is not ITabletReport pen)
                        return;
                    var down = pen.Pressure > 0;
                    if (down && !_tipDown)
                        TipDownCount++;
                    _tipDown = down;
                    Reports.Add(pen);
                };
            }
        }
    }
}
