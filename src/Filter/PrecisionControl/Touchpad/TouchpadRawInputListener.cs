using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Plugin;

namespace VoiDPlugins.Filter
{
    // Background precision-touchpad capture.
    // Adapted from emoacht/RawInput.Touchpad (MIT, Copyright (c) 2021 emoacht) and
    // ClementGre/ThreeFingerDragOnWindows (MIT, Copyright (c) 2022 Clément Grennerat):
    // the raw-input registration flags (RIDEV_INPUTSINK | RIDEV_DEVNOTIFY), the HID
    // report reading and the partial-contact-frame merging follow those sources.
    internal sealed class TouchpadRawInputListener : IDisposable
    {
        private const int WmInput = 0x00FF;
        private const int WmInputDeviceChange = 0x00FE;
        private const uint WmQuit = 0x0012;

        private const uint RidInput = 0x10000003;
        private const uint RidiPreparsedData = 0x20000005;

        private const uint RidevInputSink = 0x00000100;
        private const uint RidevDevNotify = 0x00002000;

        private const int RimTypeHid = 2;
        private const uint HidpStatusSuccess = 0x00110000;

        private const int DefaultCoordinateRange = 4096;

        public TouchpadRawInputListener()
        {
            _windowProcedure = WindowProcedure;

            _thread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "Precision Control Touchpad Input"
            };
            _thread.Start();

            if (!_ready.Wait(TimeSpan.FromSeconds(3)))
                _registrationError = -1;

            if (!IsRegistered)
                StopThread();
        }

        public event Action<TouchpadFrame>? FrameReceived;

        public bool IsRegistered { get; private set; }

