using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Plugin;

namespace VoiDPlugins.Filter
{
    internal static class GlobalHotkeyKeyMap
    {
        public static string[] Keys { get; } =
            Enumerable.Range('A', 26).Select(value => ((char)value).ToString())
                .Concat(Enumerable.Range('0', 10).Select(value => ((char)value).ToString()))
                .Concat(Enumerable.Range(1, 24).Select(value => $"F{value}"))
                .Concat(new[]
                {
                    "Up",
                    "Down",
                    "Left",
                    "Right",
                    "Pause",
                    "ScrollLock"
                })
                .ToArray();

        public static bool TryGetVirtualKey(string key, out uint virtualKey)
        {
            if (key.Length == 1)
            {
                var character = char.ToUpperInvariant(key[0]);
                if (character is >= 'A' and <= 'Z' or >= '0' and <= '9')
                {
                    virtualKey = character;
                    return true;
                }
            }

            if (key.StartsWith("F", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(key.Substring(1), out var functionKey) &&
                functionKey is >= 1 and <= 24)
            {
                virtualKey = (uint)(0x70 + functionKey - 1);
                return true;
            }

            if (key.Equals("Pause", StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = 0x13;
                return true;
            }

            if (key.Equals("ScrollLock", StringComparison.OrdinalIgnoreCase))
            {
                virtualKey = 0x91;
                return true;
            }

            var navigationKeys = new Dictionary<string, uint>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["Left"] = 0x25,
                ["Up"] = 0x26,
                ["Right"] = 0x27,
                ["Down"] = 0x28
            };
            if (navigationKeys.TryGetValue(key, out virtualKey))
                return true;

            virtualKey = 0;
            return false;
        }
    }

    internal static class PrecisionControlGlobalHotkeyManager
    {
        public static void Register(PrecisionControl filter)
        {
            lock (_syncRoot)
            {
                if (!_filters.Contains(filter))
                    _filters.Add(filter);

                PointerActivityTracker.SetEnabled(_filters.Count > 0);
                RebuildListeners();
            }
        }

        public static void Unregister(PrecisionControl filter)
        {
            lock (_syncRoot)
            {
                _filters.Remove(filter);
                PointerActivityTracker.SetEnabled(_filters.Count > 0);
                RebuildListeners();
            }
        }

        public static bool TryMatch(
            PrecisionControl filter,
            HotkeyGesture gesture,
            out PrecisionControlAction action)
        {
            foreach (var binding in CreateConfigurations(filter, false))
            {
                if (binding.Gesture.Equals(gesture))
                {
                    action = binding.Action;
                    return true;
                }
            }

            action = default;
            return false;
        }

        private static void RebuildListeners()
        {
            foreach (var listener in _listeners.Values)
                listener.Dispose();
            _listeners.Clear();

            foreach (var filter in _filters)
            {
                foreach (var binding in CreateConfigurations(filter, true))
                {
                    var gesture = binding.Gesture;
                    if (_listeners.ContainsKey(gesture))
                        continue;

                    var listener = new GlobalHotkeyListener(
                        gesture.Modifiers,
                        gesture.VirtualKey,
                        () => OnHotkeyPressed(gesture));
                    if (!listener.Start(out var error))
                    {
                        Log.Write(nameof(PrecisionControl),
                            $"Unable to register global hotkey {gesture.DisplayText}. Win32 error: {error}.",
                            LogLevel.Error);
                        listener.Dispose();
                        continue;
                    }

                    _listeners.Add(gesture, listener);
                    Log.Write(nameof(PrecisionControl),
                        $"Registered {FormatAction(binding.Action)} hotkey {gesture.DisplayText}.");
                }
            }
        }

        private static IEnumerable<HotkeyBinding> CreateConfigurations(
            PrecisionControl filter,
            bool logErrors)
        {
            var configurations = new[]
            {
                new
                {
                    Enabled = filter.EnableGlobalHotkey,
                    Key = filter.GlobalHotkeyKey,
                    Action = PrecisionControlAction.Toggle
                },
                new
                {
                    Enabled = filter.EnableRepositionHotkey,
                    Key = filter.RepositionHotkeyKey,
                    Action = PrecisionControlAction.Reposition
                },
                new
                {
                    Enabled = filter.EnableNudgeHotkeys,
                    Key = filter.NudgeUpKey,
                    Action = PrecisionControlAction.NudgeUp
                },
                new
                {
                    Enabled = filter.EnableNudgeHotkeys,
                    Key = filter.NudgeDownKey,
                    Action = PrecisionControlAction.NudgeDown
                },
                new
                {
                    Enabled = filter.EnableNudgeHotkeys,
                    Key = filter.NudgeLeftKey,
                    Action = PrecisionControlAction.NudgeLeft
                },
                new
                {
                    Enabled = filter.EnableNudgeHotkeys,
                    Key = filter.NudgeRightKey,
                    Action = PrecisionControlAction.NudgeRight
                }
            };

            var seen = new HashSet<HotkeyGesture>();
            foreach (var configuration in configurations)
            {
                if (!configuration.Enabled)
                    continue;

                if (!TryCreateGesture(
                    filter,
                    configuration.Key,
                    out var gesture,
                    out var errorMessage))
                {
                    if (logErrors)
                    {
                        Log.Write(
                            nameof(PrecisionControl),
                            errorMessage,
                            LogLevel.Error);
                    }
                    continue;
                }

                if (!seen.Add(gesture))
                {
                    if (logErrors)
                    {
                        Log.Write(
                            nameof(PrecisionControl),
                            $"Duplicate global hotkey {gesture.DisplayText}; keeping the first configured action.",
                            LogLevel.Warning);
                    }
                    continue;
                }

                yield return new HotkeyBinding(gesture, configuration.Action);
            }
        }

