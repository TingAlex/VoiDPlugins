using System;
using System.Linq;
using System.Reflection;
using OpenTabletDriver.Plugin.Attributes;
using VoiDPlugins.Filter;
using Xunit;

namespace PrecisionControl.Tests
{
    public sealed partial class PrecisionControlTests
    {
        [Fact]
        public void PageNavigationIsOffByDefaultAndCannotBeConfiguredByOtdMetadata()
        {
            using var filter = new VoiDPlugins.Filter.PrecisionControl();
            Assert.False(filter.EnablePenPageNavigation);
            Assert.False(filter.EnablePenDoubleTapPagePan);
            Assert.Null(typeof(VoiDPlugins.Filter.PrecisionControl).GetProperty("EnablePenPageNavigation"));
            foreach (var name in new[] { "EnablePenDoubleTapPagePan", "PenDoubleTapIntervalMilliseconds",
                "PenPageJitterCoefficient", "PenScrollSensitivity", "PenScrollNaturalDirection", "PenScrollMode" })
            {
                var property = typeof(VoiDPlugins.Filter.PrecisionControl).GetProperty(name)!;
                Assert.NotNull(property);
                Assert.Null(property.GetCustomAttribute<PropertyAttribute>());
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void DisabledPageGateIgnoresRetainedExperimentalParameters(bool doubleTap)
        {
            using var h = new NavigationHarness();
            var pan = new FakeDirectPan();
            h.Filter.EnablePenPageNavigation = false;
            h.Filter.EnablePenDoubleTapPagePan = doubleTap;
            h.Filter.PenScrollMode = VoiDPlugins.Filter.PrecisionControl.DirectTouchPanMode;
            h.Filter.PenDirectPanFactory = () => pan;
            h.At(10, down: true);
            h.At(400, down: true);
            h.At(410, x: 1100, y: 900, down: true);
            h.At(420);
            Assert.Equal(0, pan.BeginCount);
            Assert.Empty(h.Scrolls);
            Assert.False(h.Filter.IsPenPageDragMode);
            Assert.Equal(0, h.Overlay.HideCount);
        }

        [Fact]
        public void StableThirdButtonTapIsImmediateAndContactDragStillMovesMapping()
        {
            using var h = new NavigationHarness();
            h.Filter.EnablePenPageNavigation = false;
            h.Filter.EnablePenDoubleTapPagePan = true; // even stale in-memory opt-in cannot delay single tap
            h.At(10, down: true);
            h.At(110);
            Assert.Equal(1, h.Overlay.HideCount);
            h.At(150, down: true);
            h.At(250);
            Assert.Equal(2, h.Overlay.ShowCount);
            Assert.False(h.Filter.IsPenPageDragMode);
            var bounds = h.Overlay.Bounds;
            h.At(300, down: true);
            h.At(310, pressure: 512, down: true);
            h.At(320, x: 1030, y: 820, pressure: 512, down: true);
            Assert.Equal(bounds.Left + 30, h.Overlay.Bounds.Left);
            Assert.Equal(bounds.Top + 20, h.Overlay.Bounds.Top);
            Assert.Equal(0, h.Trace.TipDownCount);
            Assert.Empty(h.Scrolls);
        }

        [Fact]
        public void DisablingPageGateDuringContactEndsTouchesAndSuppressesInkTail()
        {
            using var h = ContactNavigation();
            var pan = new FakeDirectPan();
            h.Filter.PenDirectPanFactory = () => pan;
            DoubleTap(h, 10);
            h.At(220, pressure: 512);
            h.Filter.EnablePenPageNavigation = false;
            h.At(230, pressure: 512);
            Assert.Equal(1, pan.EndCount);
            Assert.False(h.Filter.IsPenPageDragMode);
            Assert.Equal(0u, h.Trace.Last.Pressure);
            h.At(240);
            h.At(250, pressure: 512);
            Assert.Equal(512u, h.Trace.Last.Pressure);
        }

        [Fact]
        public void VisibleSettingsHaveSevenGroupsHelpAndConsistentSliderDefaults()
        {
            var properties = typeof(VoiDPlugins.Filter.PrecisionControl).GetProperties()
                .Where(p => p.GetCustomAttribute<PropertyAttribute>() != null).ToArray();
            var groups = properties.Select(p => p.GetCustomAttribute<PropertyAttribute>()!.DisplayName.Split(' ')[0])
                .Distinct().ToArray();
            Assert.Equal(new[] { "01", "02", "03", "04", "05", "06", "07" }, groups);
            Assert.Equal(23, properties.Length);
            Assert.All(properties, p =>
            {
                Assert.False(string.IsNullOrWhiteSpace(p.GetCustomAttribute<ToolTipAttribute>()?.ToolTip));
                var slider = p.GetCustomAttribute<SliderPropertyAttribute>();
                if (slider != null)
                    Assert.Equal(Convert.ToSingle(p.GetCustomAttribute<DefaultPropertyValueAttribute>()!.Value), slider.DefaultValue);
            });
        }
    }
}
