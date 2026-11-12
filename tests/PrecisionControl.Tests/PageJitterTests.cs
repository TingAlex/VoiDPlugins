using System;
using System.Linq;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Fact]
        public void ZeroJitterCoefficientPreservesUnfilteredDisplacement()
        {
            var guard = new PenPageJitterGuard();
            guard.Reset(0);
            foreach (var p in new[] { (0.1, -0.2), (100.5, 80.25), (-200.5, 0.75), (0d, 0d) })
            {
                Assert.True(guard.TryFilter(p.Item1, p.Item2, out var result));
                Assert.Equal(p, result);
            }
        }

        [Theory]
        [InlineData(1, 1)]
        [InlineData(5, 10)]
        [InlineData(20, 50)]
        public void FastAndSlowVerticalDragsSuppressSideJitterWithoutSpeedAcceleration(int frames, int dt)
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            h.Filter.PenPageJitterCoefficient = 2;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            for (var i = 1; i <= frames; i++)
                h.At(220 + i * dt, x: 1000 + (i % 2 == 0 ? 1.5f : -1.5f),
                    y: 800 + 100f * i / frames, pressure: 512);
            Assert.All(pan.Positions, p => Assert.Equal(0d, p.Item1));
            Assert.Equal((0d, 98d), pan.Positions[^1]);
            Assert.Equal(0, h.Trace.TipDownCount);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(6)]
        [InlineData(10)]
        public void JitterGuardHasBoundedPositionErrorInsteadOfReducingMovementGain(double coefficient)
        {
            var guard = new PenPageJitterGuard();
            guard.Reset(coefficient);
            for (var i = 0; i <= 500; i++)
            {
                var x = i * 10d;
                var y = Math.Sin(i * 0.2) * 100;
                Assert.True(guard.TryFilter(x, y, out var result));
                Assert.InRange(Math.Abs(result.X - x), 0, coefficient + 1e-9);
                Assert.InRange(Math.Abs(result.Y - y), 0, coefficient + 1e-9);
            }
            Assert.True(guard.TryFilter(5100, 0, out var last));
            Assert.Equal(5100 - coefficient, last.X);
        }

        [Fact]
        public void HigherCoefficientSuppressesLargerBackAndForthMovement()
        {
            var weak = new PenPageJitterGuard();
            var strong = new PenPageJitterGuard();
            weak.Reset(1);
            strong.Reset(4);
            foreach (var x in new[] { 3d, -3d, 3d, -3d })
            {
                Assert.True(weak.TryFilter(x, 0, out var small));
                Assert.True(strong.TryFilter(x, 0, out var large));
                Assert.NotEqual(0d, small.X);
                Assert.Equal(0d, large.X);
            }
        }

        [Fact]
        public void SmallContactNoiseDoesNotEmitAnyPageMove()
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            h.Filter.PenPageJitterCoefficient = 2;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            for (var i = 1; i <= 100; i++)
                h.At(220 + i * 5, x: 1000 + (i % 2 == 0 ? 1.5f : -1.5f),
                    y: 800 + (i % 3 == 0 ? 1f : -1f), pressure: 512);
            Assert.Single(native.Frames); // DOWN only; no movement packets
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Trace.TipDownCount);
        }

        [Fact]
        public void StationaryInputAndLiftNeverCatchUpToUnfilteredPosition()
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            h.Filter.PenPageJitterCoefficient = 4;
            ManualPageSettleTimer? timer = null;
            h.Filter.PenPageSettleScheduler = tick => timer = new ManualPageSettleTimer(tick);
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, x: 1080, y: 840, pressure: 512);
            var final = native.Frames[^1];
            Assert.Equal(native.Surface.CenterX - 24 + 76, final[0].Touch.Info.Pixel.X);
            Assert.Equal(native.Surface.CenterY + 36, final[0].Touch.Info.Pixel.Y);
            var count = native.Frames.Count;
            for (var i = 1; i <= 20; i++)
                h.At(230 + i * 10, x: 1080, y: 840, pressure: 512);
            Assert.Equal(count, native.Frames.Count);
            h.At(440, x: 1200, y: 1000); // lift's raw coordinates must never flush into pan
            timer!.Fire(); timer.Fire(); timer.Fire();
            foreach (var frame in native.Frames.Skip(count))
                for (var i = 0; i < 2; i++)
                {
                    Assert.Equal(final[i].Touch.Info.Pixel.X, frame[i].Touch.Info.Pixel.X);
                    Assert.Equal(final[i].Touch.Info.Pixel.Y, frame[i].Touch.Info.Pixel.Y);
                }
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void RapidClutchResetsJitterStateWithoutLeakingPreviousStrokeOffset()
        {
            using var h = ContactNavigation();
            var native = new FakePanPlatform();
            h.Filter.PenDirectPanFactory = () => new PenDirectPan(native);
            h.Filter.PenPageJitterCoefficient = 2;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, x: 1100, y: 850, pressure: 512); // 98,48 filtered
            h.At(240);
            h.At(250, x: 700, y: 500, pressure: 512);
            h.At(260, x: 710, y: 505, pressure: 512); // new contact: 8,3 filtered
            var last = native.Frames[^1][0].Touch.Info.Pixel;
            Assert.Equal(native.Surface.CenterX - 24 + 106, last.X);
            Assert.Equal(native.Surface.CenterY + 51, last.Y);
            Assert.Equal(1, native.Created);
            Assert.Equal(0, h.Trace.TipDownCount);
        }

        [Fact]
        public void CoefficientChangesTakeEffectOnNextContactWithoutMidStrokeJump()
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            h.Filter.PenPageJitterCoefficient = 2;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.At(230, x: 1080, pressure: 512);
            Assert.Equal((78d, 0d), pan.Positions[^1]);
            h.Filter.PenPageJitterCoefficient = 10;
            var count = pan.Positions.Count;
            h.At(240, x: 1080, pressure: 512);
            Assert.Equal(count, pan.Positions.Count);
            h.At(250);
            h.At(260, pressure: 512);
            h.At(270, x: 1005, pressure: 512);
            Assert.Equal(count, pan.Positions.Count); // 5 px is inside new 10 px band
            h.At(280, x: 1020, pressure: 512);
            Assert.Equal((10d, 0d), pan.Positions[^1]);
        }

        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(-1f)]
        [InlineData(11f)]
        public void InvalidJitterSettingPausesWithoutResumingInk(float coefficient)
        {
            using var h = ContactNavigation();
            h.Filter.PenDirectPanFactory = () => new FakeDirectPan();
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.Filter.PenPageJitterCoefficient = coefficient;
            h.At(230, pressure: 512);
            h.At(240);
            h.At(250, pressure: 512);
            Assert.True(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Trace.TipDownCount);
            Assert.Equal(0u, h.Trace.Last.Pressure);
        }
    }
}
