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
        public const string ScreenRelativeMode = "Screen Relative (Legacy)";
        public const string PointerAnchoredMode = "Pointer Anchored";
        internal const string PreviousPenCursorRelativeMode = "Pen Cursor Relative";
        internal const string PreviousPointerRelativeMode = "Pointer Relative";
        public const string LastActivePointerAnchor = "Last Active Pointer";
        public const string LastPenPositionAnchor = "Last Pen Position";
        public const string CurrentMousePositionAnchor = "Current Mouse Position";

        public static string[] ValidGlobalHotkeyKeys => GlobalHotkeyKeyMap.Keys.ToArray();
        public static string[] ValidPositioningModes =>
            new[] { ScreenRelativeMode, PointerAnchoredMode };
        public static string[] ValidActivationAnchorSources =>
            new[]
            {
                LastActivePointerAnchor,
                LastPenPositionAnchor,
                CurrentMousePositionAnchor
            };

        public event Action<IDeviceReport>? Emit;

        [SliderProperty("Precision Multiplier", 0.0f, 10f, 0.3f), DefaultPropertyValue(0.3f)]
        public float Scale { get; set; }

        [Property("Precision Area Positioning"), PropertyValidated(nameof(ValidPositioningModes))]
        [DefaultPropertyValue(ScreenRelativeMode)]
        public string? PositioningMode { get; set; } = ScreenRelativeMode;

        [Property("Activation Anchor Source"), PropertyValidated(nameof(ValidActivationAnchorSources))]
        [DefaultPropertyValue(LastActivePointerAnchor)]
        public string? ActivationAnchorSource { get; set; } = LastActivePointerAnchor;

        [SliderProperty("Anchor Position X (%)", 0f, 100f, 1f), DefaultPropertyValue(10f)]
        public float PointerPositionXPercent { get; set; } = 10f;

        [SliderProperty("Anchor Position Y (%)", 0f, 100f, 1f), DefaultPropertyValue(10f)]
        public float PointerPositionYPercent { get; set; } = 10f;

        [BooleanProperty("Show Precision Border", "Show a translucent, click-through border around the precision area.")]
        [DefaultPropertyValue(true)]
        public bool ShowBorder { get; set; } = true;

        [SliderProperty("Border Thickness", 1.0f, 6.0f, 1.0f), DefaultPropertyValue(2.0f)]
        public float BorderThickness { get; set; } = 2.0f;

        [Property("Border Color (#RRGGBB)"), DefaultPropertyValue(BorderColorParser.DefaultColor)]
        public string? BorderColor { get; set; } = BorderColorParser.DefaultColor;

        [SliderProperty("Border Opacity", 0.05f, 1.0f, 0.05f), DefaultPropertyValue(0.4f)]
        public float BorderOpacity { get; set; } = 0.4f;

        [BooleanProperty("Enable Global Hotkey", "Let a Windows shortcut toggle precision at any time.")]
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

        [BooleanProperty("Enable Nudge Hotkeys", "Move the active precision area with global shortcuts while the pen is not writing.")]
        [DefaultPropertyValue(true)]
        public bool EnableNudgeHotkeys { get; set; } = true;

        [Property("Nudge Up Key"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("Up")]
        public string? NudgeUpKey { get; set; } = "Up";

        [Property("Nudge Down Key"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("Down")]
        public string? NudgeDownKey { get; set; } = "Down";

        [Property("Nudge Left Key"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("Left")]
        public string? NudgeLeftKey { get; set; } = "Left";

        [Property("Nudge Right Key"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("Right")]
        public string? NudgeRightKey { get; set; } = "Right";

        [SliderProperty("Horizontal Nudge (%)", 1f, 100f, 1f), DefaultPropertyValue(20f)]
        public float HorizontalNudgePercent { get; set; } = 20f;

        [SliderProperty("Vertical Nudge (%)", 1f, 100f, 1f), DefaultPropertyValue(20f)]
        public float VerticalNudgePercent { get; set; } = 20f;

        [TabletReference]
        public TabletReference? Tablet { get; set; }

        public PipelinePosition Position => PipelinePosition.PostTransform;

        internal long LastPointerActivityTimestamp =>
            Math.Max(
                Interlocked.Read(ref _lastPenMovementTimestamp),
                PointerActivityTracker.LastMouseMovementTimestamp);

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
                    _isWriting = false;
            }

            if (value is ITabletReport report)
            {
                lock (_stateLock)
                {
                    var rawPosition = report.Position;
                    _isWriting = report.Pressure > 0;
                    _lastRawPenPosition = rawPosition;
                    _hasLastRawPenPosition = true;

                    if (!_isActive)
                        RecordPenCursorPosition(rawPosition);

                    if (_isActive && _awaitingPenBaseline)
                    {
                        _startingPoint = rawPosition;
                        _awaitingPenBaseline = false;
                    }

                    while (_pendingActions.TryDequeue(out var action))
                        ApplyAction(action, rawPosition);

                    if (_isActive)
                    {
                        report.Position = _pointerRelativeActive
                            ? PrecisionPositionCalculator.MapPointerRelative(
                                rawPosition,
                                _startingPoint,
                                _activationAnchor,
                                Scale,
                                _precisionBounds)
                            : _activationAnchor +
                                ((rawPosition - _startingPoint) * Scale);
                    }

                    RecordPenCursorPosition(report.Position);
                }
                value = report;
            }

            Emit?.Invoke(value);
        }

        internal void Queue(PrecisionControlAction action)
        {
            _pendingActions.Enqueue(action);
        }

        internal bool QueueGlobalAction(PrecisionControlAction action)
        {
            lock (_stateLock)
            {
                if (IsNudgeAction(action) &&
                    (!_isActive || _isWriting))
                {
                    return false;
                }

                var currentPosition = _hasLastRawPenPosition
                    ? _lastRawPenPosition
                    : Vector2.Zero;
                ApplyAction(action, currentPosition);
                return true;
            }
        }

        internal bool QueueGlobalToggle()
        {
            return QueueGlobalAction(PrecisionControlAction.Toggle);
        }

        private void ApplyAction(PrecisionControlAction action, Vector2 currentPosition)
        {
            switch (action)
            {
                case PrecisionControlAction.Toggle:
                    _isActive = !_isActive;
                    if (_isActive)
                        ActivateAt(currentPosition);
                    else
                    {
                        _overlay?.Hide();
                    }
                    break;
                case PrecisionControlAction.Activate:
                    _isActive = true;
                    ActivateAt(currentPosition);
                    break;
                case PrecisionControlAction.Deactivate:
                    _isActive = false;
                    _overlay?.Hide();
                    break;
                case PrecisionControlAction.NudgeUp:
                case PrecisionControlAction.NudgeDown:
                case PrecisionControlAction.NudgeLeft:
                case PrecisionControlAction.NudgeRight:
                    Nudge(action);
                    break;
            }
        }

        private void ActivateAt(Vector2 currentPosition)
        {
            _activationAnchor = ResolveActivationAnchor(currentPosition);
            _startingPoint = _hasLastRawPenPosition
                ? _lastRawPenPosition
                : currentPosition;
            _awaitingPenBaseline = !_hasLastRawPenPosition;
            _pointerRelativeActive =
                (string.Equals(
                    PositioningMode,
                    PointerAnchoredMode,
                    StringComparison.Ordinal) ||
                string.Equals(
                    PositioningMode,
                    PreviousPenCursorRelativeMode,
                    StringComparison.Ordinal) ||
                string.Equals(
                    PositioningMode,
                    PreviousPointerRelativeMode,
                    StringComparison.Ordinal)) &&
                (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ||
                    OutputAreaProvider != null);

            var outputArea = OutputAreaProvider?.Invoke(_activationAnchor) ??
                PrecisionControlDesktop.GetOutputArea(_activationAnchor);
            _precisionBounds = _pointerRelativeActive
                ? PrecisionBoundsCalculator.CalculatePointerRelative(
                    outputArea,
                    _activationAnchor,
                    Scale,
                    PointerPositionXPercent,
                    PointerPositionYPercent)
                : PrecisionBoundsCalculator.CalculateScreenRelative(
                    outputArea,
                    _activationAnchor,
                    Scale);

            ShowPrecisionBorder();
        }

        private Vector2 ResolveActivationAnchor(Vector2 fallback)
        {
            var mouse = MouseActivityProvider?.Invoke() ??
                PointerActivityTracker.GetMouseActivity();
            var hasPen = _hasLastPenCursorPosition;

            if (string.Equals(
                ActivationAnchorSource,
                LastPenPositionAnchor,
                StringComparison.Ordinal))
            {
                if (hasPen)
                    return _lastPenCursorPosition;
                if (mouse.Available)
                    return mouse.Position;
                return fallback;
            }

            if (string.Equals(
                ActivationAnchorSource,
                CurrentMousePositionAnchor,
                StringComparison.Ordinal))
            {
                if (mouse.Available)
                    return mouse.Position;
                if (hasPen)
                    return _lastPenCursorPosition;
                return fallback;
            }

            if (hasPen &&
                (!mouse.Available ||
                    Interlocked.Read(ref _lastPenMovementTimestamp) >=
                    mouse.Timestamp))
            {
                return _lastPenCursorPosition;
            }

            return mouse.Available ? mouse.Position :
                hasPen ? _lastPenCursorPosition :
                fallback;
        }

        private void Nudge(PrecisionControlAction action)
        {
            var horizontalDistance = (int)Math.Round(
                _precisionBounds.Width *
                (Math.Clamp(HorizontalNudgePercent, 0f, 100f) / 100f));
            var verticalDistance = (int)Math.Round(
                _precisionBounds.Height *
                (Math.Clamp(VerticalNudgePercent, 0f, 100f) / 100f));
            var deltaX = action switch
            {
                PrecisionControlAction.NudgeLeft => -horizontalDistance,
                PrecisionControlAction.NudgeRight => horizontalDistance,
                _ => 0
            };
            var deltaY = action switch
            {
                PrecisionControlAction.NudgeUp => -verticalDistance,
                PrecisionControlAction.NudgeDown => verticalDistance,
                _ => 0
            };

            _precisionBounds = _precisionBounds.Translate(deltaX, deltaY);

            if (_hasLastRawPenPosition && _hasLastPenCursorPosition)
            {
                _startingPoint = _lastRawPenPosition;
                _activationAnchor = _lastPenCursorPosition;
            }

            ShowPrecisionBorder();
        }

        private void RecordPenCursorPosition(Vector2 position)
        {
            if (!_hasLastPenCursorPosition ||
                Vector2.DistanceSquared(_lastPenCursorPosition, position) > 0.01f)
            {
                Interlocked.Exchange(
                    ref _lastPenMovementTimestamp,
                    Stopwatch.GetTimestamp());
            }

            _lastPenCursorPosition = position;
            _hasLastPenCursorPosition = true;
            PointerActivityTracker.RecordPenPosition(position);
        }

        private static bool IsNudgeAction(PrecisionControlAction action)
        {
            return action is
                PrecisionControlAction.NudgeUp or
                PrecisionControlAction.NudgeDown or
                PrecisionControlAction.NudgeLeft or
                PrecisionControlAction.NudgeRight;
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
                _precisionBounds,
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
        private Vector2 _activationAnchor;
        private OverlayBounds _precisionBounds;
        private bool _isActive;
        private bool _pointerRelativeActive;
        private bool _isWriting;
        private bool _awaitingPenBaseline;
        private bool _hasLastRawPenPosition;
        private bool _hasLastPenCursorPosition;
        private Vector2 _lastRawPenPosition;
        private Vector2 _lastPenCursorPosition;
        private long _lastPenMovementTimestamp;
        private readonly object _stateLock = new();
        internal IPrecisionControlOverlay? Overlay
        {
            set => _overlay = value;
        }
        internal Func<Vector2, OverlayBounds>? OutputAreaProvider { get; set; }
        internal Func<PointerActivitySample>? MouseActivityProvider { get; set; }
        private IPrecisionControlOverlay? _overlay;
    }

    internal static class PrecisionPositionCalculator
    {
        public static Vector2 MapPointerRelative(
            Vector2 currentPosition,
            Vector2 startingPosition,
            Vector2 activationAnchor,
            float scale,
            OverlayBounds bounds)
        {
            var mapped = activationAnchor +
                ((currentPosition - startingPosition) * scale);
            return new Vector2(
                Math.Clamp(mapped.X, bounds.Left, bounds.Right - 1),
                Math.Clamp(mapped.Y, bounds.Top, bounds.Bottom - 1));
        }
    }

    internal enum PrecisionControlAction
    {
        Toggle,
        Activate,
        Deactivate,
        NudgeUp,
        NudgeDown,
        NudgeLeft,
        NudgeRight
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
            return TryQueueGlobalAction(
                PrecisionControlAction.Toggle,
                gesture);
        }

        public static bool TryQueueGlobalAction(
            PrecisionControlAction requestedAction,
            HotkeyGesture? gesture = null)
        {
            lock (_syncRoot)
            {
                var candidates = _filters
                    .Select(candidate =>
                    {
                        var action = requestedAction;
                        var matches = gesture.HasValue
                            ? PrecisionControlGlobalHotkeyManager.TryMatch(
                                candidate,
                                gesture.Value,
                                out action)
                            : IsActionEnabled(candidate, action);
                        return new { Filter = candidate, Action = action, Matches = matches };
                    })
                    .Where(candidate => candidate.Matches)
                    .OrderByDescending(candidate =>
                        candidate.Filter.LastPointerActivityTimestamp);

                foreach (var candidate in candidates)
                {
                    if (candidate.Filter.QueueGlobalAction(candidate.Action))
                        return true;
                }

                return false;
            }
        }

        private static bool IsActionEnabled(
            PrecisionControl filter,
            PrecisionControlAction action)
        {
            return action == PrecisionControlAction.Toggle
                ? filter.EnableGlobalHotkey
                : action is
                    PrecisionControlAction.NudgeUp or
                    PrecisionControlAction.NudgeDown or
                    PrecisionControlAction.NudgeLeft or
                    PrecisionControlAction.NudgeRight
                    ? filter.EnableNudgeHotkeys
                    : true;
        }

        private static readonly object _syncRoot = new();
        private static readonly List<PrecisionControl> _filters = new();
    }
}
