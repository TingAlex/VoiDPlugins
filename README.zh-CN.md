# Precision Control — TingAlex 增强版

**移动书写区，让页面留在原地。**

[English](README.md) · [安装](docs/SETUP.md) · [设置说明](docs/SETTINGS.md) · [版本记录](CHANGELOG.md) · [构建](docs/DEVELOPMENT.md)

这是面向 Windows 文档手写的 OpenTabletDriver 插件：把数位板映射到屏幕上的小范围，再用旋钮、第三笔键或快捷键移动这个范围。页面内容保持原位，下一笔写进新的屏幕区域。

工作流来自 Wacom PTK-670 + OneNote 的实际使用：写完一行，抬笔，把映射框向下移，再让手回到习惯的位置继续写。需要浏览其他内容时，用触控板拖移文档。

## 当前版本与获取方式

源码版本 **1.6.0**，开发分支 [agent/global-hotkey-precision-control](https://github.com/TingAlex/VoiDPlugins/tree/agent/global-hotkey-precision-control)。

2026-10-22 核对：GitHub 的 Latest 下载仍为 **v1.0.0**，不包含后续旋钮、笔键和设置整理。新版源码与旧下载包有区别，请按[构建指南](docs/DEVELOPMENT.md)生成当前版本，或使用版本对应的维护者安装包。[发行页面](https://github.com/TingAlex/VoiDPlugins/releases)

## 三种操作分别改变什么

| 操作 | 结果 |
| --- | --- |
| 用触控板浏览文档 | 文档页面移动 |
| 转动已绑定旋钮，或按住第三笔键贴板拖框 | 映射框与实际笔映射一起移动，尺寸保持 |
| 快捷键或第三键短按切换映射 | 启用小范围，或退出并恢复原输出映射 |

边框只是实际映射的提示。单独取消“显示轮廓”仍保留小范围映射；它与退出映射是两件事。书写缩放比例改变笔到屏幕的对应范围，不改变 OneNote 页面缩放。

## 常用控制

| 控制 | 行为 |
| --- | --- |
| Ctrl + Alt + Shift + P | 启用/退出小范围映射 |
| Ctrl + Alt + Shift + R | 在当前鼠标处启用或重新定位映射 |
| 水平旋钮 Left / Right | 每次向左/右移动 40 屏幕像素，可调 |
| 竖直旋钮 Up / Down | 每次向上/下移动 40 屏幕像素，可调 |
| 第三笔键短按并松开 | 开启对应设置后切换映射 |
| 按住第三笔键，贴板滑动 | 开启对应设置后拖框；抬笔可换位继续 |

笔键操作默认关闭，旋钮需要在 Bindings 中另行绑定。第三笔键普通绑定应留空。正在书写时不执行旋钮移动，旧动作不排队到抬笔后补放。

## 设置按用途分成七组

| 组 | 用途 |
| --- | --- |
| 01 书写范围 | 缩放比例、笔坐标映射算法 |
| 02 初始位置 | 启用映射时参考哪个指针，指针处于框内什么位置 |
| 03 旋钮移动 | 水平/竖直旋钮的像素步长 |
| 04 笔键操作 | 短按开关、短按时长、按住贴板拖框和移动倍率 |
| 05 边框外观 | 轮廓、颜色、不透明度、线宽 |
| 06 键盘快捷键 | 映射开关、鼠标重定位及主键 |
| 07 组合修饰键 | 快捷键共用的 Ctrl / Alt / Shift / Windows |

每项都有悬停说明。[完整设置表](docs/SETTINGS.md)列出默认值、适用条件与参数之间的关系。

首次使用建议：Pointer Anchored、比例 0.25、旋钮步长 40、边框不透明度 0.4。为兼容旧配置，代码默认映射算法仍是 Screen Relative (Legacy)，需要主动选择 Pointer Anchored。

## 页面拖移实验的状态

项目已实现双击笔键切换页面拖移、贴板拖页、触点停止处理及可调防抖，并获得本机用户可用反馈。用户当前更倾向让触控板负责页面导航，因此 **1.6.0 默认关闭页面拖移，并从设置页隐藏所有相关选项**。

源码与回归测试保留；旧配置中的实验字段也不会重新启用它。普通短按映射开关与贴板拖框继续可用。[实验实现与开发入口](docs/EXPERIMENTAL-PAGE-PAN.md)

## 环境与验证范围

- 需要 OpenTabletDriver；本机使用 0.6.7。它是插件，不是独立数位板驱动。
- 当前实际使用设备为 Wacom PTK-670；其他型号需 OTD 支持相应报告、笔键或旋钮。
- 作者使用 OneNote 和 Windows Ink Absolute Mode。WindowsInk、VMulti 与笔尖绑定需按各自文档配置，安装包不捆绑这些依赖。
- Windows 边框与全局快捷键依赖 Win32。混合 DPI、多屏、其他应用及新机器完整安装仍需验证。
- 某些设备会在笔悬停时抑制触控板；先把笔移出感应区再用触控板，这是当前工作流接受的操作顺序。

当前 Precision Control 套件 **191 项测试**涵盖映射、笔键隔离、设置默认值与保留的页面实验。模拟测试不等于全部硬件或应用验证；舒适度描述来自作者使用反馈。

## 项目与反馈

[发布状态](docs/RELEASE-PLAN.zh-CN.md)区分源码更新、安装包和设备验证。2026-10-22 核对时本 fork 的 Issues 尚未开启；反馈材料可包含插件/OTD/Windows 版本、设备、输出模式、DPI 和复现步骤，并去除笔记内容等隐私。

基于 [X9VoiD/VoiDPlugins](https://github.com/X9VoiD/VoiDPlugins) 和 [OpenTabletDriver](https://opentabletdriver.net/)。仓库保留[其他上游模块](docs/OTHER-PLUGINS.md)，本说明与专用安装包聚焦 Precision Control。按 [GNU GPL v3](LICENSE) 分发。
