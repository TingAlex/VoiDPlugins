using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Attributes;
using OpenTabletDriver.Plugin.DependencyInjection;
using OpenTabletDriver.Plugin.Output;
using OpenTabletDriver.Plugin.Tablet;

namespace VoiDPlugins.Filter
{
    [PluginName("Precision Control (TingAlex Enhanced)")]
    public class PrecisionControlBinding : IStateBinding
    {
        public static string[] ValidModes => new[] { "Toggle", "Hold" };

        [Property("Mode"), PropertyValidated(nameof(ValidModes))]
        public string? Mode { set; get; }

        public void Press(TabletReference tablet, IDeviceReport report)
        {
            if (Mode == "Toggle")
                PrecisionControlCoordinator.Request(tablet, PrecisionControlAction.Toggle);
            else if (Mode == "Hold")
                PrecisionControlCoordinator.Request(tablet, PrecisionControlAction.Activate);
        }

        public void Release(TabletReference tablet, IDeviceReport report)
        {
            if (Mode == "Hold")
                PrecisionControlCoordinator.Request(tablet, PrecisionControlAction.Deactivate);
        }
    }

    [PluginName("Precision Control (TingAlex Enhanced)")]
    public class PrecisionControl : IPositionedPipelineElement<IDeviceReport>, IDisposable
    {
        public static string[] ValidGlobalHotkeyKeys => GlobalHotkeyKeyMap.Keys.ToArray();

        public event Action<IDeviceReport>? Emit;

        [SliderProperty("Precision Multiplier", 0.0f, 10f, 0.3f), DefaultPropertyValue(0.3f)]
        public float Scale { get; set; }

        [BooleanProperty("Show Precision Border", "Show a translucent, click-through border around the precision area.")]
        [DefaultPropertyValue(true)]
        public bool ShowBorder { get; set; } = true;

        [SliderProperty("Border Thickness", 1.0f, 6.0f, 1.0f), DefaultPropertyValue(2.0f)]
        public float BorderThickness { get; set; } = 2.0f;

        [Property("Border Color (#RRGGBB)"), DefaultPropertyValue(BorderColorParser.DefaultColor)]
        public string? BorderColor { get; set; } = BorderColorParser.DefaultColor;

        [SliderProperty("Border Opacity", 0.05f, 1.0f, 0.05f), DefaultPropertyValue(0.4f)]
        public float BorderOpacity { get; set; } = 0.4f;

        [BooleanProperty("Enable Global Hotkey", "Let a Windows shortcut toggle precision while the pen is in range.")]
        [DefaultPropertyValue(true)]
        public bool EnableGlobalHotkey { get; set; } = true;

