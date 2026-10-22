using OpenTabletDriver.Plugin.Attributes;

namespace VoiDPlugins.Filter
{
    // Keep all visible settings together and in group order: OTD's generated
    // editor enumerates reflected properties without sorting their labels.
    public partial class PrecisionControl
    {
        [ToolTip("小范围映射宽、高相对原输出范围的倍率：0.25 表示宽高各为 25%。越小笔移动越精细，不缩放文档页面。默认 0.3；建议从 0.25 开始，修改后重新启用映射。")]
        [SliderProperty("01 书写范围 · 缩放比例", 0.0f, 10f, 0.3f), DefaultPropertyValue(0.3f)]
        public float Scale { get; set; }

        [ToolTip("推荐 Pointer Anchored：将原输出坐标按比例映射到整个框内，适合旋钮或贴板拖框。Screen Relative (Legacy) 保留旧相对位移算法，代码默认选它以兼容旧配置。修改后重新启用映射。")]
        [Property("01 书写范围 · 坐标映射方式"), PropertyValidated(nameof(ValidPositioningModes))]
        [DefaultPropertyValue(ScreenRelativeMode)]
        public string? PositioningMode { get; set; } = ScreenRelativeMode;

        [ToolTip("只决定普通启用映射时参考哪一个指针：Last Active Pointer 为最近活动的笔/鼠标；Last Pen Position 为上次笔位置；Current Mouse Position 为当前鼠标。鼠标重定位快捷键始终参考鼠标。")]
        [Property("02 初始位置 · 参考指针来源"), PropertyValidated(nameof(ValidActivationAnchorSources))]
        [DefaultPropertyValue(LastActivePointerAnchor)]
        public string? ActivationAnchorSource { get; set; } = LastActivePointerAnchor;

        [ToolTip("仅 Pointer Anchored 使用：参考指针应位于框内的横向比例，0% 左边、50% 中间、100% 右边。默认 10%；不移动鼠标，不改变框大小。修改后重新启用映射。")]
        [SliderProperty("02 初始位置 · 指针在框内的横向位置（%）", 0f, 100f, 10f), DefaultPropertyValue(10f)]
        public float PointerPositionXPercent { get; set; } = 10f;

        [ToolTip("仅 Pointer Anchored 使用：参考指针应位于框内的竖向比例，0% 顶边、50% 中间、100% 底边。默认 10%；用于在参考指针附近放框。修改后重新启用映射。")]
        [SliderProperty("02 初始位置 · 指针在框内的竖向位置（%）", 0f, 100f, 10f), DefaultPropertyValue(10f)]
        public float PointerPositionYPercent { get; set; } = 10f;

        [ToolTip("水平旋钮每次触发时移动映射框的屏幕像素数，范围 1–1000，默认 40。需另绑定 Move Area (Dial) 的 Left/Right；映射未启用或正在书写时忽略，不滚动页面。")]
        [SliderProperty("03 旋钮移动 · 水平步长（像素/格）", 1f, 1000f, 40f), DefaultPropertyValue(40f)]
        public float HorizontalDialStep { get; set; } = 40f;

        [ToolTip("竖直旋钮每次触发时移动映射框的屏幕像素数，范围 1–1000，默认 40。需另绑定 Up/Down；抬笔或离开感应区后可操作，不改变框大小或文档位置。")]
        [SliderProperty("03 旋钮移动 · 竖直步长（像素/格）", 1f, 1000f, 40f), DefaultPropertyValue(40f)]
        public float VerticalDialStep { get; set; } = 40f;

        [ToolTip("悬空短按第三笔键并松开，切换小范围/原输出映射。关闭小范围也隐藏边框；不滚动页面。第三键普通绑定须留空，书写中按键不截断已有笔画。默认关闭。")]
        [BooleanProperty("04 笔键操作 · 短按切换映射", "第三键短按启用/退出小范围映射")]
        [DefaultPropertyValue(false)]
        public bool EnablePenNavigation { get; set; }

        [ToolTip("短按切换开启时，按下到松开的时间必须小于此值。范围 150–800 毫秒，默认 300；超过后松键不切换。只决定按钮识别，不增加短按执行等待。")]
        [SliderProperty("04 笔键操作 · 短按时长上限（毫秒）", 150f, 800f, 300f), DefaultPropertyValue(300f)]
        public float PenNavigationHoldMilliseconds { get; set; } = 300f;

        [ToolTip("小范围映射启用后，按住第三笔键并贴板滑动，只移动映射框、不拖动页面。抬笔可换位；松键后先抬笔再恢复书写。第三键普通绑定须留空，默认关闭。")]
        [BooleanProperty("04 笔键操作 · 按住贴板拖框", "第三键按住时，用笔尖移动映射框")]
        [DefaultPropertyValue(false)]
        public bool EnablePenDrag { get; set; }

