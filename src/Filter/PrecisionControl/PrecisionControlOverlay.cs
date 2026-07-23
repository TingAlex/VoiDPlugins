using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Plugin;

namespace VoiDPlugins.Filter
{
    internal interface IPrecisionControlOverlay : IDisposable
    {
        void Show(OverlayBounds bounds, float thickness, float opacity);
        void Hide();
    }

    internal static class PrecisionControlOverlayFactory
    {
        public static IPrecisionControlOverlay? Create(uint borderColor)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return null;

            try
            {
                return new WindowsPrecisionControlOverlay(borderColor);
            }
            catch (Exception exception)
            {
                Log.Write(
                    nameof(PrecisionControl),
                    $"Unable to initialize the precision border: {exception.Message}",
                    LogLevel.Warning);
                return null;
            }
        }
    }

    internal static class BorderColorParser
    {
        public const string DefaultColor = "#000000";

        public static bool TryParse(string? value, out uint colorReference)
        {
            var hex = value?.Trim();
            if (hex?.StartsWith("#", StringComparison.Ordinal) == true)
                hex = hex.Substring(1);

            if (hex?.Length == 6 &&
                uint.TryParse(
                    hex,
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out var rgb))
            {
                var red = (rgb >> 16) & 0xff;
                var green = (rgb >> 8) & 0xff;
                var blue = rgb & 0xff;
                colorReference = red | (green << 8) | (blue << 16);
                return true;
            }

            colorReference = 0;
            return false;
        }
    }

    internal readonly struct OverlayBounds
    {
        public OverlayBounds(int left, int top, int width, int height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public int Left { get; }
        public int Top { get; }
        public int Width { get; }
        public int Height { get; }
        public int Right => Left + Width;
        public int Bottom => Top + Height;

        public OverlayBounds Translate(int deltaX, int deltaY)
        {
            return new OverlayBounds(
                Left + deltaX,
                Top + deltaY,
                Width,
                Height);
        }
    }

    internal static class PrecisionBoundsCalculator
    {
        public static OverlayBounds CalculateScreenRelative(
            OverlayBounds outputArea,
            Vector2 anchor,
            float scale)
        {
            var safeScale = Math.Max(0, scale);
            var left = anchor.X + ((outputArea.Left - anchor.X) * safeScale);
            var top = anchor.Y + ((outputArea.Top - anchor.Y) * safeScale);
            var right = anchor.X + ((outputArea.Right - anchor.X) * safeScale);
            var bottom = anchor.Y + ((outputArea.Bottom - anchor.Y) * safeScale);

            var roundedLeft = (int)Math.Floor(Math.Min(left, right));
            var roundedTop = (int)Math.Floor(Math.Min(top, bottom));
            var roundedRight = (int)Math.Ceiling(Math.Max(left, right));
            var roundedBottom = (int)Math.Ceiling(Math.Max(top, bottom));

            return new OverlayBounds(
                roundedLeft,
                roundedTop,
                Math.Max(1, roundedRight - roundedLeft),
                Math.Max(1, roundedBottom - roundedTop));
        }

        public static OverlayBounds CalculatePointerRelative(
            OverlayBounds outputArea,
            Vector2 pointer,
            float scale,
            float horizontalPercent,
            float verticalPercent)
        {
            var safeScale = Math.Max(0, scale);
            var width = Math.Max(
                1,
                (int)Math.Ceiling(outputArea.Width * safeScale));
            var height = Math.Max(
                1,
                (int)Math.Ceiling(outputArea.Height * safeScale));
            var horizontalRatio = Math.Clamp(horizontalPercent, 0, 100) / 100f;
            var verticalRatio = Math.Clamp(verticalPercent, 0, 100) / 100f;
            var requestedLeft =
                (int)Math.Round(pointer.X - (width * horizontalRatio));
            var requestedTop =
                (int)Math.Round(pointer.Y - (height * verticalRatio));

            return new OverlayBounds(
                requestedLeft,
                requestedTop,
                width,
                height);
        }
    }

    internal static class PrecisionControlDesktop
    {
        public static bool TryGetMousePosition(out Vector2 position)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                OverlayNativeMethods.GetCursorPos(out var point))
            {
                position = new Vector2(point.X, point.Y);
                return true;
            }

            position = Vector2.Zero;
            return false;
        }

        public static OverlayBounds GetOutputArea(Vector2 anchor)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new OverlayBounds(0, 0, 1, 1);

            var point = new NativePoint
            {
                X = (int)Math.Round(anchor.X),
                Y = (int)Math.Round(anchor.Y)
            };
            var monitor = OverlayNativeMethods.MonitorFromPoint(
                point,
                OverlayNativeMethods.MONITOR_DEFAULTTONEAREST);
            var monitorInfo = new MonitorInfo
            {
                Size = (uint)Marshal.SizeOf<MonitorInfo>()
            };

            if (monitor != IntPtr.Zero &&
                OverlayNativeMethods.GetMonitorInfo(monitor, ref monitorInfo))
            {
                return new OverlayBounds(
                    monitorInfo.Monitor.Left,
                    monitorInfo.Monitor.Top,
                    monitorInfo.Monitor.Right - monitorInfo.Monitor.Left,
                    monitorInfo.Monitor.Bottom - monitorInfo.Monitor.Top);
            }

            return new OverlayBounds(
                OverlayNativeMethods.GetSystemMetrics(OverlayNativeMethods.SM_XVIRTUALSCREEN),
                OverlayNativeMethods.GetSystemMetrics(OverlayNativeMethods.SM_YVIRTUALSCREEN),
                OverlayNativeMethods.GetSystemMetrics(OverlayNativeMethods.SM_CXVIRTUALSCREEN),
                OverlayNativeMethods.GetSystemMetrics(OverlayNativeMethods.SM_CYVIRTUALSCREEN));
        }
    }

    internal sealed class WindowsPrecisionControlOverlay : IPrecisionControlOverlay
    {
        public WindowsPrecisionControlOverlay(uint borderColor)
        {
            _borderColor = borderColor;
            _windowProcedure = WindowProcedure;
            _thread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "Precision Control Border"
            };
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(3)) || !_initialized)
            {
                Dispose();
                throw new InvalidOperationException("The overlay window thread did not initialize.");
            }
        }

        public void Show(OverlayBounds bounds, float thickness, float opacity)
        {
            if (_disposed)
                return;

            lock (_stateLock)
            {
                _pendingBounds = bounds;
                _pendingThickness = Math.Clamp((int)Math.Round(thickness), 1, 12);
                _pendingOpacity = (byte)Math.Clamp(
                    (int)Math.Round(opacity * byte.MaxValue),
                    1,
                    byte.MaxValue);
            }

            PostMessage(ShowMessage);
        }

        public void Hide()
        {
            if (!_disposed)
                PostMessage(HideMessage);
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            var threadId = _threadId;
            if (threadId != 0)
                OverlayNativeMethods.PostThreadMessage(
                    threadId,
                    OverlayNativeMethods.WM_QUIT,
                    UIntPtr.Zero,
                    IntPtr.Zero);

            var threadStopped = !_thread.IsAlive;
            if (!threadStopped && _thread != Thread.CurrentThread)
                threadStopped = _thread.Join(TimeSpan.FromSeconds(2));

            if (threadStopped)
                _ready.Dispose();
        }

        private void MessageLoop()
        {
            _threadId = OverlayNativeMethods.GetCurrentThreadId();
            var previousDpiContext = OverlayNativeMethods.SetThreadDpiAwarenessContext(
                OverlayNativeMethods.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
            OverlayNativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            var instance = OverlayNativeMethods.GetModuleHandle(null);

            try
            {
                if (_disposed)
                    return;

                _className = $"PrecisionControlBorder-{Guid.NewGuid():N}";
                _brush = OverlayNativeMethods.CreateSolidBrush(_borderColor);
                if (_brush == IntPtr.Zero)
                {
                    _ready.Set();
                    return;
                }

                var windowClass = new WindowClass
                {
                    Size = (uint)Marshal.SizeOf<WindowClass>(),
                    WindowProcedure = _windowProcedure,
                    Instance = instance,
                    Background = _brush,
                    ClassName = _className
                };

                _classAtom = OverlayNativeMethods.RegisterClassEx(ref windowClass);
                if (_classAtom == 0)
                {
                    _ready.Set();
                    return;
                }

                for (var index = 0; index < _windows.Length; index++)
                {
                    _windows[index] = OverlayNativeMethods.CreateWindowEx(
                        ExtendedWindowStyle,
                        _className,
                        $"Precision Control Border {index + 1}",
                        OverlayNativeMethods.WS_POPUP,
                        0,
                        0,
                        1,
                        1,
                        IntPtr.Zero,
                        IntPtr.Zero,
                        windowClass.Instance,
                        IntPtr.Zero);

                    if (_windows[index] == IntPtr.Zero)
                    {
                        _ready.Set();
                        return;
                    }
                }

                _initialized = true;
                _ready.Set();

                while (OverlayNativeMethods.GetMessage(
                    out var message,
                    IntPtr.Zero,
                    0,
                    0) > 0)
                {
                    if (message.Message == ShowMessage)
                        ApplyPendingBounds();
                    else if (message.Message == HideMessage)
                        HideWindows();
                    else
                        OverlayNativeMethods.DispatchMessage(ref message);
                }
            }
            finally
            {
                DestroyWindows();
                if (_classAtom != 0)
                    OverlayNativeMethods.UnregisterClass(_className, instance);
                if (_brush != IntPtr.Zero)
                    OverlayNativeMethods.DeleteObject(_brush);
                _threadId = 0;
                if (!_initialized)
                    _ready.Set();
                if (previousDpiContext != IntPtr.Zero)
                    OverlayNativeMethods.SetThreadDpiAwarenessContext(previousDpiContext);
            }
        }

        private void ApplyPendingBounds()
        {
            OverlayBounds bounds;
            int thickness;
            byte opacity;
            lock (_stateLock)
            {
                bounds = _pendingBounds;
                thickness = _pendingThickness;
                opacity = _pendingOpacity;
            }

            SetWindowBounds(_windows[0], bounds.Left, bounds.Top, bounds.Width, thickness, opacity);
            SetWindowBounds(
                _windows[1],
                bounds.Left,
                bounds.Bottom - thickness,
                bounds.Width,
                thickness,
                opacity);
            SetWindowBounds(_windows[2], bounds.Left, bounds.Top, thickness, bounds.Height, opacity);
            SetWindowBounds(
                _windows[3],
                bounds.Right - thickness,
                bounds.Top,
                thickness,
                bounds.Height,
                opacity);
        }

        private static void SetWindowBounds(
            IntPtr window,
            int x,
            int y,
            int width,
            int height,
            byte opacity)
        {
            OverlayNativeMethods.SetLayeredWindowAttributes(
                window,
                0,
                opacity,
                OverlayNativeMethods.LWA_ALPHA);
            OverlayNativeMethods.SetWindowPos(
                window,
                OverlayNativeMethods.HWND_TOPMOST,
                x,
                y,
                Math.Max(1, width),
                Math.Max(1, height),
                OverlayNativeMethods.SWP_NOACTIVATE |
                OverlayNativeMethods.SWP_SHOWWINDOW);
        }

        private void HideWindows()
        {
            foreach (var window in _windows)
            {
                if (window != IntPtr.Zero)
                    OverlayNativeMethods.ShowWindow(window, OverlayNativeMethods.SW_HIDE);
            }
        }

        private void DestroyWindows()
        {
            for (var index = 0; index < _windows.Length; index++)
            {
                var window = _windows[index];
                if (window != IntPtr.Zero)
                    OverlayNativeMethods.DestroyWindow(window);
                _windows[index] = IntPtr.Zero;
            }
        }

        private void PostMessage(uint message)
        {
            var threadId = _threadId;
            if (threadId != 0)
                OverlayNativeMethods.PostThreadMessage(
                    threadId,
                    message,
                    UIntPtr.Zero,
                    IntPtr.Zero);
        }

        private static IntPtr WindowProcedure(
            IntPtr window,
            uint message,
            UIntPtr wParam,
            IntPtr lParam)
        {
            if (message == OverlayNativeMethods.WM_NCHITTEST)
                return (IntPtr)OverlayNativeMethods.HTTRANSPARENT;
            if (message == OverlayNativeMethods.WM_MOUSEACTIVATE)
                return (IntPtr)OverlayNativeMethods.MA_NOACTIVATE;

            return OverlayNativeMethods.DefWindowProc(window, message, wParam, lParam);
        }

        private const uint ShowMessage = OverlayNativeMethods.WM_APP + 1;
        private const uint HideMessage = OverlayNativeMethods.WM_APP + 2;
        private const uint ExtendedWindowStyle =
            OverlayNativeMethods.WS_EX_LAYERED |
            OverlayNativeMethods.WS_EX_TRANSPARENT |
            OverlayNativeMethods.WS_EX_TOOLWINDOW |
            OverlayNativeMethods.WS_EX_NOACTIVATE |
            OverlayNativeMethods.WS_EX_TOPMOST;

        private readonly object _stateLock = new();
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly uint _borderColor;
        private readonly OverlayNativeMethods.WindowProcedure _windowProcedure;
        private readonly Thread _thread;
        private readonly IntPtr[] _windows = new IntPtr[4];
        private string _className = string.Empty;
        private OverlayBounds _pendingBounds;
        private int _pendingThickness;
        private byte _pendingOpacity;
        private uint _threadId;
        private ushort _classAtom;
        private IntPtr _brush;
        private bool _initialized;
        private volatile bool _disposed;
    }

    internal static class OverlayNativeMethods
    {
        public delegate IntPtr WindowProcedure(
            IntPtr window,
            uint message,
            UIntPtr wParam,
            IntPtr lParam);

        public const uint WM_QUIT = 0x0012;
        public const uint WM_NCHITTEST = 0x0084;
        public const uint WM_MOUSEACTIVATE = 0x0021;
        public const uint WM_APP = 0x8000;
        public const int HTTRANSPARENT = -1;
        public const int MA_NOACTIVATE = 3;
        public const uint WS_POPUP = 0x80000000;
        public const uint WS_EX_TOPMOST = 0x00000008;
        public const uint WS_EX_TRANSPARENT = 0x00000020;
        public const uint WS_EX_TOOLWINDOW = 0x00000080;
        public const uint WS_EX_NOACTIVATE = 0x08000000;
        public const uint WS_EX_LAYERED = 0x00080000;
        public const uint LWA_ALPHA = 0x00000002;
        public const uint SWP_NOACTIVATE = 0x0010;
        public const uint SWP_SHOWWINDOW = 0x0040;
        public const int SW_HIDE = 0;
        public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        public const int SM_XVIRTUALSCREEN = 76;
        public const int SM_YVIRTUALSCREEN = 77;
        public const int SM_CXVIRTUALSCREEN = 78;
        public const int SM_CYVIRTUALSCREEN = 79;
        public static readonly IntPtr HWND_TOPMOST = new(-1);
        public static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

        [DllImport(
            "user32.dll",
            EntryPoint = "RegisterClassExW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            SetLastError = true)]
        public static extern ushort RegisterClassEx(ref WindowClass windowClass);

        [DllImport(
            "user32.dll",
            EntryPoint = "UnregisterClassW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport(
            "user32.dll",
            EntryPoint = "CreateWindowExW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true,
            SetLastError = true)]
        public static extern IntPtr CreateWindowEx(
            uint extendedStyle,
            string className,
            string windowName,
            uint style,
            int x,
            int y,
            int width,
            int height,
            IntPtr parent,
            IntPtr menu,
            IntPtr instance,
            IntPtr parameter);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", EntryPoint = "DefWindowProcW", ExactSpelling = true)]
        public static extern IntPtr DefWindowProc(
            IntPtr window,
            uint message,
            UIntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern int GetMessage(
            out NativeMessage message,
            IntPtr window,
            uint minimumMessage,
            uint maximumMessage);

        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PeekMessage(
            out NativeMessage message,
            IntPtr window,
            uint minimumMessage,
            uint maximumMessage,
            uint removeMessage);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostThreadMessage(
            uint threadId,
            uint message,
            UIntPtr wParam,
            IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetLayeredWindowAttributes(
            IntPtr window,
            uint colorKey,
            byte alpha,
            uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetWindowPos(
            IntPtr window,
            IntPtr insertAfter,
            int x,
            int y,
            int width,
            int height,
            uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr window, int command);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out NativePoint point);

        [DllImport(
            "user32.dll",
            EntryPoint = "GetMonitorInfoW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

        [DllImport(
            "kernel32.dll",
            EntryPoint = "GetModuleHandleW",
            CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        public static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateSolidBrush(uint color);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr value);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WindowClass
    {
        public uint Size;
        public uint Style;
        public OverlayNativeMethods.WindowProcedure WindowProcedure;
        public int ClassExtra;
        public int WindowExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public uint Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
