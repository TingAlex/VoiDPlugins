using System.Runtime.InteropServices;

namespace TouchpadPenProbe;

internal static class NativeMethods
{
    internal const int WM_POINTERUPDATE = 0x0245;
    internal const int WM_POINTERDOWN = 0x0246;
    internal const int WM_POINTERUP = 0x0247;
    internal const int WM_POINTERENTER = 0x0249;
    internal const int WM_POINTERLEAVE = 0x024A;
    internal const int WM_POINTERCAPTURECHANGED = 0x024C;
    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const int WM_GESTURE = 0x0119;
    internal const int WM_GESTURENOTIFY = 0x011A;
    internal const int WM_MOUSEWHEEL = 0x020A;
    internal const int WM_MOUSEHWHEEL = 0x020E;
    internal const int WM_APPCOMMAND = 0x0319;

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterTouchpadCapableWindow(
        IntPtr window,
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RegisterTouchpadCapableThread(
        [MarshalAs(UnmanagedType.Bool)] bool enable);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCurrentInputMessageSource(
        out InputMessageSource inputMessageSource);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerInfo(
        uint pointerId,
        out PointerInfo pointerInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerPenInfo(
        uint pointerId,
        out PointerPenInfo penInfo);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetPointerFrameTouchpadInfo(
        uint pointerId,
        ref uint pointerCount,
        IntPtr touchpadInfo);

    internal static uint GetPointerId(IntPtr wParam) =>
        unchecked((uint)(long)wParam) & 0xFFFF;

    internal static bool HasExport(string name)
    {
        IntPtr library = NativeLibrary.Load("user32.dll");
        try
        {
            return NativeLibrary.TryGetExport(library, name, out _);
        }
        finally
        {
            NativeLibrary.Free(library);
        }
    }

    internal static PointerTouchInfo[] ReadTouchpadFrame(uint pointerId, out int error)
    {
        error = 0;
        uint pointerCount = 0;
        _ = GetPointerFrameTouchpadInfo(pointerId, ref pointerCount, IntPtr.Zero);

        if (pointerCount == 0)
        {
            error = Marshal.GetLastWin32Error();
            return [];
        }

        int structureSize = Marshal.SizeOf<PointerTouchInfo>();
        IntPtr buffer = Marshal.AllocHGlobal(checked(structureSize * (int)pointerCount));
        try
        {
            uint requestedCount = pointerCount;
            if (!GetPointerFrameTouchpadInfo(pointerId, ref requestedCount, buffer))
            {
                error = Marshal.GetLastWin32Error();
                return [];
            }

            PointerTouchInfo[] result = new PointerTouchInfo[requestedCount];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = Marshal.PtrToStructure<PointerTouchInfo>(
                    IntPtr.Add(buffer, index * structureSize));
            }

            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}

[Flags]
internal enum InputMessageDeviceType : uint
{
    Unavailable = 0,
    Keyboard = 0x00000001,
    Mouse = 0x00000002,
    Touch = 0x00000004,
    Pen = 0x00000008,
    Touchpad = 0x00000010,
}

internal enum InputMessageOriginId : uint
{
    Unavailable = 0,
    Hardware = 1,
    Injected = 2,
    System = 4,
}

[StructLayout(LayoutKind.Sequential)]
internal struct InputMessageSource
{
    internal InputMessageDeviceType DeviceType;
    internal InputMessageOriginId OriginId;
}

internal enum PointerInputType : uint
{
    Pointer = 1,
    Touch = 2,
    Pen = 3,
    Mouse = 4,
    Touchpad = 5,
}

[Flags]
internal enum PointerFlags : uint
{
    None = 0x00000000,
    New = 0x00000001,
    InRange = 0x00000002,
    InContact = 0x00000004,
    FirstButton = 0x00000010,
    SecondButton = 0x00000020,
    ThirdButton = 0x00000040,
    FourthButton = 0x00000080,
    FifthButton = 0x00000100,
    Primary = 0x00002000,
    Confidence = 0x00004000,
    Canceled = 0x00008000,
    Down = 0x00010000,
    Update = 0x00020000,
    Up = 0x00040000,
    Wheel = 0x00080000,
    HorizontalWheel = 0x00100000,
    CaptureChanged = 0x00200000,
    HasTransform = 0x00400000,
}

internal enum PointerButtonChangeType : uint
{
    None,
    FirstButtonDown,
    FirstButtonUp,
    SecondButtonDown,
    SecondButtonUp,
    ThirdButtonDown,
    ThirdButtonUp,
    FourthButtonDown,
    FourthButtonUp,
    FifthButtonDown,
    FifthButtonUp,
}

[Flags]
internal enum TouchFlags : uint
{
    None = 0,
}

[Flags]
internal enum TouchMask : uint
{
    None = 0,
    ContactArea = 0x00000001,
    Orientation = 0x00000002,
    Pressure = 0x00000004,
}

[Flags]
internal enum PenFlags : uint
{
    None = 0,
    Barrel = 0x00000001,
    Inverted = 0x00000002,
    Eraser = 0x00000004,
}

[Flags]
internal enum PenMask : uint
{
    None = 0,
    Pressure = 0x00000001,
    Rotation = 0x00000002,
    TiltX = 0x00000004,
    TiltY = 0x00000008,
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    internal int X;
    internal int Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NativeRect
{
    internal int Left;
    internal int Top;
    internal int Right;
    internal int Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointerInfo
{
    internal PointerInputType PointerType;
    internal uint PointerId;
    internal uint FrameId;
    internal PointerFlags PointerFlags;
    internal IntPtr SourceDevice;
    internal IntPtr WindowTarget;
    internal NativePoint PixelLocation;
    internal NativePoint HimetricLocation;
    internal NativePoint PixelLocationRaw;
    internal NativePoint HimetricLocationRaw;
    internal uint Time;
    internal uint HistoryCount;
    internal int InputData;
    internal uint KeyStates;
    internal ulong PerformanceCount;
    internal PointerButtonChangeType ButtonChangeType;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointerTouchInfo
{
    internal PointerInfo PointerInfo;
    internal TouchFlags TouchFlags;
    internal TouchMask TouchMask;
    internal NativeRect Contact;
    internal NativeRect ContactRaw;
    internal uint Orientation;
    internal uint Pressure;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PointerPenInfo
{
    internal PointerInfo PointerInfo;
    internal PenFlags PenFlags;
    internal PenMask PenMask;
    internal uint Pressure;
    internal uint Rotation;
    internal int TiltX;
    internal int TiltY;
}
