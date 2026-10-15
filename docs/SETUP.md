# 安装与配置 / Installation and setup

[中文首页](../README.zh-CN.md) · [English overview](../README.md)

## 1. 先让笔正常工作 / Set up pen input first

先安装并验证 OpenTabletDriver，再添加本插件。请按设备型号查看
[OTD 兼容性列表](https://opentabletdriver.net/Tablets)和
[Windows 安装指南](https://opentabletdriver.net/Wiki/Install/Windows)。
截至 2026-10-15，官方 Windows 指南要求 .NET 8 Desktop Runtime x64；
这与本插件源码的 net6.0 编译目标是不同层面的依赖。

Install and verify OpenTabletDriver before adding this plugin. Follow its
Windows guide for runtime and device-specific requirements; don't assume
every tablet requires the same driver steps.

在 OneNote 中书写，作者使用 **Windows Ink Absolute Mode**。请参考
[WindowsInk 上游安装说明](https://github.com/X9VoiD/VoiDPlugins/wiki/WindowsInk)
安装 WindowsInk 插件及其所需的 VMulti 驱动，并配置 Pen Tip 绑定。
先确认普通笔输入、压感和笔键正常，再继续。

For the author's OneNote workflow, configure **Windows Ink Absolute Mode**,
its VMulti prerequisite, and the Pen Tip binding following the upstream guide.
Verify writing, pressure, and pen buttons before adding precision mapping.
This package does not install WindowsInk, VMulti, OTD, or .NET.

## 2. 安装 Precision Control / Install the plugin

截至 2026-10-15，公开 Release 为 1.0.0；双旋钮需要 **1.1.2**。
新版发布前，请从[源码构建](DEVELOPMENT.md)或使用维护者提供的对应版本 ZIP。

The public release checked on 2026-10-15 is 1.0.0. Dual-dial movement needs
1.1.2; build it or use a matching ZIP supplied by the maintainer.

1. 退出 OTD 界面和后台进程 / Exit the OTD UI and daemon.
2. 备份 / Back up `%LOCALAPPDATA%\OpenTabletDriver\settings.json` and your existing Precision Control plugin directory.
3. 从 ZIP 中的 `plugin` 文件夹复制 `PrecisionControl.dll` 和 `metadata.json`，放入已有的 Precision Control 插件目录。首次安装可使用 / Copy those two files into the existing plugin directory, or for a first install use:
   `%LOCALAPPDATA%\OpenTabletDriver\Plugins\Precision Control (TingAlex Enhanced)`.
4. 不要同时保留两份该插件 DLL，也不要复制 OTD 自己的依赖 DLL / Do not keep duplicate plugin copies or copy OTD's own dependency DLLs.
5. 重启 OTD，在 Filters 添加并启用 **Precision Control (TingAlex Enhanced)**，保存并应用 / Restart OTD, add and enable the enhanced filter, then save and apply.

如果你的 OTD 使用便携或自定义数据目录，请使用实际的插件目录。
For portable/custom OTD data directories, use their actual plugin location.

## 3. 推荐起始设置 / Suggested starting settings

以下是工作流建议，不会自动覆盖你现有配置。尤其要手动选择
**Pointer Anchored**：1.1.2 为兼容旧行为，代码默认仍是 Legacy。

These are starting suggestions, not an imported full-device profile.
Explicitly choose **Pointer Anchored**: 1.1.2 still defaults to Legacy for
backward compatibility.

| 界面字段 / UI field | 建议 / Suggestion | 用途 / Meaning |
| --- | --- | --- |
| 01 映射区域 · 缩放比例 | 0.25 | Width and height are each about one quarter of the display; smaller values give finer movement |
| 02 双旋钮 · 水平步长 | 40 px | Horizontal distance per dial tick |
| 02 双旋钮 · 竖直步长 | 40 px | Vertical distance; adjust to your handwriting line spacing |
| 03 边框外观 · 显示边框 | On | Visible outline only; does not dim the page |
| 03 边框外观 · 颜色 | #000000 | Black |
| 03 边框外观 · 不透明度 | 0.4 | 40% opacity |
| 03 边框外观 · 线宽 | 2 | Border thickness in pixels |
| 06 进阶定位 · 映射方式 | Pointer Anchored | Map the tablet/output coordinates proportionally into the frame |
| 06 进阶定位 · 初始位置来源 | Last Active Pointer | Use whichever moved most recently: pen or mouse |
| 06 进阶定位 · 锚点横向 / 竖向位置 | 10% / 10% | Place the starting pointer near the frame's top-left; 50%/50% centers it |

修改后保存、应用，并重新启用映射区。步长是屏幕像素，不是毫米或文档行数；
更换屏幕缩放、OneNote 缩放或字大小后，可能需要重新调整。

Save/apply changes and reactivate the area. Steps are screen pixels, not
millimeters or document lines. Retune after changing display or app zoom.

## 4. 双旋钮绑定 / Bind the two dials

在 Bindings 的两个旋钮设置中，给四个旋转方向选择
**Precision Control Move Area (Dial)**，然后设置 `Direction`：

| 旋钮 / Dial | 顺时针 / Clockwise | 逆时针 / Counterclockwise |
| --- | --- | --- |
| 1：水平 / horizontal | Right | Left |
| 2：竖直 / vertical | Down | Up |

Wacom PTK-670 每圈 24 格，可使用 15° 触发阈值。其他型号请按设备报告方式配置，
不要直接套用 PTK-670 的阈值。若方向不合习惯，交换对应旋钮的两个 Direction。

PTK-670 has 24 detents per turn, corresponding to 15-degree thresholds.
Use device-appropriate thresholds for other models. Swap directions to reverse a dial.

## 5. 实际验证 / Try the workflow

1. 在 OneNote 中先确认能正常写字 / Verify ordinary writing.
2. 按 Ctrl+Alt+Shift+P，确认框出现，笔映射变小 / Toggle the area.
3. 抬起笔尖，分别转动两个旋钮，确认页面不动、框按对应方向移动 / Lift the tip and test both dials.
4. 写一行、抬笔、下移框，把手移回原来的舒适位置继续 / Write, shift the area, and reuse your preferred hand position.
5. 隐藏映射区再旋转，确认没有框被创建；重新启用也不会补执行 / Verify inactive ticks are ignored.
6. 试用触控板移动页面。若笔悬停时手势失效，先抬离感应区 / Test touchpad gestures, with the pen out of range if necessary.

## 快捷键与升级说明 / Shortcuts and upgrade notes

- `Ctrl+Alt+Shift+P`：显示/隐藏 / Show or hide mapping.
- `Ctrl+Alt+Shift+R`：在鼠标处显示或重建 / Show or reposition at the current mouse location.
- 以上主键和公共修饰键可在设置页修改 / Main keys and shared modifiers are configurable.
- 1.1.2 隐藏了按键平移的配置项，但仍启用“公共修饰键 + 方向键”，每次移动框宽/高的 20%。
  旧版自定义按键、步长和禁用状态保留在配置文件中，但此版本不会加载这些隐藏字段。
  Keyboard nudges remain active with built-in arrow keys and 20% steps.
  Previously saved custom nudge keys, distances, or disabled state are not
  loaded while these fields are hidden. This is a known migration limitation.

## 常见问题 / Troubleshooting

| 问题 / Symptom | 检查 / Check |
| --- | --- |
| 旋钮没反应 / Dials do nothing | Enable the filter and area; lift the tip; verify both wheel bindings and thresholds |
| 笔能动但不能写 / Pointer moves, no ink | Check WindowsInk output mode, VMulti, Pen Tip binding, and app ink mode |
| 看不到框 / No frame | Enable 显示边框; check opacity; reposition with Ctrl+Alt+Shift+R |
| 框跑出屏幕 / Area goes off-screen | This version permits it; use reposition to bring it back |
| 触控板在悬停时失效 / Touchpad suppressed during hover | Lift the pen out of range; the plugin cannot guarantee a workaround |
| 快捷键没反应 / Shortcut fails | Check OTD logs for key conflicts; choose different shared modifiers/main keys |
| 插件不出现 / Plugin missing | Verify version and folder, remove duplicate DLL copies, restart both UI and daemon, inspect logs |

停用或卸载：先从 Filters 移除本滤镜，并清除旋钮上的本插件绑定，保存配置。
退出界面和后台后，移除对应插件文件夹。回退版本时，可恢复已备份的 DLL 和配置。

To uninstall, remove the filter and its dial bindings, save, then exit OTD
and remove the plugin folder. To roll back, restore the backed-up plugin and
configuration with OTD stopped.