        public int RegistrationError => _registrationError;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            StopThread();
        }

        private void StopThread()
        {
            if (_threadId != 0)
                PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);

            if (_thread.IsAlive && _thread != Thread.CurrentThread)
                _thread.Join(TimeSpan.FromSeconds(2));
        }

        private void MessageLoop()
        {
            _threadId = GetCurrentThreadId();
            try
            {
                _className = "VoiDPrecisionControlTouchpad_" + Guid.NewGuid().ToString("N");
                var windowClass = new WndClassEx
                {
                    cbSize = (uint)Marshal.SizeOf<WndClassEx>(),
                    lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_windowProcedure),
                    hInstance = GetModuleHandle(null),
                    lpszClassName = _className
                };
                _classAtom = RegisterClassEx(ref windowClass);
                if (_classAtom == 0)
                {
                    _registrationError = Marshal.GetLastWin32Error();
                    return;
                }

                _registeredWindow = CreateWindowEx(
                    0,
                    _className,
                    string.Empty,
                    0,
                    0,
                    0,
                    0,
                    0,
                    new IntPtr(-3), // HWND_MESSAGE
                    IntPtr.Zero,
                    windowClass.hInstance,
                    IntPtr.Zero);
                if (_registeredWindow == IntPtr.Zero)
                {
                    _registrationError = Marshal.GetLastWin32Error();
                    return;
                }

                var device = new RawInputDevice
                {
                    UsagePage = 0x0D,
                    Usage = 0x05,
                    Flags = RidevInputSink | RidevDevNotify,
                    Target = _registeredWindow
                };

                if (!RegisterRawInputDevices(
                        new[] { device },
                        1,
                        (uint)Marshal.SizeOf<RawInputDevice>()))
                {
                    _registrationError = Marshal.GetLastWin32Error();
                    return;
                }

                IsRegistered = true;
                _ready.Set();

                while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            finally
            {
                foreach (var parser in _parsers.Values)
                    parser.Dispose();
                _parsers.Clear();

                if (_registeredWindow != IntPtr.Zero)
                {
                    DestroyWindow(_registeredWindow);
                    _registeredWindow = IntPtr.Zero;
                }

                if (_classAtom != 0)
                {
                    UnregisterClass(_className, GetModuleHandle(null));
                    _classAtom = 0;
                }

                _ready.Set();
                _threadId = 0;
            }
        }

        private IntPtr WindowProcedure(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam)
        {
            switch (message)
            {
                case WmInput:
                    HandleRawInput(lParam);
                    break;
                case WmInputDeviceChange:
                    ClearDeviceParsers();
                    break;
            }

            return DefWindowProc(window, message, wParam, lParam);
        }

        private void ClearDeviceParsers()
        {
            foreach (var parser in _parsers.Values)
                parser.Dispose();
            _parsers.Clear();
        }

        private void HandleRawInput(IntPtr rawInputHandle)
        {
            var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
            uint size = 0;
            if (GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize) != 0 || size == 0)
                return;

            var buffer = IntPtr.Zero;
            try
            {
                buffer = Marshal.AllocHGlobal((int)size);
                if (GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize) != size)
                    return;

                var header = Marshal.PtrToStructure<RawInputHeader>(buffer);
                var parser = GetDeviceParser(header.Device);
                if (parser == null)
                    return;

                var hidOffset = Marshal.SizeOf<RawInputHeader>();
                var reportSize = (uint)Marshal.ReadInt32(buffer, hidOffset);
                var reportCount = (uint)Marshal.ReadInt32(buffer, hidOffset + 4);
                if (reportSize == 0 || reportCount == 0)
                    return;

                var total = (long)reportSize * reportCount;
                if (total > (long)size - hidOffset - 8)
                    return;

                var dataPointer = IntPtr.Add(buffer, (int)(size - total));
                var report = new byte[reportSize];
                for (var index = 0u; index < reportCount; index++)
                {
                    Marshal.Copy(
                        IntPtr.Add(dataPointer, (int)(index * reportSize)),
                        report,
                        0,
                        report.Length);

                    if (!parser.TryParse(
                            report,
                            out var contacts,
                            out var contactCount,
                            out var horizontalRange,
                            out var verticalRange))
                        continue;

                    MergeContacts(contacts, contactCount, horizontalRange, verticalRange);
                }
            }
            finally
            {
                if (buffer != IntPtr.Zero)
                    Marshal.FreeHGlobal(buffer);
            }
        }

        private TouchpadDeviceParser? GetDeviceParser(IntPtr device)
        {
            if (_parsers.TryGetValue(device, out var existing))
                return existing;

            var parser = TouchpadDeviceParser.TryCreate(device);
            if (parser == null)
                return null;

            _parsers[device] = parser;
            return parser;
        }

        // Windows may split a multi-contact frame over more than one HID report: the first
        // report carries the contact count with a partial list, later reports carry the
        // remainder with a zero count. Merge them before publishing a frame.
        private void MergeContacts(
            List<TouchpadContact> contacts,
            int contactCount,
            int horizontalRange,
            int verticalRange)
        {
            if (contactCount <= 0 && contacts.Count == 0)
            {
                _pendingContacts.Clear();
                _pendingCount = 0;
                Publish(Array.Empty<TouchpadContact>(), horizontalRange, verticalRange);
                return;
            }

            if (contactCount > 0 && contacts.Count >= contactCount)
            {
                _pendingContacts.Clear();
                _pendingCount = 0;
                Publish(contacts.Take(contactCount).ToArray(), horizontalRange, verticalRange);
                return;
            }

            if (contactCount > 0)
            {
                _pendingCount = contactCount;
                _pendingContacts.Clear();
                _pendingContacts.AddRange(contacts);
                return;
            }

            foreach (var contact in contacts)
            {
                if (_pendingContacts.Any(candidate => candidate.ContactId == contact.ContactId))
                    continue;
                _pendingContacts.Add(contact);
            }

            if (_pendingCount > 0 && _pendingContacts.Count >= _pendingCount)
            {
                var merged = _pendingContacts.Take(_pendingCount).ToArray();
                _pendingContacts.Clear();
                _pendingCount = 0;
                Publish(merged, horizontalRange, verticalRange);
            }
            else if (_pendingContacts.Count > 10)
            {
                // Never let a broken report stream grow without bound.
                _pendingContacts.Clear();
                _pendingCount = 0;
            }
        }

        private void Publish(
            IReadOnlyList<TouchpadContact> contacts,
            int horizontalRange,
            int verticalRange)
        {
            if (!_loggedFirstFrame)
            {
                _loggedFirstFrame = true;
                Log.Write(
                    nameof(PrecisionControl),
                    $"Touchpad drag received its first precision touchpad frame ({contacts.Count} contacts).",
                    LogLevel.Info);
            }

            FrameReceived?.Invoke(new TouchpadFrame(
                Environment.TickCount64,
                contacts,
                horizontalRange,
                verticalRange));
        }

        private sealed class TouchpadDeviceParser : IDisposable
        {
            private TouchpadDeviceParser(IntPtr preparsedData, HidpCaps caps, HidpValueCaps[] valueCaps)
            {
                _preparsedData = preparsedData;
                _valueCaps = valueCaps;
                HorizontalRange = ResolveRange(0x30);
                VerticalRange = ResolveRange(0x31);
            }

            public int HorizontalRange { get; }
            public int VerticalRange { get; }

            public static TouchpadDeviceParser? TryCreate(IntPtr device)
            {
                uint preparsedSize = 0;
                if (GetRawInputDeviceInfo(device, RidiPreparsedData, IntPtr.Zero, ref preparsedSize) != 0 ||
                    preparsedSize == 0)
                    return null;

                var preparsedData = Marshal.AllocHGlobal((int)preparsedSize);
                if (GetRawInputDeviceInfo(device, RidiPreparsedData, preparsedData, ref preparsedSize) != preparsedSize)
                {
                    Marshal.FreeHGlobal(preparsedData);
                    return null;
                }

                if (HidP_GetCaps(preparsedData, out var caps) != HidpStatusSuccess)
                {
                    Marshal.FreeHGlobal(preparsedData);
                    return null;
                }

                var length = caps.NumberInputValueCaps;
                var valueCaps = new HidpValueCaps[length];
                if (HidP_GetValueCaps(HidpReportType.Input, valueCaps, ref length, preparsedData) != HidpStatusSuccess)
                {
                    Marshal.FreeHGlobal(preparsedData);
                    return null;
                }

                Array.Resize(ref valueCaps, length);
                return new TouchpadDeviceParser(preparsedData, caps, valueCaps);
            }

            public bool TryParse(
                byte[] report,
                out List<TouchpadContact> contacts,
                out int contactCount,
                out int horizontalRange,
                out int verticalRange)
            {
                contacts = new List<TouchpadContact>(5);
                contactCount = 0;
                horizontalRange = HorizontalRange;
                verticalRange = VerticalRange;

                var reportPointer = Marshal.AllocHGlobal(report.Length);
                try
                {
                    Marshal.Copy(report, 0, reportPointer, report.Length);

                    var slots = new Dictionary<ushort, ContactSlot>();
                    foreach (var valueCap in _valueCaps)
                    {
                        if (HidP_GetUsageValue(
                                HidpReportType.Input,
                                valueCap.UsagePage,
                                valueCap.LinkCollection,
                                valueCap.Usage,
                                out var value,
                                _preparsedData,
                                reportPointer,
                                (uint)report.Length) != HidpStatusSuccess)
                            continue;

                        if (valueCap.LinkCollection == 0)
                        {
                            if (valueCap.UsagePage == 0x0D && valueCap.Usage == 0x54)
                                contactCount = (int)value;
                            continue;
                        }

                        if (!slots.TryGetValue(valueCap.LinkCollection, out var slot))
                        {
                            slot = new ContactSlot();
                            slots[valueCap.LinkCollection] = slot;
                        }

                        switch (valueCap.UsagePage, valueCap.Usage)
                        {
                            case (0x0D, 0x51):
                                slot.ContactId = (int)value;
                                break;
                            case (0x01, 0x30):
                                slot.X = (int)value;
                                break;
                            case (0x01, 0x31):
                                slot.Y = (int)value;
                                break;
                        }
                    }

                    foreach (var slot in slots.OrderBy(pair => pair.Key).Select(pair => pair.Value))
                    {
                        if (slot.ContactId is not { } id || slot.X is not { } x || slot.Y is not { } y)
                            continue;
                        contacts.Add(new TouchpadContact(id, x, y));
                    }

                    return true;
                }
                finally
                {
                    Marshal.FreeHGlobal(reportPointer);
                }
            }

            private int ResolveRange(ushort usage)
            {
                var maximum = 0;
                foreach (var valueCap in _valueCaps)
                {
                    if (valueCap.UsagePage != 0x01 || valueCap.Usage != usage)
                        continue;
                    maximum = Math.Max(maximum, valueCap.LogicalMax - valueCap.LogicalMin);
                }

                return maximum > 0 ? maximum : DefaultCoordinateRange;
            }

            public void Dispose()
            {
                if (_preparsedData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_preparsedData);
                    _preparsedData = IntPtr.Zero;
                }
            }

            private sealed class ContactSlot
            {
                public int? ContactId { get; set; }
                public int? X { get; set; }
                public int? Y { get; set; }
            }

            private IntPtr _preparsedData;
            private readonly HidpValueCaps[] _valueCaps;
        }
        private readonly Dictionary<IntPtr, TouchpadDeviceParser> _parsers = new();
        private readonly List<TouchpadContact> _pendingContacts = new();
        private int _pendingCount;
        private readonly Thread _thread;
        private readonly ManualResetEventSlim _ready = new(false);
        private readonly WindowProcedureDelegate _windowProcedure;
        private string _className = string.Empty;
        private ushort _classAtom;
        private IntPtr _registeredWindow;
        private uint _threadId;
        private int _registrationError;
        private bool _loggedFirstFrame;
        private bool _disposed;

        private delegate IntPtr WindowProcedureDelegate(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct WndClassEx
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public IntPtr lpszMenuName;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string? lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Message
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int pointX;
            public int pointY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputDevice
        {
            public ushort UsagePage;
            public ushort Usage;
            public uint Flags;
            public IntPtr Target;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RawInputHeader
        {
            public uint Type;
            public uint Size;
            public IntPtr Device;
            public IntPtr wParam;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpCaps
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;

            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        private enum HidpReportType
        {
            Input,
            Output,
            Feature
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HidpValueCaps
        {
            public ushort UsagePage;
            public byte ReportId;

            [MarshalAs(UnmanagedType.U1)]
            public bool IsAlias;

            public ushort BitField;
            public ushort LinkCollection;
            public ushort LinkUsage;
            public ushort LinkUsagePage;

            [MarshalAs(UnmanagedType.U1)]
            public bool IsRange;
            [MarshalAs(UnmanagedType.U1)]
            public bool IsStringRange;
            [MarshalAs(UnmanagedType.U1)]
            public bool IsDesignatorRange;
            [MarshalAs(UnmanagedType.U1)]
            public bool IsAbsolute;
            [MarshalAs(UnmanagedType.U1)]
            public bool HasNull;

            public byte Reserved;
            public ushort BitSize;
            public ushort ReportCount;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 5)]
            public ushort[] Reserved2;

            public uint UnitsExp;
            public uint Units;
            public int LogicalMin;
            public int LogicalMax;
            public int PhysicalMin;
            public int PhysicalMax;

            public ushort UsageMin;
            public ushort UsageMax;
            public ushort StringMin;
            public ushort StringMax;
            public ushort DesignatorMin;
            public ushort DesignatorMax;
            public ushort DataIndexMin;
            public ushort DataIndexMax;

            public ushort Usage => UsageMin;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ushort RegisterClassEx(ref WndClassEx windowClass);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool UnregisterClass(string className, IntPtr instance);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWindowEx(
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

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyWindow(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DefWindowProc(IntPtr window, uint message, UIntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int GetMessage(out Message message, IntPtr window, uint min, uint max);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool TranslateMessage(ref Message message);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr DispatchMessage(ref Message message);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? moduleName);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterRawInputDevices(
            RawInputDevice[] devices,
            uint count,
            uint structureSize);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetRawInputData(
            IntPtr rawInput,
            uint command,
            IntPtr data,
            ref uint size,
            uint headerSize);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetRawInputDeviceInfo(
            IntPtr device,
            uint command,
            IntPtr data,
            ref uint size);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern uint HidP_GetCaps(IntPtr preparsedData, out HidpCaps caps);

        [DllImport("hid.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint HidP_GetValueCaps(
            HidpReportType reportType,
            [Out] HidpValueCaps[] valueCaps,
            ref ushort length,
            IntPtr preparsedData);

        [DllImport("hid.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern uint HidP_GetUsageValue(
            HidpReportType reportType,
            ushort usagePage,
            ushort linkCollection,
            ushort usage,
            out uint value,
            IntPtr preparsedData,
            IntPtr report,
            uint reportLength);
    }
}