        [Property("Global Hotkey Key"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("P")]
        public string? GlobalHotkeyKey { get; set; } = "P";

        [BooleanProperty("Hotkey Ctrl", "Require the Ctrl key."), DefaultPropertyValue(true)]
        public bool HotkeyCtrl { get; set; } = true;

        [BooleanProperty("Hotkey Alt", "Require the Alt key."), DefaultPropertyValue(true)]
        public bool HotkeyAlt { get; set; } = true;

        [BooleanProperty("Hotkey Shift", "Require the Shift key."), DefaultPropertyValue(true)]
        public bool HotkeyShift { get; set; } = true;

        [BooleanProperty("Hotkey Windows", "Require the Windows key."), DefaultPropertyValue(false)]
        public bool HotkeyWindows { get; set; }

        [TabletReference]
        public TabletReference? Tablet { get; set; }

        public PipelinePosition Position => PipelinePosition.PostTransform;

        internal bool PenInRange
        {
            get
            {
                lock (_stateLock)
                    return _penInRange;
            }
        }

        internal long LastInRangeTimestamp => Interlocked.Read(ref _lastInRangeTimestamp);

        [OnDependencyLoad]
        public void Initialize()
        {
            PrecisionControlCoordinator.Register(this);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                PrecisionControlGlobalHotkeyManager.Register(this);
        }

        public void Consume(IDeviceReport value)
        {
            if (value is OutOfRangeReport)
            {
                lock (_stateLock)
                    _penInRange = false;
            }

            if (value is ITabletReport report)
            {
                lock (_stateLock)
                {
                    _penInRange = true;
                    _lastInRangePosition = report.Position;
                    Interlocked.Exchange(ref _lastInRangeTimestamp, Stopwatch.GetTimestamp());

                    while (_pendingActions.TryDequeue(out var action))
                        ApplyAction(action, report.Position);

                    if (_isActive)
                    {
                        var delta = (report.Position - _startingPoint) * Scale;
                        report.Position = _startingPoint + delta;
                    }
                }
                value = report;
            }

            Emit?.Invoke(value);
        }

        internal void Queue(PrecisionControlAction action)
        {
            _pendingActions.Enqueue(action);
        }

        internal bool QueueGlobalToggle()
        {
            lock (_stateLock)
            {
                if (!_penInRange)
                    return false;

                ApplyAction(PrecisionControlAction.Toggle, _lastInRangePosition);
                return true;
            }
        }

        private void ApplyAction(PrecisionControlAction action, Vector2 currentPosition)
        {
            switch (action)
            {
                case PrecisionControlAction.Toggle:
                    _isActive = !_isActive;
                    if (_isActive)
                    {
                        _startingPoint = currentPosition;
                        ShowPrecisionBorder();
                    }
                    else
                    {
                        _overlay?.Hide();
                    }
                    break;
                case PrecisionControlAction.Activate:
                    _isActive = true;
                    _startingPoint = currentPosition;
                    ShowPrecisionBorder();
                    break;
                case PrecisionControlAction.Deactivate:
                    _isActive = false;
                    _overlay?.Hide();
                    break;
            }
        }

        private void ShowPrecisionBorder()
        {
            if (!ShowBorder)
            {
                _overlay?.Hide();
                return;
            }

            var borderColor = 0u;
            if (!BorderColorParser.TryParse(BorderColor, out borderColor))
            {
                Log.Write(
                    nameof(PrecisionControl),
                    $"Invalid border color '{BorderColor}'. Using {BorderColorParser.DefaultColor}.",
                    LogLevel.Warning);
            }

            _overlay ??= PrecisionControlOverlayFactory.Create(borderColor);
            _overlay?.Show(
                _startingPoint,
                Scale,
                BorderThickness,
                BorderOpacity);
        }

        public void Dispose()
        {
            PrecisionControlGlobalHotkeyManager.Unregister(this);
            PrecisionControlCoordinator.Unregister(this);
            _overlay?.Dispose();
            _overlay = null;
        }

        private readonly ConcurrentQueue<PrecisionControlAction> _pendingActions = new();
        private Vector2 _startingPoint;
        private bool _isActive;
        private bool _penInRange;
        private Vector2 _lastInRangePosition;
        private long _lastInRangeTimestamp;
        private readonly object _stateLock = new();
        internal IPrecisionControlOverlay? Overlay
        {
            set => _overlay = value;
        }
        private IPrecisionControlOverlay? _overlay;
    }

    internal enum PrecisionControlAction
    {
        Toggle,
        Activate,
        Deactivate
    }

    internal static class PrecisionControlCoordinator
    {
        public static void Register(PrecisionControl filter)
        {
            lock (_syncRoot)
            {
                if (!_filters.Contains(filter))
                    _filters.Add(filter);
            }
        }

        public static void Unregister(PrecisionControl filter)
        {
            lock (_syncRoot)
                _filters.Remove(filter);
        }

        public static void Request(TabletReference tablet, PrecisionControlAction action)
        {
            lock (_syncRoot)
            {
                var filter = _filters.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.Tablet, tablet));
                filter?.Queue(action);
            }
        }

        public static bool TryQueueGlobalToggle(HotkeyGesture? gesture = null)
        {
            lock (_syncRoot)
            {
                var filter = _filters
                    .Where(candidate =>
                        candidate.EnableGlobalHotkey &&
                        candidate.PenInRange &&
                        (!gesture.HasValue ||
                            PrecisionControlGlobalHotkeyManager.Matches(
                                candidate,
                                gesture.Value)))
                    .OrderByDescending(candidate => candidate.LastInRangeTimestamp)
                    .FirstOrDefault();

                return filter?.QueueGlobalToggle() ?? false;
            }
        }

        private static readonly object _syncRoot = new();
        private static readonly List<PrecisionControl> _filters = new();
    }
}
