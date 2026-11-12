using System.Runtime.InteropServices;
using System.Text;
using Windows.UI.Input;

namespace TouchpadPenProbe;

internal sealed class ProbeForm : Form
{
    private readonly Label _apiStatus = CreateValueLabel();
    private readonly Label _penStatus = CreateValueLabel();
    private readonly Label _twoFingerStatus = CreateValueLabel();
    private readonly Label _translatedInputStatus = CreateValueLabel();
    private readonly Label _globalGestureStatus = CreateValueLabel();
    private readonly Label _foregroundStatus = CreateValueLabel();
    private readonly RichTextBox _log = new()
    {
        Dock = DockStyle.Fill,
        BackColor = Color.FromArgb(24, 26, 31),
        ForeColor = Color.Gainsboro,
        BorderStyle = BorderStyle.None,
        Font = new Font("Consolas", 10),
        ReadOnly = true,
        HideSelection = false,
    };
    private readonly CheckBox _claimGlobalGestures = new()
    {
        AutoSize = true,
        Text = "临时接管三/四/五指手势以检测（会在勾选且本窗口位于前台时暂停这些系统动作）",
    };
    private readonly bool _registerTouchpadApiPresent =
        NativeMethods.HasExport("RegisterTouchpadCapableWindow");
    private readonly bool _touchpadFrameApiPresent =
        NativeMethods.HasExport("GetPointerFrameTouchpadInfo");

    private TouchpadGesturesController? _globalController;
    private readonly HashSet<uint> _globalPointers = [];
    private readonly Dictionary<int, long> _touchpadFrameCounts = [];
    private readonly Dictionary<string, long> _globalActionCounts = [];
    private readonly Dictionary<string, long> _translatedInputCounts = [];
    private long _touchpadMessageCount;
    private int _lastTouchpadContacts;
    private DateTime _lastTouchpadAt;
    private DateTime _lastPenAt;
    private string _penState = "尚未看到手写笔事件";
    private bool _touchpadWindowRegistered;
    private bool _touchpadThreadRegistered;

