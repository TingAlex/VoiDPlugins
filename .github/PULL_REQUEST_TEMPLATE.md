## 变更内容

<!-- 简述改了什么、为什么 -->

## 关联 issue

<!-- 例如 Closes #8 、Part of #7 -->

## 测试与证据

<!-- 跑了哪些测试、构建、安装；贴关键日志或命令输出 -->

- [ ] `dotnet test tests/PrecisionControl.Tests/PrecisionControl.Tests.csproj -c Release` 通过
- [ ] （如涉及）打包脚本执行成功

## 硬件验证

<!-- 涉及真实设备行为时必须填写；没有验证就写“未验证，待实机确认”，不要把模拟测试写成硬件通过 -->

- 设备：
- OTD / 系统版本：
- 是否实机验证：

## 检查清单

- [ ] 设置项 `Property` ID 未破坏旧配置
- [ ] 行为/默认值变化已同步 `docs/SETTINGS.md`、`docs/TOUCHPAD-DRAG.zh-CN.md`、`CHANGELOG.md`
- [ ] 相对 Markdown 链接有效
- [ ] 未提交 bin/obj、日志、个人配置或隐私内容
- [ ] 已更新对应 issue / 看板 Status
