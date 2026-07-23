using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;

namespace VoiDPlugins.Filter
{
    [PluginName("Precision Control Global Hotkey")]
    [SupportedPlatform(PluginPlatform.Windows)]
    public sealed class PrecisionControlGlobalHotkey : ITool
    {
        public static string[] ValidKeys => GlobalHotkeyKeyMap.Keys.ToArray();

        [Property("Key"), PropertyValidated(nameof(ValidKeys)), DefaultPropertyValue("P")]
        public string? Key { get; set; }

        [BooleanProperty("Ctrl", "Require the Ctrl key."), DefaultPropertyValue(true)]
        public bool Ctrl { get; set; }

        [BooleanProperty("Alt", "Require the Alt key."), DefaultPropertyValue(true)]
        public bool Alt { get; set; }

        [BooleanProperty("Shift", "Require the Shift key."), DefaultPropertyValue(true)]
        public bool Shift { get; set; }

        [BooleanProperty("Windows", "Require the Windows key."), DefaultPropertyValue(false)]
        public bool Windows { get; set; }

        public bool Initialize()
        {
            if (string.IsNullOrWhiteSpace(Key) ||
                !GlobalHotkeyKeyMap.TryGetVirtualKey(Key, out var virtualKey))
            {
                Log.Write(nameof(PrecisionControlGlobalHotkey),
                    $"Unsupported global hotkey key '{Key}'.", LogLevel.Error);
                return false;
            }

            var modifiers = HotkeyModifiers.NoRepeat;
            if (Ctrl)
                modifiers |= HotkeyModifiers.Control;
            if (Alt)
                modifiers |= HotkeyModifiers.Alt;
            if (Shift)
                modifiers |= HotkeyModifiers.Shift;
            if (Windows)
                modifiers |= HotkeyModifiers.Windows;

            if ((modifiers & ~HotkeyModifiers.NoRepeat) == 0)
            {
                Log.Write(nameof(PrecisionControlGlobalHotkey),
                    "At least one modifier is required for the global hotkey.", LogLevel.Error);
                return false;
            }

            _listener = new GlobalHotkeyListener(modifiers, virtualKey, OnHotkeyPressed);
            if (!_listener.Start(out var error))
            {
                Log.Write(nameof(PrecisionControlGlobalHotkey),
                    $"Unable to register global hotkey {FormatHotkey()}. Win32 error: {error}.",
                    LogLevel.Error);
                _listener.Dispose();
                _listener = null;
                return false;
            }

            Log.Write(nameof(PrecisionControlGlobalHotkey),
                $"Registered global hotkey {FormatHotkey()}. The hotkey toggles precision only while a pen is in range.");
            return true;
        }

        public void Dispose()
        {
            _listener?.Dispose();
            _listener = null;
        }

        private void OnHotkeyPressed()
        {
            if (!PrecisionControlCoordinator.TryQueueGlobalToggle())
            {
                Log.Debug(nameof(PrecisionControlGlobalHotkey),
                    "Ignored global hotkey because no Precision Control pen is in range.");
            }
        }

        private string FormatHotkey()
        {
            var parts = new List<string>();
            if (Ctrl)
                parts.Add("Ctrl");
            if (Alt)
                parts.Add("Alt");
            if (Shift)
                parts.Add("Shift");
            if (Windows)
                parts.Add("Win");
            parts.Add(Key ?? string.Empty);
            return string.Join("+", parts);
        }

        private GlobalHotkeyListener? _listener;
    }

    internal static class GlobalHotkeyKeyMap
    {
        public static string[] Keys { get; } =
            Enumerable.Range('A', 26).Select(value => ((char)value).ToString())
                .Concat(Enumerable.Range('0', 10).Select(value => ((char)value).ToString()))
                .Concat(Enumerable.Range(1, 24).Select(value => $"F{value}"))
                .Concat(new[] { "Pause", "ScrollLock" })
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

            virtualKey = 0;
            return false;
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