        private static bool TryCreateGesture(
            PrecisionControl filter,
            string? key,
            out HotkeyGesture gesture,
            out string errorMessage)
        {
            gesture = default;
            if (string.IsNullOrWhiteSpace(key) ||
                !GlobalHotkeyKeyMap.TryGetVirtualKey(
                    key,
                    out var virtualKey))
            {
                errorMessage =
                    $"Unsupported global hotkey key '{key}'.";
                return false;
            }

            var modifiers = HotkeyModifiers.NoRepeat;
            if (filter.HotkeyCtrl)
                modifiers |= HotkeyModifiers.Control;
            if (filter.HotkeyAlt)
                modifiers |= HotkeyModifiers.Alt;
            if (filter.HotkeyShift)
                modifiers |= HotkeyModifiers.Shift;
            if (filter.HotkeyWindows)
                modifiers |= HotkeyModifiers.Windows;

            if ((modifiers & ~HotkeyModifiers.NoRepeat) == 0)
            {
                errorMessage =
                    "At least one modifier is required for the global hotkey.";
                return false;
            }

            gesture = new HotkeyGesture(
                modifiers,
                virtualKey,
                FormatHotkey(filter, key));
            errorMessage = string.Empty;
            return true;
        }

        private static void OnHotkeyPressed(HotkeyGesture gesture)
        {
            if (!PrecisionControlCoordinator.TryQueueGlobalAction(
                PrecisionControlAction.Toggle,
                gesture))
            {
                Log.Debug(nameof(PrecisionControl),
                    "Ignored global hotkey because its action is unavailable or the pen is writing.");
            }
        }

        private static string FormatHotkey(
            PrecisionControl filter,
            string key)
        {
            var parts = new List<string>();
            if (filter.HotkeyCtrl)
                parts.Add("Ctrl");
            if (filter.HotkeyAlt)
                parts.Add("Alt");
            if (filter.HotkeyShift)
                parts.Add("Shift");
            if (filter.HotkeyWindows)
                parts.Add("Win");
            parts.Add(key);
            return string.Join("+", parts);
        }

        private static string FormatAction(PrecisionControlAction action)
        {
            return action switch
            {
                PrecisionControlAction.Toggle => "toggle",
                PrecisionControlAction.Reposition => "reposition",
                PrecisionControlAction.NudgeUp => "nudge-up",
                PrecisionControlAction.NudgeDown => "nudge-down",
                PrecisionControlAction.NudgeLeft => "nudge-left",
                PrecisionControlAction.NudgeRight => "nudge-right",
                _ => action.ToString()
            };
        }

        private static readonly object _syncRoot = new();
        private static readonly List<PrecisionControl> _filters = new();
        private static readonly Dictionary<HotkeyGesture, GlobalHotkeyListener>
            _listeners = new();
    }

    internal readonly struct HotkeyBinding
    {
        public HotkeyBinding(
            HotkeyGesture gesture,
            PrecisionControlAction action)
        {
            Gesture = gesture;
            Action = action;
        }

        public HotkeyGesture Gesture { get; }
        public PrecisionControlAction Action { get; }
    }

    internal readonly struct PointerActivitySample
    {
        public PointerActivitySample(
            Vector2 position,
            long timestamp,
            bool available = true)
        {
            Position = position;
            Timestamp = timestamp;
            Available = available;
        }

        public Vector2 Position { get; }
        public long Timestamp { get; }
        public bool Available { get; }
    }

    internal static class PointerActivityTracker
    {
        public static long LastMouseMovementTimestamp
        {
            get
            {
                lock (_syncRoot)
                    return _mouseTimestamp;
            }
        }

        public static void SetEnabled(bool enabled)
        {
            lock (_syncRoot)
            {
                if (enabled)
                {
                    if (_timer != null)
                        return;

                    SampleMouseLocked();
                    _timer = new Timer(
                        _ => SampleMouse(),
                        null,
                        TimeSpan.FromMilliseconds(25),
                        TimeSpan.FromMilliseconds(25));
                }
                else
                {
                    _timer?.Dispose();
                    _timer = null;
                }
            }
        }

        public static PointerActivitySample GetMouseActivity()
        {
            lock (_syncRoot)
            {
                SampleMouseLocked();
                return new PointerActivitySample(
                    _mousePosition,
                    _mouseTimestamp,
                    _hasMousePosition);
            }
        }