        [ToolTip("只影响按住第三键时拖框的距离。范围 0.1–5，默认 1；按原输出坐标计算倍率，调低可更精细。不会改变书写缩放比例或旋钮步长。")]
        [SliderProperty("04 笔键操作 · 拖框移动倍率", 0.1f, 5f, 1f), DefaultPropertyValue(1f)]
        public float PenDragSensitivity { get; set; } = 1f;

        [ToolTip("只控制轮廓是否可见，取消勾选仍保留小范围笔映射。若要退出小范围，请用映射开关或第三键短按。边框可穿透点击，默认开启。")]
        [BooleanProperty("05 边框外观 · 显示轮廓", "显示当前小范围映射的边框")]
        [DefaultPropertyValue(true)]
        public bool ShowBorder { get; set; } = true;

        [ToolTip("输入 #RRGGBB，例如 #000000 黑色、#FFFFFF 白色。默认黑色。")]
        [Property("05 边框外观 · 颜色（#RRGGBB）"), DefaultPropertyValue(BorderColorParser.DefaultColor)]
        public string? BorderColor { get; set; } = BorderColorParser.DefaultColor;

        [ToolTip("范围 0.05–1；0.4 表示 40% 不透明，数值越大越明显。")]
        [SliderProperty("05 边框外观 · 不透明度", 0.05f, 1.0f, 0.4f), DefaultPropertyValue(0.4f)]
        public float BorderOpacity { get; set; } = 0.4f;

        [ToolTip("边框线条的粗细，建议 1–6 像素；默认 2。")]
        [SliderProperty("05 边框外观 · 线宽（像素）", 1.0f, 6.0f, 2.0f), DefaultPropertyValue(2.0f)]
        public float BorderThickness { get; set; } = 2.0f;

        [ToolTip("启用下方主键与第 07 组修饰键的组合。默认 Ctrl+Alt+Shift+P，切换小范围/原输出映射及边框，不只是隐藏轮廓；建议抬笔操作。")]
        [BooleanProperty("06 键盘快捷键 · 映射开关", "启用小范围映射切换快捷键")]
        [DefaultPropertyValue(true)]
        public bool EnableGlobalHotkey { get; set; } = true;

        [ToolTip("映射开关的主键，默认 P。与第 07 组勾选的修饰键一起按下；关闭上方开关后此组合不注册。")]
        [Property("06 键盘快捷键 · 映射开关主键"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("P")]
        public string? GlobalHotkeyKey { get; set; } = "P";

        [ToolTip("默认 Ctrl+Alt+Shift+R：在当前鼠标处启用或重新放置映射框，位置按第 02 组锚点百分比计算。书写或拖框期间不执行；与初始位置来源选择无关。")]
        [BooleanProperty("06 键盘快捷键 · 鼠标重定位", "启用在当前鼠标处放置映射框的快捷键")]
        [DefaultPropertyValue(true)]
        public bool EnableRepositionHotkey { get; set; } = true;

        [ToolTip("默认 R。与公共修饰键一起按下，将映射框定位到鼠标处。")]
        [Property("06 键盘快捷键 · 鼠标重定位主键"), PropertyValidated(nameof(ValidGlobalHotkeyKeys))]
        [DefaultPropertyValue("R")]
        public string? RepositionHotkeyKey { get; set; } = "R";

        [ToolTip("映射开关、鼠标重定位和方向键平移共用这组修饰键。勾选 Ctrl 后须同时按住它；默认 Ctrl+Alt+Shift。")]
        [BooleanProperty("07 组合修饰键 · Ctrl", "快捷键组合包含 Ctrl"), DefaultPropertyValue(true)]
        public bool HotkeyCtrl { get; set; } = true;

        [ToolTip("勾选后，触发上述快捷键时还必须按住 Alt。")]
        [BooleanProperty("07 组合修饰键 · Alt", "快捷键组合包含 Alt"), DefaultPropertyValue(true)]
        public bool HotkeyAlt { get; set; } = true;

        [ToolTip("勾选后，触发上述快捷键时还必须按住 Shift。")]
        [BooleanProperty("07 组合修饰键 · Shift", "快捷键组合包含 Shift"), DefaultPropertyValue(true)]
        public bool HotkeyShift { get; set; } = true;

        [ToolTip("勾选后，触发上述快捷键时还必须按住 Windows 键；默认不勾选。")]
        [BooleanProperty("07 组合修饰键 · Windows", "快捷键组合包含 Windows"), DefaultPropertyValue(false)]
        public bool HotkeyWindows { get; set; }
    }
}
