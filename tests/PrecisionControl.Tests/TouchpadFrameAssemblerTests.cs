using System;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed class TouchpadFrameAssemblerTests
    {
        [Fact]
        public void ParallelFrameConsumesReportedCountBeforeIgnoringUnusedSlots()
        {
            var assembler = new TouchpadFrameAssembler();
            var frame = assembler.AddReport(0, new[] {
                Contact(1), Contact(2, false), Contact(3), Contact(0, false), Contact(0, false)
            }, 3, 4096, 2048, 1);
            Assert.Equal(3, frame!.Contacts.Count);
            Assert.False(frame.Contacts[1].IsTipDown);
            Assert.Equal(2048, frame.VerticalRange);
        }

        [Fact]
        public void HybridFrameCanStartWithOnlyLiftedRecords()
        {
            var assembler = new TouchpadFrameAssembler();
            Assert.Null(assembler.AddReport(0, new[] { Contact(1, false) }, 3, 4096, 4096, 1));
            Assert.Null(assembler.AddReport(1, new[] { Contact(2) }, 0, 4096, 4096, 1));
            var frame = assembler.AddReport(2, new[] { Contact(3) }, 0, 4096, 4096, 1);
            Assert.Equal(3, frame!.Contacts.Count);
            Assert.False(frame.Contacts[0].IsTipDown);
            Assert.True(frame.Contacts[2].IsTipDown);
        }

        [Theory]
        [InlineData(101, 1)] // a continuation from a different scan
        [InlineData(100, 41)] // an obsolete partial frame
        public void MismatchedOrStaleContinuationClearsPartialFrame(uint scanTime, long time)
        {
            var assembler = new TouchpadFrameAssembler();
            Assert.Null(assembler.AddReport(0, new[] { Contact(1), Contact(2) }, 3, 4096, 4096, 100));
            var invalid = assembler.AddReport(time, new[] { Contact(3) }, 0, 4096, 4096, scanTime);
            Assert.Empty(invalid!.Contacts);
            var orphan = assembler.AddReport(time + 1, new[] { Contact(3) }, 0, 4096, 4096, scanTime);
            Assert.Empty(orphan!.Contacts);
        }

        [Fact]
        public void NewHeaderReplacesIncompleteFrame()
        {
            var assembler = new TouchpadFrameAssembler();
            Assert.Null(assembler.AddReport(0, new[] { Contact(1) }, 3, 4096, 4096, 100));
            Assert.Null(assembler.AddReport(1, new[] { Contact(4), Contact(5) }, 3, 4096, 4096, 101));
            var frame = assembler.AddReport(2, new[] { Contact(6) }, 0, 4096, 4096, 101);
            Assert.Equal(new[] { Contact(4), Contact(5), Contact(6) }, frame!.Contacts);
        }

        [Fact]
        public void DuplicateIdsCancelInsteadOfCompletingWithStaleCoordinates()
        {
            var assembler = new TouchpadFrameAssembler();
            Assert.Null(assembler.AddReport(0, new[] { Contact(1), Contact(2) }, 3, 4096, 4096, 100));
            var invalid = assembler.AddReport(1, new[] { Contact(2) }, 0, 4096, 4096, 100);
            Assert.Empty(invalid!.Contacts);
        }

        [Fact]
        public void DeviceAssemblersCannotCompleteEachOthersPartialFrames()
        {
            var first = new TouchpadFrameAssembler();
            var second = new TouchpadFrameAssembler();
            Assert.Null(first.AddReport(0, new[] { Contact(1), Contact(2) }, 3, 4096, 4096, 100));
            var orphan = second.AddReport(1, new[] { Contact(3) }, 0, 4096, 4096, 100);
            Assert.Empty(orphan!.Contacts);
            var complete = first.AddReport(2, new[] { Contact(3) }, 0, 4096, 4096, 100);
            Assert.Equal(3, complete!.Contacts.Count);
        }

        [Fact]
        public void EmptyReportDropsPendingRecords()
        {
            var assembler = new TouchpadFrameAssembler();
            Assert.Null(assembler.AddReport(0, new[] { Contact(1) }, 3, 4096, 4096));
            var release = assembler.AddReport(1, Array.Empty<TouchpadContact>(), 0, 4096, 4096);
            Assert.Empty(release!.Contacts);
            var orphan = assembler.AddReport(2, new[] { Contact(2), Contact(3) }, 0, 4096, 4096);
            Assert.Empty(orphan!.Contacts);
        }

        private static TouchpadContact Contact(int id, bool tip = true) => new(id, id * 500, 800, tip);
    }
}
