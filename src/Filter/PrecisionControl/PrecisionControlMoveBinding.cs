using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.Tablet;

namespace VoiDPlugins.Filter
{
    [PluginName("Precision Control Move Area (Dial)")]
    public sealed class PrecisionControlMoveBinding : IStateBinding
    {
        public static string[] ValidDirections => new[] { "Left", "Right", "Up", "Down" };

        [Property("Direction"), PropertyValidated(nameof(ValidDirections))]
        [DefaultPropertyValue("Right")]
        public string Direction { get; set; } = "Right";

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            PrecisionControlCoordinator.MoveFromDial(tablet, Direction);
        }

        public void Release(TabletReference tablet, IDeviceReport report) { }
    }
}
