using System;
using System.Numerics;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Fact]
        public void TouchpadDragIsOptIn()
        {
            using var filter = CreateFilter(out _, out var overlay);
            var original = ActivateMapping(filter, overlay);
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100));
            filter.HandleTouchpadFrame(Frame(20, 400));
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);
        }

        [Fact]
        public void ThreeFingerDragMovesActualMappingAndKeepsSize()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;

            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100)); // crosses the start threshold, baseline only
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Dragging, filter.TouchpadPhase);

            filter.HandleTouchpadFrame(Frame(20, 500)); // +400 logical units from the baseline
            Assert.Equal(original.Left + 375, overlay.Bounds.Left);
            Assert.Equal(original.Top, overlay.Bounds.Top);
            Assert.Equal(original.Width, overlay.Bounds.Width);
            Assert.Equal(original.Height, overlay.Bounds.Height);
        }

        [Fact]
        public void SmallMovementBelowThresholdDoesNotMoveMapping()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;

            filter.HandleTouchpadFrame(Frame(0, 0));
            for (var step = 1; step <= 3; step++)
                filter.HandleTouchpadFrame(Frame(step * 10, step * 10));
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Tracking, filter.TouchpadPhase);

            filter.HandleTouchpadFrame(Frame(40, 50)); // threshold crossed here
            Assert.Equal(original, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(50, 100)); // +50 from the new baseline
            Assert.Equal(original.Left + 47, overlay.Bounds.Left);
        }

        [Theory]
        [InlineData(2)]
        [InlineData(4)]
        public void NonConfiguredFingerCountNeverDrags(int fingers)
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;

            filter.HandleTouchpadFrame(Frame(0, 0, fingers: fingers));
            filter.HandleTouchpadFrame(Frame(10, 200, fingers: fingers));
            filter.HandleTouchpadFrame(Frame(20, 500, fingers: fingers));
            Assert.Equal(original, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);
        }

        [Fact]
        public void LiftingFingersKeepsPositionAndNewGestureDoesNotJump()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            DragTo(filter, overlay, original.Left + 375);
            var committed = overlay.Bounds;

            filter.HandleTouchpadFrame(new TouchpadFrame(40, Array.Empty<TouchpadContact>(), 4096, 4096));
            Assert.Equal(committed, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);

            // Re-place the fingers far away: the first frames are a new baseline, not a jump.
            filter.HandleTouchpadFrame(Frame(60, -4000));
            filter.HandleTouchpadFrame(Frame(70, -3900));
            Assert.Equal(committed, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(80, -3700));
            Assert.Equal(committed.Left + 188, overlay.Bounds.Left);
        }

        [Fact]
        public void ChangingContactsWhileDraggingRebuildsBaselineWithoutJump()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            DragTo(filter, overlay, original.Left + 375);
            var committed = overlay.Bounds;

            filter.HandleTouchpadFrame(Frame(30, 900, ids: new[] { 7, 8, 9 }));
            filter.HandleTouchpadFrame(Frame(40, 1000, ids: new[] { 7, 8, 9 }));
            Assert.Equal(committed, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(50, 1100, ids: new[] { 7, 8, 9 }));
            Assert.Equal(committed.Left + 94, overlay.Bounds.Left);
        }

        [Fact]
        public void PauseBetweenFramesRestartsGestureWithoutJump()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            DragTo(filter, overlay, original.Left + 375);
            var committed = overlay.Bounds;

            filter.HandleTouchpadFrame(Frame(500, 1500)); // gap > 40 ms
            filter.HandleTouchpadFrame(Frame(510, 1600));
            Assert.Equal(committed, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(520, 1700));
            Assert.Equal(committed.Left + 94, overlay.Bounds.Left);
        }

        [Fact]
        public void TouchpadDragIgnoredWhenMappingIsInactive()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            filter.QueueGlobalToggle(); // hide mapping
            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            var original = overlay.Bounds;
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 400));
            Assert.Equal(original, overlay.Bounds);
        }

        [Fact]
        public void PenContactEndsTouchpadDragAndBlocksIt()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            DragTo(filter, overlay, original.Left + 375);
            var committed = overlay.Bounds;

            filter.Consume(new TestTabletReport(new Vector2(1000, 800), 512));
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);
            filter.HandleTouchpadFrame(Frame(30, 900));
            Assert.Equal(committed, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(0f)]
        [InlineData(3.5f)]
        public void InvalidTouchpadSensitivityCannotStartDrag(float sensitivity)
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = sensitivity;
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 400));
            filter.HandleTouchpadFrame(Frame(20, 900));
            Assert.Equal(original, overlay.Bounds);
        }

        [Fact]
        public void TouchpadDragBlocksDialAndRepositionHotkeys()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            DragTo(filter, overlay, original.Left + 375);
            var committed = overlay.Bounds;

            filter.MoveFromDial("Right");
            Assert.False(filter.QueueGlobalAction(PrecisionControlAction.NudgeRight));
            Assert.Equal(committed, overlay.Bounds);
        }

        private static void DragTo(
            VoiDPlugins.Filter.PrecisionControl filter,
            TestOverlay overlay,
            int expectedLeft)
        {
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100));
            filter.HandleTouchpadFrame(Frame(20, 500));
            Assert.Equal(expectedLeft, overlay.Bounds.Left);
        }

        private static OverlayBounds ActivateMapping(
            VoiDPlugins.Filter.PrecisionControl filter,
            TestOverlay overlay)
        {
            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            filter.QueueGlobalToggle();
            filter.Consume(new TestTabletReport(new Vector2(1000, 800)));
            return overlay.Bounds;
        }

        private static VoiDPlugins.Filter.PrecisionControl PrepareTouchpadDrag(out TestOverlay overlay)
        {
            var filter = CreateFilter(out _, out overlay);
            filter.EnableTouchpadDrag = true;
            filter.TouchpadDragSensitivity = 1f;
            ActivateMapping(filter, overlay);
            return filter;
        }

        private static TouchpadFrame Frame(
            long time,
            int offsetX,
            int offsetY = 0,
            int fingers = 3,
            int[]? ids = null)
        {
            var contacts = new TouchpadContact[fingers];
            for (var index = 0; index < fingers; index++)
            {
                contacts[index] = new TouchpadContact(
                    ids?[index] ?? (index + 1),
                    1000 + (index * 500) + offsetX,
                    800 + offsetY);
            }

            return new TouchpadFrame(time, contacts, 4096, 4096);
        }
    }
}

