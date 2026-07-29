# 次元杀 P0/P1 实施状态

完成基线：GameCore/协议/内容 API `2.0.0`，网络协议 `2`，不提供 V1 兼容握手。

## 已落地模块

| 计划项 | 实现位置 | 状态 |
| --- | --- | --- |
| 可恢复基线与 GPL | 根 Git 仓库、`LICENSE`、`NOTICE`、`THIRD_PARTY_NOTICES`、移植台账 | 完成 |
| 纯规则状态、事件栈、确定性 RNG | `CiyuanSha.GameCore/Domain`、`Events`、`Determinism` | 完成 |
| 统一选择与裁剪视图 | `Choices`、`Domain/GameView.cs`、`UI/GenericChoicePanel.cs` | 完成 |
| 网络 V2、握手、令牌、增量同步 | `Networking`、`Scripts/Networking` | 完成 |
| 六阶段与标准/军争核心结算 | `Engine/GameEngine.cs`、`Data/Packs` | 完成；规则窄化见规则文档 |
| Skill V2 与五个内容包 | `Skills`、`Content`、`Data/Packs` | 完成 |
| 12 名原创武将 | `ciyuansha-generals`、`general_catalog.json` | 完成 |
| 单挑/身份/2v2/Boss | `Modes/GameModes.cs`、LAN 大厅模式配置 | 完成 |
| 简单/标准/困难 AI | `AI/BotPolicies.cs` | 完成 |
| 日志、录像、观战、错误包 | `Replay`、`GameCoreRuntime.cs`、`ReplayController.cs` | 完成 |
| 无界面模拟和 LAN 回归 | `GameCore.Tests`、`GameCore.Simulations`、`Scripts/Test` | 完成 |

## 适配层边界

`GameCoreRuntime` 是 Godot 与纯内核之间的适配器。网络上的局内输入只接受 V2 `SubmitChoice`；现有水墨 HUD 的旧按钮命令会先封装为
V2 表现层载荷，再由主机校验。这个桥接只为保留现有 UI，不构成协议 V1 兼容，也不允许客户端直接改权威状态。

旧 `GameManager` 暂时仍承载动画时序和既有场景回归，新增规则不得继续写入其中。新内容必须通过内容包、`ChoiceRequest` 和
`GameEngine` 实现。待所有旧 HUD 渲染器都改读 `GameView` 后，可在不改变协议和录像格式的情况下删除表现桥。

## 发布闸门

本次基线已通过 [`simulation-report-2026-07-30.md`](simulation-report-2026-07-30.md) 记录的 12000 局长跑和 10 项 LAN 回归。
以后发布仍须重新执行 [`testing-gamecore-v2.md`](testing-gamecore-v2.md) 的快速闸门和长跑；长跑不在普通增量构建中自动运行，
报告必须连同引擎版本、内容哈希和确定性摘要归档。
