using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using OpenTabletDriver.Plugin.Tablet;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Theory]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(20)]
        public void DirectPanUsesTotalPixelDisplacementIndependentOfFrameCount(int frames)
        {
            using var h = new NavigationHarness();
            var pan = new FakeDirectPan();
            h.Filter.PenScrollMode = VoiDPlugins.Filter.PrecisionControl.DirectTouchPanMode;
            h.Filter.PenDirectPanFactory = () => pan;
            var area = h.Overlay.Bounds;
            h.At(0, down: true);
            h.At(300, down: true);
            Assert.Equal(1, pan.BeginCount);
            for (var i = 1; i <= frames; i++)
                h.At(300 + i * 5, x: 1000 + 80f * i / frames, y: 800 + 40f * i / frames, down: true);
            Assert.Equal((80d, 40d), pan.Positions[^1]);
            Assert.Empty(h.Scrolls);
            Assert.Equal(area, h.Overlay.Bounds);
            h.At(500, x: 1080, y: 840);
            Assert.Equal(1, pan.EndCount);
            var count = pan.Positions.Count;
            h.At(510, x: 1200, y: 1000);
            Assert.Equal(count, pan.Positions.Count); // no post-release continuation
        }

        [Fact]
        public void StationaryDirectPanDoesNotAdvanceWithTime()
        {
            using var h = new NavigationHarness();
            var pan = new FakeDirectPan();
            h.Filter.PenScrollMode = VoiDPlugins.Filter.PrecisionControl.DirectTouchPanMode;
            h.Filter.PenDirectPanFactory = () => pan;
            h.At(0, down: true);
            h.At(300, down: true);
            h.At(310, y: 820, down: true);
            h.At(500, y: 820, down: true);
            h.At(1000, y: 820, down: true);
            Assert.All(pan.Positions, p => Assert.Equal((0d, 20d), p));
            Assert.Empty(h.Scrolls);
        }

        [Theory]
        [InlineData("release")]
        [InlineData("contact")]
        [InlineData("focus")]
        [InlineData("range")]
        [InlineData("disable")]
        [InlineData("toggle")]
        public void DirectContactsAreEndedForEveryExitPath(string exit)
        {
            using var h = new NavigationHarness();
            var pan = new FakeDirectPan();
            h.Filter.PenScrollMode = VoiDPlugins.Filter.PrecisionControl.DirectTouchPanMode;
            h.Filter.PenDirectPanFactory = () => pan;
            h.At(0, down: true);
            h.At(300, down: true);
            switch (exit)
            {
                case "release": h.At(310); break;
                case "contact": h.At(310, pressure: 512, down: true); break;
                case "focus": h.Window = new IntPtr(2); h.At(310, down: true); break;
                case "range": h.Filter.Consume(new OutOfRangeReport(Array.Empty<byte>())); break;
                case "disable": h.Filter.EnablePenNavigation = false; h.At(310, down: true); break;
                case "toggle": h.Filter.QueueGlobalToggle(); break;
            }
            Assert.Equal(1, pan.EndCount);
            Assert.Empty(h.Scrolls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DirectPanFailuresNeverFallBackToWheel(bool beginSucceeds)
        {
            using var h = new NavigationHarness();
            var pan = new FakeDirectPan { BeginSucceeds = beginSucceeds, MoveSucceeds = false };
            h.Filter.PenScrollMode = VoiDPlugins.Filter.PrecisionControl.DirectTouchPanMode;
            h.Filter.PenDirectPanFactory = () => pan;
            h.At(0, down: true);
            h.At(300, down: true);
            h.At(310, y: 820, down: true);
            h.At(320, y: 840, down: true);
            Assert.Equal(1, pan.BeginCount);
            Assert.Equal(1, pan.EndCount);
            Assert.Empty(h.Scrolls);
            h.At(330, y: 840);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void DirectModeRetainsJitterTolerantShortTapWithoutTouchInjection()
        {
            using var h = new NavigationHarness();
            var pan = new FakeDirectPan();
            h.Filter.PenScrollMode = VoiDPlugins.Filter.PrecisionControl.DirectTouchPanMode;
            h.Filter.PenDirectPanFactory = () => pan;
            h.At(0, down: true);
            h.At(100, x: 1050);
            Assert.Equal(1, h.Overlay.HideCount);
            Assert.Equal(0, pan.BeginCount);
            Assert.Empty(pan.Positions);
        }

        [Fact]
        public void DirectTouchFramesMaintainSeparationAndNativeLayout()
        {
            var first = PenDirectPan.BuildFrames(500, 600, 0, 0, 0x10006);
            var next = PenDirectPan.BuildFrames(500, 600, 80, -40, 0x20006);
            Assert.Equal(48, next[1].Touch.Info.Pixel.X - next[0].Touch.Info.Pixel.X);
            for (var i = 0; i < 2; i++)
            {
                Assert.Equal((uint)i, next[i].Touch.Info.Id);
                Assert.Equal(2u, next[i].Type);
                Assert.Equal(80, next[i].Touch.Info.Pixel.X - first[i].Touch.Info.Pixel.X);
                Assert.Equal(-40, next[i].Touch.Info.Pixel.Y - first[i].Touch.Info.Pixel.Y);
            }
            Assert.Equal(IntPtr.Size == 8 ? 144 : 136, Marshal.SizeOf<PenDirectPan.Touch>());
            Assert.Equal(IntPtr.Size == 8 ? 152 : 144, Marshal.SizeOf<PenDirectPan.TouchFrame>());
        }

        private sealed class FakeDirectPan : IPenDirectPan
        {
            public int BeginCount, EndCount, HoldCount, RebaseCount;
            public System.Numerics.Vector2? Anchor;
            public readonly List<(double, double)> Positions = new();
            public bool BeginSucceeds = true, MoveSucceeds = true;
            public string Error => "fake-failure";
            public bool Begin(IntPtr target, System.Numerics.Vector2? preferredAnchor = null) { BeginCount++; Anchor = preferredAnchor; return BeginSucceeds; }
            public bool Move(double x, double y) { Positions.Add((x, y)); return MoveSucceeds; }
            public bool Hold() { HoldCount++; return true; }
            public void Rebase() { RebaseCount++; }
            public void End() { EndCount++; }
        }
    }
}
