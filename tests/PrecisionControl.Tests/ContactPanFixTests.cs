using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTabletDriver.Plugin.Tablet;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void ContactPanResolvesSurfaceUsingActualMappedPenPosition(bool mappingActive)
        {
            using var h = ContactNavigation(mappingActive);
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            DoubleTap(h, 10);
            h.At(210, x: 1500, y: 1000);
            var mapped = h.Trace.Last.Position;
            h.At(220, x: 1500, y: 1000, pressure: 512);
            Assert.Equal(mapped, native.PreferredAnchor);
            var down = Assert.Single(native.Frames);
            Assert.Equal(native.Surface.CenterX - 24, down[0].Touch.Info.Pixel.X);
            Assert.Equal(native.Surface.CenterY, down[0].Touch.Info.Pixel.Y);
            Assert.Equal(0u, h.Trace.Last.Pressure);
        }

        [Theory]
        [InlineData(5000, 5000)]
        [InlineData(-5000, -5000)]
        [InlineData(5000, -5000)]
        public void TouchesClampInsideClientAndNeverRestoreInkAtTheEdge(int dx, int dy)
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, x: 1000 + dx, y: 800 + dy, pressure: 512);
            h.At(240, x: 1000 + dx, y: 800 + dy, pressure: 512);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Trace.TipDownCount);
            Assert.Equal(0, native.Destroyed);
            Assert.All(native.Frames.SelectMany(f => f), contact =>
            {
                Assert.InRange(contact.Touch.Info.Pixel.X, native.Surface.Left, native.Surface.Right - 1);
                Assert.InRange(contact.Touch.Info.Pixel.Y, native.Surface.Top, native.Surface.Bottom - 1);
            });
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void TenLargeContactStrokesRemainInPageModeWithMappingEnabled()
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            var original = h.Overlay.Bounds;
            DoubleTap(h, 10);
            for (var i = 0; i < 10; i++)
            {
                var t = 300 + i * 300;
                h.At(t, pressure: 512);
                h.At(t + 10, x: 3500, y: 1800, pressure: 512);
                h.At(t + 20, x: 3500, y: 1800);
                h.At(t + 150); // complete stationary release, re-center next stroke
                Assert.True(h.Filter.IsPenPageDragMode);
                Assert.Equal(0, h.Trace.TipDownCount);
            }
            Assert.Equal(10, native.Created);
            Assert.Equal(10, native.Destroyed);
            Assert.Equal(original, h.Overlay.Bounds);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void LiftSendsStationaryFramesBeforeCancelWithoutQueuedMovement()
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            ManualPageSettleTimer? timer = null;
            h.Filter.PenPageSettleScheduler = tick => timer = new ManualPageSettleTimer(tick);
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, x: 1080, y: 840, pressure: 512);
            var final = native.Frames[^1];
            h.At(240, x: 1080, y: 840);
            Assert.Equal(0, native.Destroyed); // no UP at physical lift
            timer!.Fire();
            timer.Fire();
            Assert.Equal(0, native.Destroyed);
            timer.Fire();
            Assert.Equal(1, native.Destroyed);
            Assert.True(timer.Disposed);
            Assert.Equal(0x48000u, native.Frames[^1][0].Touch.Info.Flags);
            foreach (var frame in native.Frames.Skip(2))
                for (var i = 0; i < 2; i++)
                {
                    Assert.Equal(final[i].Touch.Info.Pixel.X, frame[i].Touch.Info.Pixel.X);
                    Assert.Equal(final[i].Touch.Info.Pixel.Y, frame[i].Touch.Info.Pixel.Y);
                }
        }

        [Fact]
        public void StaleSettleCallbackCannotEndNewContactOrReplayMovement()
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            var timers = new List<ManualPageSettleTimer>();
            h.Filter.PenPageSettleScheduler = tick =>
            { var timer = new ManualPageSettleTimer(tick); timers.Add(timer); return timer; };
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, x: 1080, y: 840, pressure: 512);
            h.At(240);
            h.At(250, x: 700, y: 500, pressure: 512); // rapid clutch uses same contacts
            h.At(260, x: 710, y: 505, pressure: 512);
            var frames = native.Frames.Count;
            timers[0].Fire(evenIfDisposed: true);
            Assert.Equal(frames, native.Frames.Count);
            Assert.Equal(0, native.Destroyed);
            Assert.Equal(1, native.Created);
            var last = native.Frames[^1][0].Touch.Info.Pixel;
            Assert.Equal(native.Surface.CenterX - 24 + 90, last.X);
            Assert.Equal(native.Surface.CenterY + 45, last.Y);
        }

        [Fact]
        public void DisposingDuringSettleCancelsCallbacksAndCleansDeviceOnce()
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            ManualPageSettleTimer? timer = null;
            h.Filter.PenPageSettleScheduler = tick => timer = new ManualPageSettleTimer(tick);
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230);
            h.Filter.Dispose();
            var count = native.Frames.Count;
            timer!.Fire(evenIfDisposed: true);
            Assert.Equal(count, native.Frames.Count);
            Assert.Equal(1, native.Destroyed);
        }

        [Theory]
        [InlineData("surface")]
        [InlineData("inject")]
        public void NativeFailureStaysInPageModeAndCannotTurnLaterStrokesIntoInk(string failure)
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            DoubleTap(h, 10);
            if (failure == "surface") native.SurfaceAvailable = false;
            else native.InjectionWorks = false;
            h.At(220, pressure: 512);
            h.At(230, y: 900, pressure: 512);
            h.At(240);
            h.At(250, pressure: 512);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Trace.TipDownCount);
            h.At(260);
            native.SurfaceAvailable = native.InjectionWorks = true;
            DoubleTap(h, 400);
            h.At(610, pressure: 512);
            Assert.Equal(1, h.Trace.TipDownCount);
        }

        [Fact]
        public void FocusChangeAndLeavingRangeCannotSilentlyUnlockInk()
        {
            using var h = ContactNavigation();
            h.Filter.PenDirectPanFactory = () => new FakeDirectPan();
            DoubleTap(h, 10);
            h.Window = new IntPtr(2);
            h.Filter.Consume(new OutOfRangeReport(Array.Empty<byte>()));
            h.At(220);
            h.At(230, pressure: 512);
            h.At(240);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Trace.TipDownCount);
            DoubleTap(h, 400); // explicit exit also works while paused in another app
            h.At(610, pressure: 512);
            Assert.False(h.Filter.IsPenPageDragMode);
            Assert.Equal(1, h.Trace.TipDownCount);
        }

        private sealed class FakePanPlatform : IPenPanPlatform
        {
            public bool Ready => true;
            public string? Error => null;
            internal bool SurfaceAvailable = true, InjectionWorks = true;
            internal int Created, Destroyed;
            internal PenPanSurface Surface = new(100, 200, 1700, 1100);
            internal Vector2? PreferredAnchor;
            internal readonly List<PenDirectPan.TouchFrame[]> Frames = new();
            public bool ContextValid(IntPtr target) => target != IntPtr.Zero;
            public bool ResolveSurface(IntPtr target, Vector2? preferred, out PenPanSurface surface)
            { PreferredAnchor = preferred; surface = Surface; return SurfaceAvailable; }
            public IntPtr Create() { Created++; return new IntPtr(Created); }
            public bool Inject(IntPtr device, PenDirectPan.TouchFrame[] frames)
            { if (!InjectionWorks) return false; Frames.Add((PenDirectPan.TouchFrame[])frames.Clone()); return true; }
            public void Destroy(IntPtr device) => Destroyed++;
        }
    }
}
