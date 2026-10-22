using System;
using System.Numerics;
using System.Runtime.InteropServices;

namespace VoiDPlugins.Filter
{
    internal interface IPenDirectPan
    {
        bool Begin(IntPtr target, Vector2? preferredAnchor = null);
        bool Move(double x, double y);
        bool Hold();
        void Rebase();
        void End();
        string? Error { get; }
    }

    // Two contacts translate together at a fixed separation. No wheel events,
    // velocity curve, easing, timer-driven continuation, or queued movement.
    internal sealed class PenDirectPan : IPenDirectPan
    {
        internal static bool IsSupported => NativePenPanPlatform.Available;
        private readonly IPenPanPlatform _platform;
        private IntPtr _device, _target;
        private PenPanSurface _surface;
        private TouchFrame[] _frames = new TouchFrame[2];
        private int _x, _y, _baseX, _baseY;
        public string? Error { get; private set; }

        public PenDirectPan() : this(new NativePenPanPlatform()) { }
        internal PenDirectPan(IPenPanPlatform platform) => _platform = platform;

        public bool Begin(IntPtr target, Vector2? preferredAnchor = null)
        {
            End();
            Error = null;
            if (!_platform.Ready || target == IntPtr.Zero || !_platform.ContextValid(target))
                return Fail("unsupported-or-invalid-context");
            _target = target;
            if (!_platform.ResolveSurface(target, preferredAnchor, out _surface) || !_surface.Valid)
                return Fail("page-surface-unavailable");
            _device = _platform.Create();
            if (_device == IntPtr.Zero)
                return Fail("create-device");
            _x = _y = _baseX = _baseY = 0;
            _frames = BuildFrames(_surface.CenterX, _surface.CenterY, 0, 0, 0x10006);
            if (!_platform.Inject(_device, _frames))
            {
                Fail("touch-down");
                End();
                return false;
            }
            return true;
        }

        public bool Move(double x, double y)
        {
            if (_device == IntPtr.Zero || !_platform.ContextValid(_target))
                return Fail("context-changed");
            if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > 10000 || Math.Abs(y) > 10000)
                return Fail("invalid-displacement");
            // Reaching the page edge clamps this stroke; it never authorizes ink.
            var nextX = (int)Math.Clamp(Math.Round(x) + _baseX,
                _surface.Left + 28d - _surface.CenterX, _surface.Right - 29d - _surface.CenterX);
            var nextY = (int)Math.Clamp(Math.Round(y) + _baseY,
                _surface.Top + 4d - _surface.CenterY, _surface.Bottom - 5d - _surface.CenterY);
            if (nextX == _x && nextY == _y)
                return true;
            var next = BuildFrames(_surface.CenterX, _surface.CenterY, nextX, nextY, 0x20006);
            if (!_platform.Inject(_device, next))
                return Fail("touch-update");
            _frames = next;
            _x = nextX;
            _y = nextY;
            return true;
        }

        // Force a stationary frame with a fresh OS timestamp before releasing.
        public bool Hold()
        {
            if (_device == IntPtr.Zero || !_platform.ContextValid(_target))
                return Fail("context-changed");
            _frames = BuildFrames(_surface.CenterX, _surface.CenterY, _x, _y, 0x20006);
            return _platform.Inject(_device, _frames) || Fail("touch-hold");
        }

        public void Rebase() { _baseX = _x; _baseY = _y; }

        public void End()
        {
            if (_device == IntPtr.Zero)
                return;
            var device = _device;
            _device = IntPtr.Zero;
            try
            {
                // Cancel at the last accepted positions rather than fling with a normal UP.
                for (var i = 0; i < _frames.Length; i++)
                    _frames[i].Touch.Info.Flags = 0x40000 | 0x8000;
                if (!_platform.Inject(device, _frames))
                    Fail("touch-cancel");
            }
            finally { _platform.Destroy(device); }
        }

        private bool Fail(string reason)
        {
            Error = reason + (_platform.Error == null ? "" : ":" + _platform.Error);
            return false;
        }

        internal static TouchFrame[] BuildFrames(int anchorX, int anchorY, int dx, int dy, uint flags)
        {
            var result = new TouchFrame[2];
            for (var i = 0; i < 2; i++)
                result[i] = new TouchFrame
                {
                    Type = 2,
                    Touch = new Touch
                    {
                        Info = new PointerInfo
                        {
                            Type = 2, Id = (uint)i, Flags = flags,
                            Pixel = new Point { X = anchorX + (i == 0 ? -24 : 24) + dx, Y = anchorY + dy }
                        }
                    }
                };
            return result;
        }

        [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int L, T, R, B; }
        [StructLayout(LayoutKind.Sequential)] internal struct PointerInfo
        {
            public uint Type, Id, Frame, Flags;
            public IntPtr Device, Target;
            public Point Pixel, Himetric, PixelRaw, HimetricRaw;
            public uint Time, History;
            public int Input;
            public uint Keys;
            public ulong Performance;
            public uint ButtonChange;
        }
        [StructLayout(LayoutKind.Sequential)] internal struct Touch
        {
            public PointerInfo Info;
            public uint Flags, Mask;
            public Rect Contact, ContactRaw;
            public uint Orientation, Pressure;
        }
        // The native union's largest member is POINTER_TOUCH_INFO.
        [StructLayout(LayoutKind.Sequential)] internal struct TouchFrame { public uint Type; public Touch Touch; }
    }
}