    internal ProbeForm()
    {
        Text = "Precision Touchpad / Pen 只读探针";
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
        MinimumSize = new Size(900, 620);
        BackColor = Color.FromArgb(245, 246, 249);
        ForeColor = Color.FromArgb(26, 29, 36);
        Font = new Font("Microsoft YaHei UI", 10);
        KeyPreview = true;

        Controls.Add(BuildLayout());

        _claimGlobalGestures.CheckedChanged += (_, _) => ApplyGlobalGestureMode();
        KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape)
            {
                Close();
            }
        };

        Shown += (_, _) =>
        {
            InitializeGlobalGestureController();
            UpdateStatusLabels();
            AppendLog("探针已启动。先让笔悬停并移动，再进行两指动作。");
            AppendLog("需要检测三/四指点击时，再勾选临时接管；关闭窗口即恢复系统处理。");
        };

        Activated += (_, _) => UpdateStatusLabels();
        Deactivate += (_, _) => UpdateStatusLabels();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);

        try
        {
            _touchpadThreadRegistered = NativeMethods.RegisterTouchpadCapableThread(true);
            _touchpadWindowRegistered = NativeMethods.RegisterTouchpadCapableWindow(Handle, true);
            if (!_touchpadThreadRegistered)
            {
                AppendLog($"RegisterTouchpadCapableThread 失败，Win32 错误 {Marshal.GetLastWin32Error()}。");
            }

            if (!_touchpadWindowRegistered)
            {
                AppendLog($"RegisterTouchpadCapableWindow 失败，Win32 错误 {Marshal.GetLastWin32Error()}。");
            }
        }
        catch (EntryPointNotFoundException)
        {
            _touchpadWindowRegistered = false;
            AppendLog("当前系统没有 RegisterTouchpadCapableWindow。");
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (_touchpadWindowRegistered)
        {
            _ = NativeMethods.RegisterTouchpadCapableWindow(Handle, false);
            _touchpadWindowRegistered = false;
        }

        if (_touchpadThreadRegistered)
        {
            _ = NativeMethods.RegisterTouchpadCapableThread(false);
            _touchpadThreadRegistered = false;
        }

        if (_globalController is not null)
        {
            _globalController.Enabled = false;
        }

        base.OnHandleDestroyed(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (IsPointerMessage(message.Msg))
        {
            InspectPointerMessage(message.Msg, NativeMethods.GetPointerId(message.WParam));
        }
        else if (IsTranslatedInputMessage(message.Msg))
        {
            InspectTranslatedInput(message);
        }

        base.WndProc(ref message);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            ColumnCount = 1,
            RowCount = 6,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        Label title = new()
        {
            AutoSize = true,
            Font = new Font(Font.FontFamily, 20, FontStyle.Bold),
            Text = "Precision Touchpad / Pen 事件探针",
            Margin = new Padding(0, 0, 0, 8),
        };
        root.Controls.Add(title, 0, 0);

        Label explanation = new()
        {
            AutoSize = true,
            MaximumSize = new Size(1200, 0),
            Text = "默认模式只观察本窗口已经收到的输入，不注入、不重定向、不修改笔语义。"
                + " 请让笔光标停留在本窗口内：先在悬停中做两指滚动/缩放，再按需开启三指以上的临时检测。",
            Margin = new Padding(0, 0, 0, 16),
        };
        root.Controls.Add(explanation, 0, 1);

        TableLayoutPanel status = new()
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 5,
            Margin = new Padding(0, 0, 0, 12),
        };
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        status.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddStatusRow(status, 0, "系统 API", _apiStatus);
        AddStatusRow(status, 1, "窗口前台状态", _foregroundStatus);
        AddStatusRow(status, 2, "手写笔状态", _penStatus);
        AddStatusRow(status, 3, "两指原始帧", _twoFingerStatus);
        AddStatusRow(status, 4, "退化/转换消息", _translatedInputStatus);
        AddStatusRow(status, 5, "三指以上手势", _globalGestureStatus);
        root.Controls.Add(status, 0, 2);

        FlowLayoutPanel actions = new()
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            Margin = new Padding(0, 0, 0, 12),
        };
        actions.Controls.Add(_claimGlobalGestures);
        actions.Controls.Add(CreateButton("复制诊断摘要", (_, _) => CopySummary()));
        actions.Controls.Add(CreateButton("清空计数", (_, _) => ResetCounters()));
        actions.Controls.Add(CreateButton("关闭 (Esc)", (_, _) => Close()));
        root.Controls.Add(actions, 0, 3);

        GroupBox logGroup = new()
        {
            Dock = DockStyle.Fill,
            Text = "实时事件",
            Padding = new Padding(10),
        };
        logGroup.Controls.Add(_log);
        root.Controls.Add(logGroup, 0, 4);

        return root;
    }

    private static Label CreateValueLabel() => new()
    {
        AutoSize = true,
        Font = new Font("Microsoft YaHei UI", 10, FontStyle.Bold),
        Padding = new Padding(0, 3, 0, 3),
    };

    private static Button CreateButton(string text, EventHandler onClick)
    {
        Button button = new()
        {
            AutoSize = true,
            Text = text,
            Margin = new Padding(12, 0, 0, 0),
        };
        button.Click += onClick;
        return button;
    }

    private static void AddStatusRow(
        TableLayoutPanel panel,
        int row,
        string caption,
        Label value)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            Text = caption,
            Padding = new Padding(0, 3, 0, 3),
        }, 0, row);
        panel.Controls.Add(value, 1, row);
    }

    private void InitializeGlobalGestureController()
    {
        try
        {
            if (!TouchpadGesturesController.IsSupported())
            {
                AppendLog("TouchpadGesturesController.IsSupported() 返回 false。");
                return;
            }

            _globalController = TouchpadGesturesController.CreateForProcess();
            _globalController.SupportedGestures =
                TouchpadGlobalGestureKinds.ThreeFingerManipulations
                | TouchpadGlobalGestureKinds.FourFingerManipulations
                | TouchpadGlobalGestureKinds.FiveFingerManipulations
                | TouchpadGlobalGestureKinds.ThreeFingerActions
                | TouchpadGlobalGestureKinds.FourFingerActions
                | TouchpadGlobalGestureKinds.FiveFingerActions;

            _globalController.GlobalActionPerformed += (_, args) =>
            {
                string action = args.Action.ToString();
                Increment(_globalActionCounts, action);
                AppendLog($"全局动作: {action}；笔状态: {_penState}");
                UpdateStatusLabels();
            };
            _globalController.PointerPressed += (_, args) =>
            {
                uint pointerId = args.CurrentPoint.PointerId;
                _globalPointers.Add(pointerId);
                AppendLog($"三指以上移动流: PointerPressed #{pointerId}，当前 {_globalPointers.Count} 个触点；笔状态: {_penState}");
                UpdateStatusLabels();
            };
            _globalController.PointerMoved += (_, args) =>
            {
                _globalPointers.Add(args.CurrentPoint.PointerId);
                UpdateStatusLabels();
            };
            _globalController.PointerReleased += (_, args) =>
            {
                uint pointerId = args.CurrentPoint.PointerId;
                _globalPointers.Remove(pointerId);
                AppendLog($"三指以上移动流: PointerReleased #{pointerId}，剩余 {_globalPointers.Count} 个触点；笔状态: {_penState}");
                UpdateStatusLabels();
            };
            _globalController.Enabled = false;
        }
        catch (Exception exception)
        {
            AppendLog($"TouchpadGesturesController 初始化失败: {exception.GetType().Name}: {exception.Message}");
            _globalController = null;
        }
    }

    private void ApplyGlobalGestureMode()
    {
        if (_globalController is null)
        {
            if (_claimGlobalGestures.Checked)
            {
                _claimGlobalGestures.Checked = false;
                MessageBox.Show(
                    this,
                    "当前系统或运行时没有提供 TouchpadGesturesController，无法检测三指以上的原始手势。",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            return;
        }

        _globalController.Enabled = _claimGlobalGestures.Checked;
        _globalPointers.Clear();
        AppendLog(_globalController.Enabled
            ? "已临时接管三/四/五指手势。系统原动作只在本窗口前台且该选项勾选时暂停。"
            : "已停止接管三/四/五指手势，系统原动作恢复。");
        UpdateStatusLabels();
    }

    private void InspectPointerMessage(int message, uint pointerId)
    {
        if (!NativeMethods.GetPointerInfo(pointerId, out PointerInfo pointerInfo))
        {
            return;
        }

        switch (pointerInfo.PointerType)
        {
            case PointerInputType.Pen:
                InspectPen(pointerId, message, pointerInfo);
                break;
            case PointerInputType.Touchpad:
                InspectTouchpad(pointerId, message);
                break;
        }
    }

    private void InspectPen(uint pointerId, int message, PointerInfo pointerInfo)
    {
        _lastPenAt = DateTime.Now;
        PointerFlags flags = pointerInfo.PointerFlags;
        string newState =
            flags.HasFlag(PointerFlags.InContact) ? "接触数位板"
            : flags.HasFlag(PointerFlags.InRange) ? "悬停 / 在数位板感应范围内"
            : "离开数位板感应范围";

        if (message == NativeMethods.WM_POINTERLEAVE
            && !flags.HasFlag(PointerFlags.InRange))
        {
            newState = "离开数位板感应范围";
        }

        uint pressure = 0;
        if (NativeMethods.GetPointerPenInfo(pointerId, out PointerPenInfo penInfo))
        {
            pressure = penInfo.Pressure;
        }

        if (!string.Equals(newState, _penState, StringComparison.Ordinal)
            || message is NativeMethods.WM_POINTERDOWN or NativeMethods.WM_POINTERUP)
        {
            _penState = newState;
            AppendLog($"笔: {_penState}；pressure={pressure}；flags={flags}");
        }
        else
        {
            _penState = newState;
        }

        UpdateStatusLabels();
    }

    private void InspectTouchpad(uint pointerId, int message)
    {
        PointerTouchInfo[] frame = NativeMethods.ReadTouchpadFrame(pointerId, out int error);
        if (frame.Length == 0)
        {
            if (error != 0)
            {
                AppendLog($"读取触控板帧失败: Win32 错误 {error}");
            }

            return;
        }

        _touchpadMessageCount++;
        _lastTouchpadAt = DateTime.Now;
        _lastTouchpadContacts = frame.Length;
        Increment(_touchpadFrameCounts, frame.Length);

        if (message is NativeMethods.WM_POINTERDOWN
            or NativeMethods.WM_POINTERUP
            or NativeMethods.WM_POINTERENTER
            or NativeMethods.WM_POINTERLEAVE
            || _touchpadMessageCount % 20 == 0)
        {
            AppendLog(
                $"触控板帧: {frame.Length} 指；{PointerMessageName(message)}；"
                + $"累计 {_touchpadMessageCount} 帧；笔状态: {_penState}");
        }

        UpdateStatusLabels();
    }

    private void InspectTranslatedInput(Message message)
    {
        string name = TranslatedMessageName(message.Msg);
        InputMessageSource source = default;
        bool hasSource = NativeMethods.GetCurrentInputMessageSource(out source);
        string sourceText = hasSource
            ? $"{source.DeviceType}/{source.OriginId}"
            : $"unknown/error={Marshal.GetLastWin32Error()}";

        string details = message.Msg switch
        {
            NativeMethods.WM_MOUSEWHEEL or NativeMethods.WM_MOUSEHWHEEL =>
                $"delta={unchecked((short)(((long)message.WParam >> 16) & 0xFFFF))}",
            NativeMethods.WM_KEYDOWN
                or NativeMethods.WM_KEYUP
                or NativeMethods.WM_SYSKEYDOWN
                or NativeMethods.WM_SYSKEYUP =>
                $"vk=0x{unchecked((long)message.WParam):X}",
            NativeMethods.WM_APPCOMMAND =>
                $"command={unchecked(((long)message.LParam >> 16) & 0x7FF)}",
            _ => string.Empty,
        };

        string key = $"{name} [{source.DeviceType}]";
        Increment(_translatedInputCounts, key);
        AppendLog(
            $"转换消息: {name}；{details}；source={sourceText}；笔状态: {_penState}");
        UpdateStatusLabels();
    }

    private void UpdateStatusLabels()
    {
        string globalApi = _globalController is null ? "不可用" : "可用";
        _apiStatus.Text =
            $"窗口注册={_registerTouchpadApiPresent}; 帧读取={_touchpadFrameApiPresent}; 三指以上控制器={globalApi}";
        _apiStatus.ForeColor =
            _registerTouchpadApiPresent && _touchpadFrameApiPresent
                ? Color.DarkGreen
                : Color.DarkRed;

        _foregroundStatus.Text = ContainsFocus
            ? "本窗口在前台"
            : "本窗口不在前台（系统不会把三指以上手势交给探针）";
        _foregroundStatus.ForeColor = ContainsFocus ? Color.DarkGreen : Color.DarkOrange;

        string penAge = _lastPenAt == default
            ? string.Empty
            : $"；最后事件 {FormatAge(_lastPenAt)}";
        _penStatus.Text = _penState + penAge;
        _penStatus.ForeColor = _penState.StartsWith("悬停", StringComparison.Ordinal)
            || _penState.StartsWith("接触", StringComparison.Ordinal)
                ? Color.DarkBlue
                : Color.DimGray;

        string touchpadAge = _lastTouchpadAt == default
            ? "尚未收到"
            : $"最近 {_lastTouchpadContacts} 指，{FormatAge(_lastTouchpadAt)}；累计 {_touchpadMessageCount} 帧";
        _twoFingerStatus.Text =
            $"{touchpadAge}；按触点数: {FormatCounts(_touchpadFrameCounts)}";
        _twoFingerStatus.ForeColor = _touchpadMessageCount > 0
            ? Color.DarkGreen
            : Color.DimGray;

        _translatedInputStatus.Text = FormatCounts(_translatedInputCounts);
        _translatedInputStatus.ForeColor = _translatedInputCounts.Count > 0
            ? Color.DarkGreen
            : Color.DimGray;

        string controllerState =
            _globalController is null ? "API 不可用"
            : _globalController.Enabled ? "正在临时检测"
            : "未接管（默认）";
        _globalGestureStatus.Text =
            $"{controllerState}；当前移动触点 {_globalPointers.Count}；动作: {FormatCounts(_globalActionCounts)}";
        _globalGestureStatus.ForeColor = _globalController?.Enabled == true
            ? Color.DarkOrange
            : Color.DimGray;
    }

    private void CopySummary()
    {
        try
        {
            Clipboard.SetText(BuildSummary());
            AppendLog("诊断摘要已复制到剪贴板。");
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "复制失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private string BuildSummary()
    {
        StringBuilder summary = new();
        summary.AppendLine("Precision Touchpad / Pen Probe");
        summary.AppendLine($"Time: {DateTime.Now:O}");
        summary.AppendLine($"OS: {Environment.OSVersion}");
        summary.AppendLine($"Touchpad window registered: {_touchpadWindowRegistered}");
        summary.AppendLine($"Touchpad thread registered: {_touchpadThreadRegistered}");
        summary.AppendLine($"Global gesture API available: {_globalController is not null}");
        summary.AppendLine($"Global gesture controller enabled: {_globalController?.Enabled == true}");
        summary.AppendLine($"Probe foreground: {ContainsFocus}");
        summary.AppendLine($"Pen state: {_penState}");
        summary.AppendLine($"Touchpad messages: {_touchpadMessageCount}");
        summary.AppendLine($"Touchpad contact counts: {FormatCounts(_touchpadFrameCounts)}");
        summary.AppendLine($"Translated input messages: {FormatCounts(_translatedInputCounts)}");
        summary.AppendLine($"Global gesture actions: {FormatCounts(_globalActionCounts)}");
        summary.AppendLine();
        summary.Append(_log.Text);
        return summary.ToString();
    }

    private void ResetCounters()
    {
        _touchpadFrameCounts.Clear();
        _globalActionCounts.Clear();
        _translatedInputCounts.Clear();
        _globalPointers.Clear();
        _touchpadMessageCount = 0;
        _lastTouchpadContacts = 0;
        _lastTouchpadAt = default;
        _lastPenAt = default;
        _penState = "清空后尚未看到手写笔事件";
        _log.Clear();
        AppendLog("计数已清空。");
        UpdateStatusLabels();
    }

    private void AppendLog(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        _log.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}{Environment.NewLine}");
        if (_log.TextLength > 100_000)
        {
            _log.Select(0, 20_000);
            _log.SelectedText = string.Empty;
        }

        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static void Increment<TKey>(Dictionary<TKey, long> counts, TKey key)
        where TKey : notnull
    {
        counts.TryGetValue(key, out long count);
        counts[key] = count + 1;
    }

    private static string FormatCounts<TKey>(Dictionary<TKey, long> counts)
        where TKey : notnull =>
        counts.Count == 0
            ? "无"
            : string.Join(", ", counts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));

    private static string FormatAge(DateTime timestamp)
    {
        TimeSpan age = DateTime.Now - timestamp;
        return age.TotalSeconds < 1
            ? $"{age.TotalMilliseconds:0} ms 前"
            : $"{age.TotalSeconds:0.0} s 前";
    }

    private static bool IsPointerMessage(int message) =>
        message is NativeMethods.WM_POINTERUPDATE
            or NativeMethods.WM_POINTERDOWN
            or NativeMethods.WM_POINTERUP
            or NativeMethods.WM_POINTERENTER
            or NativeMethods.WM_POINTERLEAVE
            or NativeMethods.WM_POINTERCAPTURECHANGED;

    private static bool IsTranslatedInputMessage(int message) =>
        message is NativeMethods.WM_MOUSEWHEEL
            or NativeMethods.WM_MOUSEHWHEEL
            or NativeMethods.WM_GESTURE
            or NativeMethods.WM_GESTURENOTIFY
            or NativeMethods.WM_APPCOMMAND
            or NativeMethods.WM_KEYDOWN
            or NativeMethods.WM_KEYUP
            or NativeMethods.WM_SYSKEYDOWN
            or NativeMethods.WM_SYSKEYUP;

    private static string TranslatedMessageName(int message) => message switch
    {
        NativeMethods.WM_MOUSEWHEEL => "MouseWheel",
        NativeMethods.WM_MOUSEHWHEEL => "MouseHWheel",
        NativeMethods.WM_GESTURE => "Gesture",
        NativeMethods.WM_GESTURENOTIFY => "GestureNotify",
        NativeMethods.WM_APPCOMMAND => "AppCommand",
        NativeMethods.WM_KEYDOWN => "KeyDown",
        NativeMethods.WM_KEYUP => "KeyUp",
        NativeMethods.WM_SYSKEYDOWN => "SysKeyDown",
        NativeMethods.WM_SYSKEYUP => "SysKeyUp",
        _ => $"0x{message:X}",
    };

    private static string PointerMessageName(int message) => message switch
    {
        NativeMethods.WM_POINTERUPDATE => "Update",
        NativeMethods.WM_POINTERDOWN => "Down",
        NativeMethods.WM_POINTERUP => "Up",
        NativeMethods.WM_POINTERENTER => "Enter",
        NativeMethods.WM_POINTERLEAVE => "Leave",
        NativeMethods.WM_POINTERCAPTURECHANGED => "CaptureChanged",
        _ => $"0x{message:X}",
    };
}
