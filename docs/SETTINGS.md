# 设置参考 / Settings reference

适用版本：**1.6.0**。在 OTD 的 Filters 中选择 Precision Control (TingAlex Enhanced)。界面采用七组编号前缀与悬停解释；下表的 Property ID 是稳定配置键，重命名显示文字不更改旧配置含义。

## 01 书写范围 / Writing range

| 界面字段 | Property ID | 默认值 | 作用与生效时机 |
| --- | --- | --- | --- |
| 缩放比例 | Scale | 0.3 | 小范围宽/高相对原输出的倍率。0.25 表示宽高各 25%，不是文档缩放。为兼容旧配置保留 0–10 范围，通常从 0.25 开始；改后重新启用映射 |
| 坐标映射方式 | PositioningMode | Screen Relative (Legacy) | 推荐主动选择 Pointer Anchored，将原输出坐标映射进整个框；Legacy 保留旧相对位移算法。改后重新启用 |

如果只想改变接下来写的位置，移动映射框；如果要浏览文档，使用触控板。比例与框位置是两个不同参数。

## 02 初始位置 / Initial position

| 界面字段 | Property ID | 默认值 | 作用与条件 |
| --- | --- | --- | --- |
| 参考指针来源 | ActivationAnchorSource | Last Active Pointer | 启用映射时参考最近活动的笔/鼠标。Last Pen Position 用上次笔位置，Current Mouse Position 用当前鼠标。仅影响启用位置 |
| 指针在框内的横向位置（%） | PointerPositionXPercent | 10 | Pointer Anchored 专用：0 为左边，50 为中间，100 为右边 |
| 指针在框内的竖向位置（%） | PointerPositionYPercent | 10 | Pointer Anchored 专用：0 为顶边，50 为中间，100 为底边 |

百分比决定框如何围绕参考指针放置，不移动指针，也不改变框尺寸。鼠标重定位快捷键始终参考鼠标，不受“参考指针来源”的选择影响。改后重新启用映射。

## 03 旋钮移动 / Dial movement

| 界面字段 | Property ID | 默认值 | 作用与条件 |
| --- | --- | --- | --- |
| 水平步长（像素/格） | HorizontalDialStep | 40 | Left/Right 旋钮 Binding 每次触发移动的屏幕像素，1–1000 |
| 竖直步长（像素/格） | VerticalDialStep | 40 | Up/Down 的独立像素步长，1–1000 |

需要在 Bindings 中另行配置 Precision Control Move Area (Dial)。单改步长不会自动绑定硬件。映射关闭、书写中、笔键拖框捕获期间忽略移动，不在之后补放；抬笔或离开感应区时可操作。

## 04 笔键操作 / Pen-button mapping controls

| 界面字段 | Property ID | 默认值 | 作用与条件 |
| --- | --- | --- | --- |
| 短按切换映射 | EnablePenNavigation | 关闭 | 第三笔键悬空短按并松开，立即启用/退出小范围映射 |
| 短按时长上限（毫秒） | PenNavigationHoldMilliseconds | 300 | 150–800。按住到松开必须短于此值；超时不切换。它是识别上限，不是等待计时 |
| 按住贴板拖框 | EnablePenDrag | 关闭 | 小范围启用后按住第三键，用笔尖贴板滑动移动映射；抬笔可换位 |
| 拖框移动倍率 | PenDragSensitivity | 1 | 0.1–5，按原输出坐标计算拖框位移；和 Scale、旋钮步长分开 |

两个开关独立。第三笔键的普通 Binding 须留空，设备报告需实际含第三键。悬空晃动不否决短按；长按/接触/取消不触发短按。松键仍贴板时屏蔽接触尾部，松键并抬笔后恢复书写。已有笔画中才按键不会抢走当前笔画。

当前版本不通过第三键拖移文档。长按悬空无页面动作；连续两次短按按两次普通映射开关处理，没有双击等待。页面导航交给触控板。

## 05 边框外观 / Border appearance

| 界面字段 | Property ID | 默认值 | 作用 |
| --- | --- | --- | --- |
| 显示轮廓 | ShowBorder | 开启 | 显示实际小范围映射的边框。取消仅隐藏轮廓，映射继续生效 |
| 颜色（#RRGGBB） | BorderColor | #000000 | RGB 颜色，例如黑色 #000000、白色 #FFFFFF |
| 不透明度 | BorderOpacity | 0.4 | 0.05–1，0.4 为 40% 不透明，越大越明显 |
| 线宽（像素） | BorderThickness | 2 | 1–6，边框的屏幕像素宽度 |

边框可穿透点击、保持周围页面可见。外观设置在下一次显示/更新边框时应用；保存应用后重新启用映射可检查效果。

## 06 键盘快捷键 / Keyboard shortcuts

| 界面字段 | Property ID | 默认值 | 作用 |
| --- | --- | --- | --- |
| 映射开关 | EnableGlobalHotkey | 开启 | 注册小范围映射切换组合键 |
| 映射开关主键 | GlobalHotkeyKey | P | 与第 07 组修饰键组合，默认 Ctrl+Alt+Shift+P |
| 鼠标重定位 | EnableRepositionHotkey | 开启 | 在当前鼠标处启用或重新放置映射框 |
| 鼠标重定位主键 | RepositionHotkeyKey | R | 默认 Ctrl+Alt+Shift+R |

关闭键盘开关不影响已开启的笔键控制。当前 Toggle 在落笔时也允许，建议抬笔使用；重定位和方向平移在书写/拖框期间被拒绝。若组合键被占用，OTD 日志会提示注册冲突。

## 07 组合修饰键 / Shared modifiers

| 界面字段 | Property ID | 默认值 | 作用 |
| --- | --- | --- | --- |
| Ctrl | HotkeyCtrl | 开启 | 组合键要求同时按 Ctrl |
| Alt | HotkeyAlt | 开启 | 要求同时按 Alt |
| Shift | HotkeyShift | 开启 | 要求同时按 Shift |
| Windows | HotkeyWindows | 关闭 | 开启后还要求按 Windows |

这组修饰键共同用于映射开关、鼠标重定位和内置方向键微移。内置方向键每次移动框宽/高的 20%；微移的独立开关/按键/步长未显示，使用代码默认，旧隐藏自定义值不会加载。

## 已保留但未展示的页面实验

双击贴板拖页、悬空滚动、触点方向/增益、双击间隔及防抖均保留在源码和测试中。普通版本的内部页面开关为 false；这些字段没有 OTD Property 元数据，因此旧 JSON 也不能启用它们。[实验文档](EXPERIMENTAL-PAGE-PAN.md)说明实现、使用反馈和开发入口。
