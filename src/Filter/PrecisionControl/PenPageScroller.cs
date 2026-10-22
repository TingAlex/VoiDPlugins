using System;
using System.Runtime.InteropServices;

namespace VoiDPlugins.Filter
{
    // Wheel output only: no pointer move, click, keyboard injection or focus stealing.
    internal static class PenPageScroller
    {
        private static readonly bool Available = ProbeApi();
        internal static bool IsAvailable => Available;
        internal static IntPtr CurrentWindow => Available ? GetForegroundWindow() : IntPtr.Zero;

        internal static bool TryScroll(int horizontal, int vertical, IntPtr target)
        {
            if (!Available || target == IntPtr.Zero || GetForegroundWindow() != target)
                return false;
            // Never reinterpret wheel input as Ctrl+zoom or Shift/Alt navigation.
            foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((GetAsyncKeyState(key) & 0x8000) != 0)
                    return false;
            if (!GetCursorPos(out var point) || GetAncestor(WindowFromPoint(point), 2) != target)
                return false;
            var inputs = BuildInputs(horizontal, vertical);
            return inputs.Length == 0 || SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<WheelInput>()) == (uint)inputs.Length;
        }

        internal static WheelInput[] BuildInputs(int horizontal, int vertical)
        {
            var result = new WheelInput[(horizontal == 0 ? 0 : 1) + (vertical == 0 ? 0 : 1)];
            var index = 0;
            if (horizontal != 0)
                result[index++] = Create(0x1000, horizontal);
            if (vertical != 0)
                result[index] = Create(0x0800, vertical);
            return result;
        }

        private static WheelInput Create(uint flags, int delta) => new()
        {
            Mouse = new WheelMouseInput { Flags = flags, Data = unchecked((uint)delta) }
        };

        private static bool ProbeApi()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                !NativeLibrary.TryLoad("user32.dll", out var library))
                return false;
            try
            {
                foreach (var name in new[] { "SendInput", "GetForegroundWindow", "GetAsyncKeyState", "GetCursorPos", "WindowFromPoint", "GetAncestor" })
                    if (!NativeLibrary.TryGetExport(library, name, out _))
                        return false;
                return true;
            }
            finally { NativeLibrary.Free(library); }
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct WheelInput { public uint Type; public WheelMouseInput Mouse; }
        [StructLayout(LayoutKind.Sequential)]
        internal struct WheelMouseInput
        {
            public int X, Y;
            public uint Data, Flags, Time;
            public UIntPtr ExtraInfo;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct Point { public int X, Y; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, [In] WheelInput[] inputs, int size);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);
    }
}
