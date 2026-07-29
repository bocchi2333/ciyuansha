# GameCore V2 验证手册

所有命令从仓库根目录执行，测试框架为 xUnit。

## 快速闸门

```powershell
dotnet build CiyuanSha.sln -c Debug --nologo
dotnet test CiyuanSha.GameCore.Tests/CiyuanSha.GameCore.Tests.csproj `
  -c Debug --no-restore --logger "console;verbosity=minimal"
& ./Scripts/Test/RunLanV2Acceptance.ps1 -TimeoutSeconds 90 -NoBuild
```

当前纯规则测试共 52 项，覆盖：

- 事件生命周期、`next/after`、暂停恢复、最大深度和最大步数；
- 统一选择的请求 ID、操作者、状态版本、数量、重复项、禁用项和取消策略；
- 阶段跳过、额外阶段、阶段替换及其状态哈希；
- 161 张实体牌向量、43 个牌定义和所有内置装备效果；
- 濒死、身份奖惩、四模式胜负、Boss 阈值转换；
- Skill V2 标签、触发排序、限定/临时子技能和锁定自动闪避；
- 内容包哈希、依赖、未知 EffectId；
- 视图/日志隐私、协议握手、包序号防重放、重连令牌；
- 录像哈希链、篡改、版本/协议/缺包拒绝、全知重执行与有限视角锁定。

## LAN V2

```powershell
& ./Scripts/Test/RunLanV2Acceptance.ps1 -TimeoutSeconds 90
```

脚本启动真实 Godot/ENet 多进程，覆盖：

1. `duel2`：双人单挑；
2. `boss3`：Boss 与两名英雄，并验证模式补位；
3. `identity4`：四人身份及隐藏信息；
4. `bots`：三机器人填位及三档难度；
5. `reconnect`：活动选择中断线、同席令牌恢复和日志增量；
6. `spectator`：中途公开观战、日志裁剪和禁止提交；
7. `stale`：重复/过期选择拒绝；
8. `replay`：完整对局、主机全知录像、观战有限录像、重开校验，以及全体返回大厅并取消准备。

最终全套报告：`Build/LanV2Acceptance/ce3f0d05c1/summary.json`。其中 `replay` 场景已同时验证完整对局、录像重开校验、参与者返回大厅和非观战席取消准备状态。

旧 `LanSmokeTest.tscn` 和十个 V1 表现命令场景作为迁移来源保留，不再作为协议验收入口；其行为意图已映射到 xUnit 与 V2 场景，见 `p0-p1-acceptance-audit.md`。协议 V2 不接受旧局内命令。

## 录像 UI 与视觉检查

主场景/资源解析：

```powershell
& ./Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64/Godot_v4.6.2-stable_mono_win64_console.exe `
  --headless --path . --quit-after 10
```

`Scenes/Test/ReplayViewerShowcase.tscn` 可接收：

```text
--replay=<absolute .cysreplay path> --capture=<absolute png path>
```

它会载入并验证录像、跳转、切视角、改倍速、单步并截图。最终截图为
`Build/VisualChecks/replay-viewer-1920x1080.png`。

统一选择面板截图：

- `Build/VisualChecks/generic-choice-1366x768.png`
- `Build/VisualChecks/generic-choice-1920x1080.png`

两个 PNG 均须人工检查无裁切、重叠或不可读文本。

## 正式模拟

```powershell
& ./Scripts/Test/RunSimulationAcceptance.ps1 `
  -MatchesPerCombination 1000 `
  -ParallelismPerShard 2 `
  -MaximumDecisions 50000
```

验收条件：12 个组合各 `Completed=1000`，`Faulted=0`，`RejectedChoices=0`，失败列表为空，12 名武将均至少出场 20 次。完整结果见 `simulation-report-2026-07-30.md`。

## 发布闸门

```powershell
dotnet build CiyuanSha.sln -c Debug --nologo
dotnet test CiyuanSha.GameCore.Tests/CiyuanSha.GameCore.Tests.csproj -c Debug --no-restore
dotnet build CiyuanSha.sln -c Release --nologo
dotnet test CiyuanSha.GameCore.Tests/CiyuanSha.GameCore.Tests.csproj -c Release --no-restore
git diff --check
```

必须同时满足：Debug/Release 零警告零错误、52 项测试全过、LAN V2 8/8、12000 局全过、录像 UI 和两个选择面板分辨率检查通过、工作区无意外生成文件。
