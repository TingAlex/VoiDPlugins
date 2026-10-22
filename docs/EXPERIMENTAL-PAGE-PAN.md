# 保留的页面导航实验 / Retained page-navigation experiment

更新：2026-10-22；普通版本 **1.6.0 默认关闭，并隐藏相关设置**。当前推荐工作流仍由触控板导航文档，Precision Control 调整实际笔映射。

## 已实现什么

1.3.x 先实现长按悬空双轴滚轮导航；1.4.x 引入直接双触点页面平移；1.5.x 增加双击切换贴板拖页与空间防抖。

用户反馈悬空晃动、映射开启时路由问题、触点越界自动恢复墨迹和抬笔续滑后，实现逐步调整为：按映射后的笔坐标定位页面、在客户区内建立/限制触点、失败保留墨迹隔离、抬笔先静止再结束。用户对后续版本反馈“目前确实是可用了”，并确认新增功能可用，随后选择暂时回到专用触控板导航。

这证明当前用户/设备组合的可用反馈，不等于所有设备、DPI 或 OneNote 版本都通过独立量化测试。

## 保留的交互和参数

- 双击第三键切换独立页面模式，不关闭原书写映射；贴板滑动移动页面，抬笔换位，第二次双击退出。
- 接触期屏蔽笔尖压力。边界限制本笔行程；接口/上下文错误暂停，保留页面模式，避免下一笔意外写成墨迹。
- 抬笔时触点位置保持，0/40/80 ms 原地更新，120 ms 后取消。快速再次落笔复用触点并重建起点；旧回调不能结束新拖动。
- 防抖系数 0–10，默认 2，按每轴触点像素的位移滞回抑制小幅反向/横向扰动。0 关闭；更大数值增加起步/转向位移，不添加时间追赶或抬笔补移。
- 旧滚轮路线仍在源码中；实际应用可累计滚轮单位或产生动画，不能由发送量推断像素跟随。

## 为什么隐藏而不是删除

用户当前认为触控板专门拖页更合适，希望保留已完成工作，减少普通设置页的实验选项。因此页面能力保留为开发与回归路径；稳定的短按映射开关、按住贴板拖框继续展示。

普通版本的内部 EnablePenPageNavigation 默认为 false，且不是公开配置属性。页面参数也没有 Property/SliderProperty/BooleanProperty 元数据。旧配置中的 EnablePenDoubleTapPagePan=true 不会开启双击等待，旧 PenScrollMode 也不会引发滚轮或触点注入。

## 开发与测试入口

显式开发/测试代码可在构造滤镜后设置内部 EnablePenPageNavigation=true；贴板页面模式还需 EnablePenNavigation=true、EnablePenDoubleTapPagePan=true。NavigationHarness 为历史实验测试显式打开内部开关，普通默认测试则检查其关闭状态。

| 文件 | 作用 |
| --- | --- |
| PrecisionControlPenNavigation.cs | 内部开关、稳定短按及保留的悬空路线 |
| PrecisionControlContactNavigation.cs | 双击页面模式、接触隔离、静止结束 |
| PenDirectPan.cs | 双触点生命周期与位移 |
| NativePenPanPlatform.cs | 运行时 API 检查、目标客户区、DPI 和边框识别 |
| PenPageJitterGuard.cs | 位移防抖 |
| ContactPanFixTests.cs / PageJitterTests.cs | 接触持续性、结束和防抖回归 |
| SettingsSurfaceTests.cs | 默认关闭、无 UI 入口、稳定映射控制 |

未来恢复用户入口时，应同时重审设置加载、版本迁移和真实设备验证；单改 JSON 不是本版入口。开发验证先用空白页，分别确认位移、墨迹隔离、停止和普通书写恢复，不以 API 成功返回替代物理页面结果。

## Evidence boundary

The local user reported the later contact-pan implementation usable. Normal 1.6.0 keeps it off and hidden by choice. Synthetic touch, application-side gesture handling, jitter feel and new hardware combinations still need separate checks. The retained experiment is page navigation, independent of the actual tablet-to-screen mapping.
