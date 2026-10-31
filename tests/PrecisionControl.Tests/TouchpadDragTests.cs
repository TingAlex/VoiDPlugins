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

            filter.HandleTouchpadFrame(Frame(20, 500)); // committed on the next frame
            Assert.Equal(original, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(30, 500));
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
            filter.HandleTouchpadFrame(Frame(50, 100)); // +50 from the new baseline, committed next
            Assert.Equal(original, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(60, 100));
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
            Assert.Equal(committed, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(90, -3700));
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
            Assert.Equal(committed, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(60, 1100, ids: new[] { 7, 8, 9 }));
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
            Assert.Equal(committed, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(530, 1700));
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
        [InlineData(0.01f)]
        [InlineData(5.5f)]
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

        [Theory]
        [InlineData(0.02f, 2)]
        [InlineData(5f, 469)]
        public void SensitivityRangeEndpointsAreAccepted(float sensitivity, int expectedDeltaX)
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = sensitivity;
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100));
            filter.HandleTouchpadFrame(Frame(20, 200));
            filter.HandleTouchpadFrame(Frame(30, 200));
            Assert.Equal(original.Left + expectedDeltaX, overlay.Bounds.Left);
        }

        [Fact]
        public void DetentStepMovesInWholeStepsAndIgnoresBoundaryNoise()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            filter.TouchpadDragStepPixels = 100f;

            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100)); // gesture starts here
            Assert.Equal(original, overlay.Bounds);

            filter.HandleTouchpadFrame(Frame(20, 150)); // 46.9 px, below half a step
            Assert.Equal(original.Left, overlay.Bounds.Left);

            filter.HandleTouchpadFrame(Frame(30, 170)); // 65.6 px, first detent pending
            Assert.Equal(original.Left, overlay.Bounds.Left);

            filter.HandleTouchpadFrame(Frame(40, 230)); // first detent committed, still one step
            Assert.Equal(original.Left + 100, overlay.Bounds.Left);
            filter.HandleTouchpadFrame(Frame(50, 280)); // 168.8 px pending second detent
            Assert.Equal(original.Left + 100, overlay.Bounds.Left);
            filter.HandleTouchpadFrame(Frame(60, 280)); // second detent committed
            Assert.Equal(original.Left + 200, overlay.Bounds.Left);
        }

        [Fact]
        public void DetentStepWorksInNegativeDirection()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            filter.TouchpadDragStepPixels = 100f;

            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100));
            filter.HandleTouchpadFrame(Frame(30, 280));
            Assert.Equal(original.Left, overlay.Bounds.Left);

            filter.HandleTouchpadFrame(Frame(40, 220)); // 112.5 px -> one step back pending
            Assert.Equal(original.Left + 200, overlay.Bounds.Left);
            filter.HandleTouchpadFrame(Frame(50, 120)); // 18.8 px -> zero step pending
            Assert.Equal(original.Left + 100, overlay.Bounds.Left);
            filter.HandleTouchpadFrame(Frame(60, 120));
            Assert.Equal(original.Left, overlay.Bounds.Left);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(-20f)]
        public void InvalidDetentStepFallsBackToContinuous(float step)
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            filter.TouchpadDragStepPixels = step;
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100));
            filter.HandleTouchpadFrame(Frame(20, 500));
            filter.HandleTouchpadFrame(Frame(30, 500));
            Assert.Equal(original.Left + 375, overlay.Bounds.Left);
        }

        [Fact]
        public void LockedAxisIgnoresSmallCrossAxisDrift()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisLock;
            filter.TouchpadDragAxisLockRatio = 1.5f;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 0, 100)); // starts a vertical drag
            filter.HandleTouchpadFrame(Frame(20, 5, 200)); // locks vertical, 53 px pending
            Assert.Equal(original.Left, overlay.Bounds.Left);
            Assert.Equal(original.Top, overlay.Bounds.Top);

            filter.HandleTouchpadFrame(Frame(30, 10, 300)); // commits 53 px, 105 px pending
            Assert.Equal(original.Left, overlay.Bounds.Left);
            Assert.Equal(original.Top + 53, overlay.Bounds.Top);

            filter.HandleTouchpadFrame(Frame(40, 10, 300));
            Assert.Equal(original.Left, overlay.Bounds.Left);
            Assert.Equal(original.Top + 105, overlay.Bounds.Top);
        }

        [Fact]
        public void ComparableAxesStayDiagonalUntilOneDominates()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisLock;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100, 100));
            filter.HandleTouchpadFrame(Frame(20, 200, 200));
            Assert.Equal(original.Left, overlay.Bounds.Left);
            filter.HandleTouchpadFrame(Frame(30, 200, 200));
            Assert.Equal(original.Left + 94, overlay.Bounds.Left);
            Assert.Equal(original.Top + 53, overlay.Bounds.Top);
        }

        [Fact]
        public void LockedAxisSwitchesOnlyWhenOtherAxisExceedsReleaseRatio()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisLock;
            filter.TouchpadDragAxisReleaseRatio = 2.5f;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 0, 100));
            filter.HandleTouchpadFrame(Frame(20, 5, 200)); // locks vertical
            filter.HandleTouchpadFrame(Frame(30, 5, 300)); // commits 53 px
            filter.HandleTouchpadFrame(Frame(40, 800, 310)); // commits 105 px, then switches
            Assert.Equal(original.Left, overlay.Bounds.Left);
            Assert.Equal(original.Top + 105, overlay.Bounds.Top);

            filter.HandleTouchpadFrame(Frame(50, 900, 310)); // 94 px pending
            Assert.Equal(original.Left, overlay.Bounds.Left);
            filter.HandleTouchpadFrame(Frame(60, 900, 310));
            Assert.Equal(original.Left + 94, overlay.Bounds.Left);
            Assert.Equal(original.Top + 105, overlay.Bounds.Top);
        }

        [Fact]
        public void ForcedHorizontalModeIgnoresVerticalMovement()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisHorizontal;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100, 0));
            filter.HandleTouchpadFrame(Frame(20, 100, 300));
            filter.HandleTouchpadFrame(Frame(30, 200, 300));
            Assert.Equal(original.Top, overlay.Bounds.Top);
            filter.HandleTouchpadFrame(Frame(40, 200, 300));
            Assert.Equal(original.Left + 94, overlay.Bounds.Left);
        }

        [Fact]
        public void ForcedVerticalModeIgnoresHorizontalMovement()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisVertical;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 0, 100));
            filter.HandleTouchpadFrame(Frame(20, 300, 100));
            filter.HandleTouchpadFrame(Frame(30, 300, 200));
            Assert.Equal(original.Left, overlay.Bounds.Left);
            filter.HandleTouchpadFrame(Frame(40, 300, 200));
            Assert.Equal(original.Top + 53, overlay.Bounds.Top);
        }

        [Fact]
        public void FreeModeKeepsBothAxes()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisFree;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100, 100));
            filter.HandleTouchpadFrame(Frame(20, 200, 200));
            filter.HandleTouchpadFrame(Frame(30, 200, 200));
            Assert.Equal(original.Left + 94, overlay.Bounds.Left);
            Assert.Equal(original.Top + 53, overlay.Bounds.Top);
        }

        [Fact]
        public void UnknownAxisValueFallsBackToFreeMovement()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = "旧配置里的未知值";

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100, 100));
            filter.HandleTouchpadFrame(Frame(20, 200, 200));
            filter.HandleTouchpadFrame(Frame(30, 200, 200));
            Assert.Equal(original.Left + 94, overlay.Bounds.Left);
            Assert.Equal(original.Top + 53, overlay.Bounds.Top);
        }

        [Fact]
        public void HigherLockRatioAllowsLooserDiagonalMovement()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisLock;
            filter.TouchpadDragAxisLockRatio = 5f;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100, 200));
            filter.HandleTouchpadFrame(Frame(20, 200, 400)); // ratio 2 < 5, no lock
            filter.HandleTouchpadFrame(Frame(30, 200, 400));
            Assert.Equal(original.Left + 94, overlay.Bounds.Left);
            Assert.Equal(original.Top + 105, overlay.Bounds.Top);
        }

        [Fact]
        public void LiftDiscardsTheFinalUncommittedMovement()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            var original = overlay.Bounds;
            filter.TouchpadDragSensitivity = 1f;
            filter.TouchpadDragAxis = VoiDPlugins.Filter.PrecisionControl.TouchpadAxisLock;

            filter.HandleTouchpadFrame(Frame(0, 0, 0));
            filter.HandleTouchpadFrame(Frame(10, 0, 100));
            filter.HandleTouchpadFrame(Frame(20, 0, 200)); // 53 px pending
            filter.HandleTouchpadFrame(Frame(30, 0, 300)); // commits 53, 105 pending
            filter.HandleTouchpadFrame(Frame(40, 0, 700)); // commits 105, 316 pending
            Assert.Equal(original.Top + 105, overlay.Bounds.Top);

            // Finger release often reports one last distorted frame; it must not move the box.
            filter.HandleTouchpadFrame(new TouchpadFrame(50, Array.Empty<TouchpadContact>(), 4096, 4096));
            Assert.Equal(original.Top + 105, overlay.Bounds.Top);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);
        }

        private static void DragTo(
            VoiDPlugins.Filter.PrecisionControl filter,
            TestOverlay overlay,
            int expectedLeft)
        {
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100));
            filter.HandleTouchpadFrame(Frame(20, 500));
            filter.HandleTouchpadFrame(Frame(30, 500));
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