        public static void RecordPenPosition(Vector2 position)
        {
            lock (_syncRoot)
            {
                _lastPenPosition = position;
                _lastPenTimestamp = Stopwatch.GetTimestamp();
                _hasPenPosition = true;
            }
        }

        private static void SampleMouse()
        {
            lock (_syncRoot)
                SampleMouseLocked();
        }

        private static void SampleMouseLocked()
        {
            if (!PrecisionControlDesktop.TryGetMousePosition(out var position))
                return;

            var now = Stopwatch.GetTimestamp();
            if (!_hasMousePosition ||
                Vector2.DistanceSquared(_mousePosition, position) > 0.01f)
            {
                _mousePosition = position;
                _hasMousePosition = true;

                var mirrorsRecentPen =
                    _hasPenPosition &&
                    Vector2.DistanceSquared(_lastPenPosition, position) <= 1f &&
                    now - _lastPenTimestamp <= Stopwatch.Frequency / 10;
                if (!mirrorsRecentPen)
                    _mouseTimestamp = now;
            }
            else if (_mouseTimestamp == 0)
            {
                _mouseTimestamp = now;
            }
        }

        private static readonly object _syncRoot = new();
        private static Timer? _timer;
        private static Vector2 _mousePosition;
        private static Vector2 _lastPenPosition;
        private static long _mouseTimestamp;
        private static long _lastPenTimestamp;
        private static bool _hasMousePosition;
        private static bool _hasPenPosition;
    }

    internal readonly struct HotkeyGesture : IEquatable<HotkeyGesture>
    {
        public HotkeyGesture(
            HotkeyModifiers modifiers,
            uint virtualKey,
            string displayText)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            DisplayText = displayText;
        }

        public HotkeyModifiers Modifiers { get; }
        public uint VirtualKey { get; }
        public string DisplayText { get; }

        public bool Equals(HotkeyGesture other)
        {
            return Modifiers == other.Modifiers &&
                VirtualKey == other.VirtualKey;
        }

        public override bool Equals(object? obj)
        {
            return obj is HotkeyGesture other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine((uint)Modifiers, VirtualKey);
        }
    }

    [Flags]
    internal enum HotkeyModifiers : uint
    {
        Alt = 0x0001,
        Control = 0x0002,
        Shift = 0x0004,
        Windows = 0x0008,
        NoRepeat = 0x4000
    }

    internal sealed class GlobalHotkeyListener : IDisposable
    {
        public GlobalHotkeyListener(HotkeyModifiers modifiers, uint virtualKey, Action callback)
        {
            _modifiers = modifiers;
            _virtualKey = virtualKey;
            _callback = callback;
        }

        public bool Start(out int error)
        {
            _thread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "Precision Control Global Hotkey"
            };
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(3)))
            {
                error = 1460;
                return false;
            }

            error = _registrationError;
            return _registered;
        }

        public void Dispose()
        {
            _disposeRequested = true;
            var threadId = _threadId;
            if (threadId != 0)
                NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);

            var thread = _thread;
            var threadStopped = thread == null || !thread.IsAlive;
            if (!threadStopped && thread != Thread.CurrentThread)
                threadStopped = thread!.Join(TimeSpan.FromSeconds(2));

            if (threadStopped)
            {
                _thread = null;
                _ready.Dispose();
            }
        }

        private void MessageLoop()
        {
            _threadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            if (_disposeRequested)
            {
                _ready.Set();
                _threadId = 0;
                return;
            }

            _registered = NativeMethods.RegisterHotKey(
                IntPtr.Zero,
                HotkeyId,
                (uint)_modifiers,
                _virtualKey);
            _registrationError = _registered ? 0 : Marshal.GetLastWin32Error();
            _ready.Set();

            if (!_registered)
                return;

            try
            {
                while (NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    if (message.Message == NativeMethods.WM_HOTKEY &&
                        message.WParam == (UIntPtr)HotkeyId)
                    {
                        _callback();
                    }
                }
            }
            finally
            {
                NativeMethods.UnregisterHotKey(IntPtr.Zero, HotkeyId);
                _registered = false;
                _threadId = 0;
            }
        }

        private const int HotkeyId = 0x5043;
        private readonly HotkeyModifiers _modifiers;
        private readonly uint _virtualKey;
        private readonly Action _callback;
        private readonly ManualResetEventSlim _ready = new(false);
        private Thread? _thread;
        private uint _threadId;
        private bool _registered;
        private int _registrationError;
        private volatile bool _disposeRequested;
    }

    internal static class NativeMethods
    {
        public const uint WM_HOTKEY = 0x0312;
        public const uint WM_QUIT = 0x0012;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RegisterHotKey(
            IntPtr window,
            int id,
            uint modifiers,
            uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnregisterHotKey(IntPtr window, int id);

        [DllImport("user32.dll")]
        public static extern int GetMessage(
            out NativeMessage message,
            IntPtr window,
            uint minimumMessage,
            uint maximumMessage);

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

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeMessage
    {
        public IntPtr Window;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        public int X;
        public int Y;
    }
}
