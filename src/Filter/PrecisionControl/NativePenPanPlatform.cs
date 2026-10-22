using System;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;

namespace VoiDPlugins.Filter
{
    internal readonly struct PenPanSurface
    {
        internal PenPanSurface(int left, int top, int right, int bottom)
        { Left = left; Top = top; Right = right; Bottom = bottom; }
        internal int Left { get; }
        internal int Top { get; }
        internal int Right { get; }
        internal int Bottom { get; }
        internal int CenterX => Left + (Right - Left) / 2;
        internal int CenterY => Top + (Bottom - Top) / 2;
        internal bool Valid => Right - (long)Left >= 160 && Bottom - (long)Top >= 120;
    }

    internal interface IPenPanPlatform
    {
        bool Ready { get; }
        string? Error { get; }
        bool ContextValid(IntPtr target);
        bool ResolveSurface(IntPtr target, Vector2? preferredAnchor, out PenPanSurface surface);
        IntPtr Create();
        bool Inject(IntPtr device, PenDirectPan.TouchFrame[] frames);
        void Destroy(IntPtr device);
    }

    internal sealed class NativePenPanPlatform : IPenPanPlatform
    {
        internal static readonly bool Available = Probe();
        public bool Ready => Available;
        public string? Error { get; private set; }
        public bool ContextValid(IntPtr target)
        {
            if (GetForegroundWindow() != target) return false;
            foreach (var key in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C })
                if ((GetAsyncKeyState(key) & 0x8000) != 0) return false;
            return true;
        }

        public bool ResolveSurface(IntPtr target, Vector2? preferredAnchor, out PenPanSurface surface)
        {
            surface = default;
            Error = null;
            var oldDpi = SetThreadDpiAwarenessContext(new IntPtr(-4));
            try
            {
                PenDirectPan.Point point;
                if (preferredAnchor.HasValue)
                {
                    var p = preferredAnchor.Value;
                    if (!float.IsFinite(p.X) || !float.IsFinite(p.Y) ||
                        p.X < int.MinValue || p.X >= int.MaxValue || p.Y < int.MinValue || p.Y >= int.MaxValue)
                        return false;
                    point = new PenDirectPan.Point { X = (int)Math.Round(p.X), Y = (int)Math.Round(p.Y) };
                }
                else if (!GetPhysicalCursorPos(out point)) return false;

                if (!TryTargetHit(target, point, out var hit))
                { Error = "target-is-obscured"; return false; }

                // Locate the page child using the mapped pen point. Mouse state
                // may lag the first pen-down report, especially with precision mapping.
                for (var i = 0; i < 32; i++)
                {
                    var local = point;
                    if (!ScreenToClient(hit, ref local)) break;
                    var child = ChildWindowFromPointEx(hit, local, 7);
                    if (child == IntPtr.Zero || child == hit) break;
                    hit = child;
                }
                for (var i = 0; i < 32 && GetAncestor(hit, 2) == target; i++)
                {
                    if (GetClientRect(hit, out var r))
                    {
                        var start = new PenDirectPan.Point { X = r.L, Y = r.T };
                        var end = new PenDirectPan.Point { X = r.R, Y = r.B };
                        if (ClientToScreen(hit, ref start) && ClientToScreen(hit, ref end))
                        {
                            surface = new PenPanSurface(start.X, start.Y, end.X, end.Y);
                            if (surface.Valid)
                            {
                                var left = new PenDirectPan.Point { X = surface.CenterX - 24, Y = surface.CenterY };
                                var right = new PenDirectPan.Point { X = surface.CenterX + 24, Y = surface.CenterY };
                                if (TryTargetHit(target, left, out _) && TryTargetHit(target, right, out _)) return true;
                                Error = "page-center-is-obscured";
                                return false;
                            }
                        }
                    }
                    if (hit == target) break;
                    hit = GetParent(hit);
                    if (hit == IntPtr.Zero) break;
                }
                Error = "page-client-too-small";
                return false;
            }
            finally { if (oldDpi != IntPtr.Zero) SetThreadDpiAwarenessContext(oldDpi); }
        }

        private static bool IsOwnBorder(IntPtr window)
        {
            GetWindowThreadProcessId(window, out var pid);
            if (pid != (uint)Environment.ProcessId) return false;
            var name = new StringBuilder(128);
            return GetClassName(window, name, name.Capacity) > 0 &&
                name.ToString().StartsWith("PrecisionControlBorder-", StringComparison.Ordinal);
        }

        private static bool TryTargetHit(IntPtr target, PenDirectPan.Point point, out IntPtr hit)
        {
            hit = WindowFromPhysicalPoint(point);
            var root = GetAncestor(hit, 2);
            if (root == target) return true;
            if (!IsOwnBorder(root)) return false;
            // Skip only a verified border belonging to this daemon. A foreign
            // overlay is never ignored when selecting initial touch destinations.
            for (var i = 0; i < 256; i++)
            {
                root = GetWindow(root, 2);
                if (root == IntPtr.Zero) return false;
                if (!IsWindowVisible(root) || !IsWindowEnabled(root) || !GetWindowRect(root, out var r) ||
                    point.X < r.L || point.X >= r.R || point.Y < r.T || point.Y >= r.B || IsOwnBorder(root)) continue;
                hit = target;
                return root == target;
            }
            return false;
        }
        public IntPtr Create()
        { var result = CreateSyntheticPointerDevice(2, 2, 3); Error = result == IntPtr.Zero ? Marshal.GetLastWin32Error().ToString() : null; return result; }
        public bool Inject(IntPtr device, PenDirectPan.TouchFrame[] frames)
        { var ok = InjectSyntheticPointerInput(device, frames, (uint)frames.Length); Error = ok ? null : Marshal.GetLastWin32Error().ToString(); return ok; }
        public void Destroy(IntPtr device) => DestroySyntheticPointerDevice(device);

        private static bool Probe()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || !NativeLibrary.TryLoad("user32.dll", out var lib)) return false;
            try
            {
                foreach (var name in new[] { "CreateSyntheticPointerDevice", "InjectSyntheticPointerInput", "DestroySyntheticPointerDevice",
                    "GetForegroundWindow", "GetAsyncKeyState", "GetPhysicalCursorPos", "WindowFromPhysicalPoint", "GetAncestor",
                    "GetWindow", "GetWindowRect", "GetWindowThreadProcessId", "GetClassNameW", "IsWindowVisible", "IsWindowEnabled",
                    "GetClientRect", "ClientToScreen", "ScreenToClient", "ChildWindowFromPointEx", "GetParent", "SetThreadDpiAwarenessContext" })
                    if (!NativeLibrary.TryGetExport(lib, name, out _)) return false;
                return true;
            }
            finally { NativeLibrary.Free(lib); }
        }
        [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr CreateSyntheticPointerDevice(uint type, uint count, uint mode);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool InjectSyntheticPointerInput(IntPtr device, [In] PenDirectPan.TouchFrame[] frames, uint count);
        [DllImport("user32.dll")] private static extern void DestroySyntheticPointerDevice(IntPtr device);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPhysicalCursorPos(out PenDirectPan.Point point);
        [DllImport("user32.dll")] private static extern IntPtr WindowFromPhysicalPoint(PenDirectPan.Point point);
        [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr window, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr window);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowEnabled(IntPtr window);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr window, out PenDirectPan.Rect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(IntPtr window, out PenDirectPan.Rect rect);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClientToScreen(IntPtr window, ref PenDirectPan.Point point);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ScreenToClient(IntPtr window, ref PenDirectPan.Point point);
        [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr window, PenDirectPan.Point point, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
    }
}
