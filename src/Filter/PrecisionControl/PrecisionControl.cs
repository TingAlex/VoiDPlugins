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

        [ToolTip("控制映射框宽高相对屏幕的比例。0.25 表示宽高各为屏幕的 25%；越小越精细。修改后重新启用映射框。")]
        [SliderProperty("01 映射区域 · 缩放比例", 0.0f, 10f, 0.3f), DefaultPropertyValue(0.3f)]
        public float Scale { get; set; }

        [ToolTip("旋钮 1 每格左右移动的距离，范围 1–1000 像素，默认 40。仅移动现有框；没有框或正在落笔时忽略旋转。")]
        [SliderProperty("02 双旋钮 · 水平步长（像素/格）", 1f, 1000f, 1f), DefaultPropertyValue(40f)]
        public float HorizontalDialStep { get; set; } = 40f;

        [ToolTip("旋钮 2 每格上下移动的距离，范围 1–1000 像素，默认 40。抬笔或笔离开感应区后仍可移动；不改变框大小。")]
        [SliderProperty("02 双旋钮 · 竖直步长（像素/格）", 1f, 1000f, 1f), DefaultPropertyValue(40f)]
        public float VerticalDialStep { get; set; } = 40f;

        [ToolTip("只控制边框可见性，不关闭精确映射。边框可穿透点击，不遮挡操作。")]
        [BooleanProperty("03 边框外观 · 显示边框", "显示映射范围的轮廓")]
        [DefaultPropertyValue(true)]
        public bool ShowBorder { get; set; } = true;

        [ToolTip("输入 #RRGGBB，例如 #000000 黑色、#FFFFFF 白色。默认黑色。")]
        [Property("03 边框外观 · 颜色"), DefaultPropertyValue(BorderColorParser.DefaultColor)]
        public string? BorderColor { get; set; } = BorderColorParser.DefaultColor;

        [ToolTip("范围 0.05–1；0.4 表示 40% 不透明，数值越大越明显。")]
        [SliderProperty("03 边框外观 · 不透明度", 0.05f, 1.0f, 0.05f), DefaultPropertyValue(0.4f)]
        public float BorderOpacity { get; set; } = 0.4f;

        [ToolTip("边框线条的粗细，建议 1–6 像素；默认 2。")]
        [SliderProperty("03 边框外观 · 线宽（像素）", 1.0f, 6.0f, 1.0f), DefaultPropertyValue(2.0f)]
        public float BorderThickness { get; set; } = 2.0f;

        [ToolTip("使用下方主键和第 05 组公共修饰键组合，显示或隐藏映射框。默认 Ctrl+Alt+Shift+P。")]
        [BooleanProperty("04 快捷键 · 显示/隐藏开关", "启用显示/隐藏快捷键")]
        [DefaultPropertyValue(true)]
        public bool EnableGlobalHotkey { get; set; } = true;

        [ToolTip("默认 P。与第 05 组勾选的 Ctrl、Alt、Shift、Windows 键一起按下。")]
        [Property("04 快捷键 · 显示/隐藏主键"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("P")]
        public string? GlobalHotkeyKey { get; set; } = "P";

        [ToolTip("将框重新放到当前鼠标位置；没有框时会创建。默认 Ctrl+Alt+Shift+R，书写期间不执行。")]
        [BooleanProperty("04 快捷键 · 鼠标定位开关", "启用鼠标定位快捷键")]
        [DefaultPropertyValue(true)]
        public bool EnableRepositionHotkey { get; set; } = true;

        [ToolTip("默认 R。与公共修饰键一起按下，将映射框定位到鼠标处。")]
        [Property("04 快捷键 · 鼠标定位主键"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("R")]
        public string? RepositionHotkeyKey { get; set; } = "R";

        // Hidden from the generated settings UI; retain the existing shortcut defaults.
        public bool EnableNudgeHotkeys { get; set; } = true;

        public string? NudgeUpKey { get; set; } = "Up";

        public string? NudgeDownKey { get; set; } = "Down";

        public string? NudgeLeftKey { get; set; } = "Left";

        public string? NudgeRightKey { get; set; } = "Right";

        public float HorizontalNudgePercent { get; set; } = 20f;

        public float VerticalNudgePercent { get; set; } = 20f;

        [ToolTip("对显示/隐藏、鼠标定位、方向平移这三类快捷键共同生效。")]
        [BooleanProperty("05 公共修饰键 · Ctrl", "组合键包含 Ctrl"), DefaultPropertyValue(true)]
        public bool HotkeyCtrl { get; set; } = true;

        [ToolTip("勾选后，触发上述快捷键时还必须按住 Alt。")]
        [BooleanProperty("05 公共修饰键 · Alt", "组合键包含 Alt"), DefaultPropertyValue(true)]
        public bool HotkeyAlt { get; set; } = true;

        [ToolTip("勾选后，触发上述快捷键时还必须按住 Shift。")]
        [BooleanProperty("05 公共修饰键 · Shift", "组合键包含 Shift"), DefaultPropertyValue(true)]
        public bool HotkeyShift { get; set; } = true;

        [ToolTip("勾选后，触发上述快捷键时还必须按住 Windows 键；默认不勾选。")]
        [BooleanProperty("05 公共修饰键 · Windows", "组合键包含 Windows"), DefaultPropertyValue(false)]
        public bool HotkeyWindows { get; set; }

        [ToolTip("Pointer Anchored：整个数位板按比例映射到框内，适合旋钮移动。Screen Relative (Legacy)：旧版相对位移方式。更改后重新启用框。")]
        [Property("06 进阶定位 · 映射方式"), PropertyValidated(nameof(ValidPositioningModes))]
        [DefaultPropertyValue(ScreenRelativeMode)]
        public string? PositioningMode { get; set; } = ScreenRelativeMode;

        [ToolTip("Last Active Pointer：最近活动的笔或鼠标；Last Pen Position：上次笔位置；Current Mouse Position：当前鼠标位置。只影响启用时的位置。")]
        [Property("06 进阶定位 · 初始位置来源"), PropertyValidated(nameof(ValidActivationAnchorSources))]
        [DefaultPropertyValue(LastActivePointerAnchor)]
        public string? ActivationAnchorSource { get; set; } = LastActivePointerAnchor;

        [ToolTip("Pointer Anchored 模式下，初始指针在框内的横向位置：0 为左边，50 为中间，100 为右边。更改后重新启用框。")]
        [SliderProperty("06 进阶定位 · 锚点横向位置（%）", 0f, 100f, 1f), DefaultPropertyValue(10f)]
        public float PointerPositionXPercent { get; set; } = 10f;

        [ToolTip("Pointer Anchored 模式下，初始指针在框内的竖向位置：0 为顶边，50 为中间，100 为底边。更改后重新启用框。")]
        [SliderProperty("06 进阶定位 · 锚点竖向位置（%）", 0f, 100f, 1f), DefaultPropertyValue(10f)]
        public float PointerPositionYPercent { get; set; } = 10f;

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
                            ? PrecisionPositionCalculator.MapOutputAreaToBounds(
                                rawPosition,
                                _outputArea,
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
                if (action == PrecisionControlAction.Reposition &&
                    _isWriting)
                    return false;
                if (IsNudgeAction(action) &&
                    (!_isActive || _isWriting))
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
                if (!_isActive || _isWriting)
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
