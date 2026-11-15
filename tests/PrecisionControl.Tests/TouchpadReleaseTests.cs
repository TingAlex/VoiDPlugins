using System;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Theory]
        [InlineData(0, 0)]
        [InlineData(1, 0)]
        [InlineData(2, 0)]
        [InlineData(0, 40)]
        [InlineData(1, 40)]
        [InlineData(2, 40)]
        public void FirstLiftEndsDragEvenWhileReportedContactCountIsStillThree(int lifted, int step)
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            filter.TouchpadDragStepPixels = step;
            filter.HandleTouchpadFrame(Frame(0, 0));
            filter.HandleTouchpadFrame(Frame(10, 100));
            filter.HandleTouchpadFrame(Frame(20, 500));
            filter.HandleTouchpadFrame(Frame(30, 500));
            var committed = overlay.Bounds;

            // The final distorted three-down frame is pending. The next HID frame
            // still declares three records, but one record has Tip Switch cleared.
            filter.HandleTouchpadFrame(Frame(40, 900));
            var contacts = new TouchpadContact[3];
            for (var index = 0; index < contacts.Length; index++)
                contacts[index] = new TouchpadContact(index + 1, 8000 + index * 500, 2000, index != lifted);
            filter.HandleTouchpadFrame(new TouchpadFrame(50, contacts, 4096, 4096));
            Assert.Equal(committed, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);

            // Remaining fingers keep moving while peeling off, without moving the box.
            filter.HandleTouchpadFrame(Frame(60, 5000, fingers: 2));
            filter.HandleTouchpadFrame(Frame(70, 6000, fingers: 1));
            filter.HandleTouchpadFrame(Frame(80, 0, fingers: 0));
            Assert.Equal(committed, overlay.Bounds);
            filter.HandleTouchpadFrame(Frame(90, -1000));
            filter.HandleTouchpadFrame(Frame(100, -900));
            Assert.Equal(committed, overlay.Bounds);
        }

        [Fact]
        public void HybridReleaseWaitsForAllRecordsBeforeEndingDrag()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            DragTo(filter, overlay, overlay.Bounds.Left + 375);
            var committed = overlay.Bounds;
            filter.HandleTouchpadFrame(Frame(40, 900));
            var assembler = new TouchpadFrameAssembler();
            Assert.Null(assembler.AddReport(50, new[] {
                new TouchpadContact(1, 2000, 800, false),
                new TouchpadContact(2, 9000, 800)
            }, 3, 4096, 4096, 100));
            var release = assembler.AddReport(51, new[] {
                new TouchpadContact(3, 9500, 800)
            }, 0, 4096, 4096, 100);
            Assert.NotNull(release);
            filter.HandleTouchpadFrame(release!);
            Assert.Equal(committed, overlay.Bounds);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);
        }

        [Fact]
        public void AllTipOffRecordsEndDragWithoutAnEmptyReport()
        {
            using var filter = PrepareTouchpadDrag(out var overlay);
            DragTo(filter, overlay, overlay.Bounds.Left + 375);
            var committed = overlay.Bounds;
            var assembler = new TouchpadFrameAssembler();
            var release = assembler.AddReport(40, new[] {
                new TouchpadContact(1, 0, 0, false),
                new TouchpadContact(2, 0, 0, false),
                new TouchpadContact(3, 0, 0, false)
            }, 3, 4096, 4096);
            filter.HandleTouchpadFrame(release!);
            Assert.Equal(TouchpadDragPhase.Idle, filter.TouchpadPhase);
            Assert.Equal(committed, overlay.Bounds);
        }
    }
}
