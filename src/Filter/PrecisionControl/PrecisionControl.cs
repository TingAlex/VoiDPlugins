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
    public partial class PrecisionControl : IPositionedPipelineElement<IDeviceReport>, IDisposable
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

        // Hidden from the generated settings UI; retain the existing shortcut defaults.
        public bool EnableNudgeHotkeys { get; set; } = true;

        public string? NudgeUpKey { get; set; } = "Up";

        public string? NudgeDownKey { get; set; } = "Down";

        public string? NudgeLeftKey { get; set; } = "Left";

        public string? NudgeRightKey { get; set; } = "Right";

        public float HorizontalNudgePercent { get; set; } = 20f;

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
            if (EnablePenNavigation && !EnablePenPageNavigation)
                Log.Write(nameof(PrecisionControl), "Third-button mapping controls enabled; experimental page navigation is disabled and hidden.", LogLevel.Info);
            else if (EnablePenNavigation && EnablePenDoubleTapPagePan)
                Log.Write(nameof(PrecisionControl),
                    $"Pen navigation enabled; double-tap toggles contact page pan; interval: {PenDoubleTapIntervalMilliseconds} ms; hover hold disabled; direct API available: {PenDirectPan.IsSupported}.",
                    PenDirectPan.IsSupported ? LogLevel.Info : LogLevel.Warning);
            else if (EnablePenNavigation)
                Log.Write(nameof(PrecisionControl),
                    $"Pen navigation enabled; short tap toggles mapping, hover-scroll API available: {PenPageScroller.IsAvailable}, hold delay: {PenNavigationHoldMilliseconds} ms.",
                    PenPageScroller.IsAvailable ? LogLevel.Info : LogLevel.Warning);
            if (EnablePenNavigation && EnablePenPageNavigation && PenScrollMode == DirectTouchPanMode)
                Log.Write(nameof(PrecisionControl),
                    $"Direct page pan selected; API exports available: {PenDirectPan.IsSupported}. Application panning and cancellation still require verification.",
                    PenDirectPan.IsSupported ? LogLevel.Info : LogLevel.Warning);
        }

        public void Consume(IDeviceReport value)
        {
            if (value is OutOfRangeReport)
            {
                lock (_stateLock)
                {
                    _isWriting = false;
                    ResetPenDrag(preservePageMode: true);
                }
            }

            if (value is ITabletReport report)
            {
                lock (_stateLock)
                {
                    var rawPosition = report.Position;
                    var wasWriting = _isWriting;
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

                    var suppressPenInput = EnablePenNavigation || _penNavigationPhase != PenNavigationPhase.Idle || PenContactNavigationCaptured
                        ? HandlePenNavigation(report, rawPosition, wasWriting)
                        : HandlePenDrag(report, rawPosition, wasWriting);
                    report.Position = suppressPenInput
                        ? _penDragCursorPosition
                        : MapPenPosition(rawPosition);

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
                if (action == PrecisionControlAction.Reposition &&
                    (_isWriting || PenDragBlocksMovement))
                    return false;
                if (IsNudgeAction(action) &&
                    (!_isActive || _isWriting || PenDragBlocksMovement))
                    return false;

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

        internal void MoveFromDial(string direction)
        {
            lock (_stateLock)
            {
                // Never queue dial input: inactive/stroke-time ticks must not replay later.
                if (!_isActive || _isWriting || PenDragBlocksMovement)
                    return;
                var step = direction is "Left" or "Right"
                    ? HorizontalDialStep : VerticalDialStep;
                if (!float.IsFinite(step) || step <= 0)
                    return;
                var distance = (int)Math.Round(Math.Clamp(step, 1f, 1000f));
                switch (direction)
                {
                    case "Left": TranslateArea(-distance, 0); break;
                    case "Right": TranslateArea(distance, 0); break;
                    case "Up": TranslateArea(0, -distance); break;
                    case "Down": TranslateArea(0, distance); break;
                }
            }
        }

        private void ApplyAction(PrecisionControlAction action, Vector2 currentPosition)
        {
            // Bound actions must follow the same exclusion as hotkeys/dials.
            // Discard them now; never replay movement after the drag ends.
            if (PenDragBlocksMovement &&
                (action == PrecisionControlAction.Reposition || IsNudgeAction(action)))
                return;

            switch (action)
            {
                case PrecisionControlAction.Toggle:
                    _isActive = !_isActive;
                    if (_isActive)
                        ActivateAt(currentPosition);
                    else
                    {
                        CancelPenDrag();
                        _overlay?.Hide();
                    }
                    break;
                case PrecisionControlAction.Activate:
                    _isActive = true;
                    ActivateAt(currentPosition);
                    break;
                case PrecisionControlAction.Deactivate:
                    _isActive = false;
                    CancelPenDrag();
                    _overlay?.Hide();
                    break;
                case PrecisionControlAction.Reposition:
                    _isActive = true;
                    ActivateAt(
                        currentPosition,
                        ResolveCurrentMouseAnchor(currentPosition));
                    break;
                case PrecisionControlAction.NudgeUp:
                case PrecisionControlAction.NudgeDown:
                case PrecisionControlAction.NudgeLeft:
                case PrecisionControlAction.NudgeRight:
                    Nudge(action);
                    break;
            }
        }

        private void ActivateAt(
            Vector2 currentPosition,
            Vector2? anchorOverride = null)
        {
            CancelPenDrag();
            _activationAnchor = anchorOverride ??
                ResolveActivationAnchor(currentPosition);
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

            _outputArea = OutputAreaProvider?.Invoke(_activationAnchor) ??
                PrecisionControlDesktop.GetOutputArea(_activationAnchor);
            _precisionBounds = _pointerRelativeActive
                ? PrecisionBoundsCalculator.CalculatePointerRelative(
                    _outputArea,
                    _activationAnchor,
                    Scale,
                    PointerPositionXPercent,
                    PointerPositionYPercent)
                : PrecisionBoundsCalculator.CalculateScreenRelative(
                    _outputArea,
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

        private Vector2 ResolveCurrentMouseAnchor(Vector2 fallback)
        {
            var mouse = MouseActivityProvider?.Invoke() ??
                PointerActivityTracker.GetMouseActivity();
            if (mouse.Available)
                return mouse.Position;
            if (_hasLastPenCursorPosition)
                return _lastPenCursorPosition;
            return fallback;
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

            TranslateArea(deltaX, deltaY);
        }

        private void TranslateArea(int deltaX, int deltaY)
        {
            _precisionBounds = _precisionBounds.Translate(deltaX, deltaY);

            if (!_pointerRelativeActive)
                _activationAnchor += new Vector2(deltaX, deltaY);

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
            lock (_stateLock)
                ResetPenDrag();
            PrecisionControlGlobalHotkeyManager.Unregister(this);
            PrecisionControlCoordinator.Unregister(this);
            _overlay?.Dispose();
            _overlay = null;
        }

        private readonly ConcurrentQueue<PrecisionControlAction> _pendingActions = new();
        private Vector2 _startingPoint;
        private Vector2 _activationAnchor;
        private OverlayBounds _outputArea;
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
        public static Vector2 MapOutputAreaToBounds(
            Vector2 currentPosition,
            OverlayBounds outputArea,
            OverlayBounds bounds)
        {
            var horizontalRatio =
                (currentPosition.X - outputArea.Left) /
                Math.Max(1, outputArea.Width);
            var verticalRatio =
                (currentPosition.Y - outputArea.Top) /
                Math.Max(1, outputArea.Height);
            var mapped = new Vector2(
                bounds.Left + (horizontalRatio * bounds.Width),
                bounds.Top + (verticalRatio * bounds.Height));
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
        Reposition,
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

        public static void MoveFromDial(TabletReference tablet, string direction)
        {
            lock (_syncRoot)
            {
                var filter = _filters.FirstOrDefault(candidate =>
                    ReferenceEquals(candidate.Tablet, tablet));
                filter?.MoveFromDial(direction);
            }
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
                : action == PrecisionControlAction.Reposition
                    ? filter.EnableRepositionHotkey
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
