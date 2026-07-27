using System.Numerics;
using OpenTabletDriver.Plugin.Platform.Display;
using OpenTabletDriver.Plugin.Platform.Pointer;
using OpenTabletDriver.Plugin.Tablet;

namespace VoiDPlugins.OutputMode
{
    public unsafe class WinInkAbsolutePointer : WinInkBasePointer, IAbsolutePointer
    {
        public WinInkAbsolutePointer(TabletReference tabletReference, IVirtualScreen screen)
            : base("Windows Ink", tabletReference, screen)
        {
        }

        public void SetPosition(Vector2 pos)
        {
            var screenPosition = pos;
            var reportAsInk = PreparePosition(screenPosition);
            var inkPosition = Convert(screenPosition);
            RawPointer->X = (ushort)inkPosition.X;
            RawPointer->Y = (ushort)inkPosition.Y;
            Dirty = reportAsInk;
        }
    }
}
