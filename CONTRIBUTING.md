# 贡献指南 / Contributing

Precision Control (TingAlex Enhanced) 是面向 OpenTabletDriver 的 Windows 插件：把数位板映射到屏幕上的小范围，并用旋钮、笔键或触控板三指拖框移动这个范围。本文件说明如何构建、测试、提交和交接。

## 1. 环境

| 依赖 | 版本 | 说明 |
| --- | --- | --- |
| Windows | 10/11 | 边框、Raw Input、全局快捷键都依赖 Win32 |
| .NET SDK | 6.0 | 插件与测试的目标框架 |
| .NET SDK | 9.0 | 仅 `tools/TouchpadPenProbe`、`tools/BleHidAccessProbe` 需要 |
| OpenTabletDriver | 0.6.7 | 作者实际使用版本；插件元数据声明支持 0.6.7 |
| git | 任意 | 仓库包含子模块 `.modules/OpenTabletDriver` |

## 2. 克隆与构建

```powershell
git clone --recursive https://github.com/TingAlex/VoiDPlugins.git
cd VoiDPlugins

dotnet build src/Filter/PrecisionControl/PrecisionControl.csproj -c Release
dotnet test  tests/PrecisionControl.Tests/PrecisionControl.Tests.csproj -c Release
```

打包（需要 PowerShell 7，产物在 `build/releases`）：

```powershell
pwsh scripts/Package-PrecisionControl.ps1
```

安装、升级与回退见 [SETUP.md](docs/SETUP.md)；全部设置字段见 [SETTINGS.md](docs/SETTINGS.md)。

## 3. 目录

| 路径 | 内容 |
| --- | --- |
| `src/Filter/PrecisionControl/` | 插件源码（映射、边框、笔键、触控板拖框） |
| `tests/PrecisionControl.Tests/` | xUnit 测试（当前 220 项） |
| `docs/` | 安装、设置、触控板拖框、开发、路线图 |
| `tools/` | 诊断探针，不随插件包发布 |
| `.github/workflows/` | 构建、测试与发布工作流 |

## 4. 分支与提交

- 分支：`feature/<topic>`、`fix/<topic>`、`docs/<topic>`；`master` 保持可发布状态。
- 提交标题用祈使句英文，例如 `Fix the jump at three-finger lift`；正文写动机、改动点与验证方式。
- 一个 PR 只解决一件事；PR 描述引用 issue（`#N`），合并前 CI 必须通过。
- 不提交 `bin/`、`obj/`、日志、个人 `settings.json`、设备序列号或笔记内容。

## 5. PR 检查清单

- [ ] `dotnet test` 通过（当前 220 项）
- [ ] 行为、默认值、设置范围变化时，同步更新 `docs/SETTINGS.md`、`docs/TOUCHPAD-DRAG.zh-CN.md`、`CHANGELOG.md`
- [ ] 涉及硬件行为时，注明“已实机验证”或“待验证”；模拟测试不能写成硬件通过
- [ ] 相对 Markdown 链接有效
- [ ] 新增/修改设置项时，`Property` ID 保持稳定，旧配置不能因此失效

## 6. 证据分级

沿用项目既有惯例，区分：

- **S**：源码/文档证据（可引用的代码位置、OTD 配置）
- **T**：本机实测（绑定设备、系统、驱动、时间）
- **R**：用户反馈
- **H**：设计假设
- **U**：未验证

硬件结论必须绑定具体设备与版本；一次失败不要泛化为“硬件不可能”。

## 7. 开发看板

- 看板：https://github.com/users/TingAlex/projects/2
- 字段：`Status`（Todo / In Progress / Done）、`Priority`（P0–P2）、`Area`（Touchpad / Pen / Core / Docs / Build）、`Size`、`Target`（v1.9.x / v1.10 / v2.0）
- 新工作先开 issue 并写验收标准，再加入看板；进行中时把 Status 改成 In Progress。

## 8. 调试

- OTD 日志：`%LOCALAPPDATA%\OpenTabletDriver\Logs\`（`PrecisionControl` 组）
- 触控板帧探针：`dotnet run --project tools/TouchpadFrameProbe`（复用插件同一份 Raw Input 监听，先确认输入层有没有数据）
- 关键日志：`Touchpad drag listening...`、`received its first precision touchpad frame`、`Touchpad drag started/ended`
- 安装后无反应：确认 `EnableTouchpadDrag` 已保存、重启 OTD、笔离开感应区

## 9. 多机 / AI 交接

1. 先读 [docs/ROADMAP.md](docs/ROADMAP.md) 与开发看板，确认当前里程碑与未完成项。
2. 按第 2 节完成 clone、构建、测试；改动前先跑一遍测试确认基线。
3. 不要修改用户的 OTD 配置、设备驱动或系统设置，除非任务明确要求。
4. 完成一个工作项后：更新对应 issue、必要时更新文档与 CHANGELOG，再提交 PR。

## English quick start

```powershell
git clone --recursive https://github.com/TingAlex/VoiDPlugins.git
cd VoiDPlugins
dotnet build src/Filter/PrecisionControl/PrecisionControl.csproj -c Release
dotnet test  tests/PrecisionControl.Tests/PrecisionControl.Tests.csproj -c Release
pwsh scripts/Package-PrecisionControl.ps1
```

Read [docs/ROADMAP.md](docs/ROADMAP.md) and the [project board](https://github.com/users/TingAlex/projects/2) before starting. Hardware-dependent changes must state whether they were verified on a real tablet/touchpad; automated tests are not hardware proof.
