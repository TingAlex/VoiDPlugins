# 维护、双机升级与回退

当前功能进入维护期。保留三指拖框、笔键、旋钮和快捷键的已有行为；四指推广、速度自适应、英文详细手册和系统手势自动检测暂不开发。

## 每台电脑保留自己的参数

默认值用于新建、缺省或重置。两台电脑可以有不同的 Scale、锚点、倍率、步长和方向设置；升级和回退均不写 `settings.json`，不自动同步参数。以后如有多个参数模板的需要，再单独定义。

## 安装包升级

先关闭 OTD 界面和后台，再在解压后的安装包目录运行（Windows PowerShell 5.1 或 PowerShell 7）：

```powershell
.\scripts\Install-PrecisionControl.ps1 -PackageDirectory .\plugin
```

脚本在实际数据目录下查找唯一的 `PrecisionControl.dll`，不依赖插件文件夹的名字。便携数据目录可加 `-DataDirectory 'D:\OTD-data'`。发现多份 DLL 时停止，由用户处理重复安装，不猜测目标。

备份默认放在数据目录 `Backups/PrecisionControl/`，包括原 DLL、可选 metadata、配置快照、原文件哈希和安装验证。旧安装没有 metadata 也可升级。输出 `settingsByteIdentical=true` 表示保存配置文件逐字节未变，不等于运行配置或物理手感已验证。

若需要脚本重启正在运行的 OTD，须在当前已登录的交互桌面执行，并明确原运行目录：

```powershell
.\scripts\Install-PrecisionControl.ps1 -PackageDirectory .\plugin -Restart -RuntimeDirectory 'C:\path\to\OpenTabletDriver'
```

它只重启该目录、当前会话中原来运行的进程。SSH Session 0 不允许使用 `-Restart`；远程维护应复用已有登录桌面通道，不新增服务、驱动或免密码登录。

## 回退

关闭 OTD 后，使用安装输出中的 `backupDirectory`：

```powershell
.\scripts\Install-PrecisionControl.ps1 -RestoreBackup 'C:\path\to\backup'
```

可同样指定 `-DataDirectory`、`-Restart` 和 `-RuntimeDirectory`。脚本校验备份哈希，恢复原 DLL/metadata；原本没有 metadata 时恢复其缺失状态。回退保留当前设置，不把用户后来调整的参数覆盖成旧快照。实际参数需要人工恢复时，可参考备份的 `settings.before.json`。

## 升级后的核对

1. 两台电脑的 DLL 版本与哈希应对应同一发行包；分别记录各自参数和显示器环境。
2. 连接数位板，启动原 OTD，日志应显示插件加载、设备就绪；开启三指拖框且滤镜初始化后应出现后台监听日志。没有设备时不能用“没有滤镜日志”推断插件坏了。
3. 在 Windows 触控板设置中检查三指手势；按当前配置选择“无操作”，避免系统动作与拖框同时触发。脚本不修改这些设置。
4. 在每台电脑的 OneNote 空白测试页，用该机原参数：启用映射 → 拖框 → 同时/逐根抬手 → 落笔 → 退出映射。检查页面不动、拖框无墨迹、下一笔落点正确、退出恢复。

第 4 步由用户实笔确认，对应 [#8](https://github.com/TingAlex/VoiDPlugins/issues/8)；两台现用屏幕的 DPI/落点对应 [#9](https://github.com/TingAlex/VoiDPlugins/issues/9)。远程命令成功和自动测试不替代这两项。

## 已验证的安装工具边界

2026-11-15：在隔离副本中验证升级、哈希恢复、配置保留、缺失 metadata 的恢复与重复 DLL 拒绝；Windows PowerShell 5.1 与 PowerShell 7 均通过安装/回退。测试没有覆盖所有便携布局、第三方自动启动工具或新系统环境。
