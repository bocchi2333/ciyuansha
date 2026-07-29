# 次元杀 P0/P1 实施状态

状态：P0/P1 计划已实现并进入稳定版验收。GameCore/内容 API 为 `2.0.0`，网络协议为 `2`，不提供协议 V1 兼容层。

## 已落地模块

| 计划项 | 主要实现位置 | 状态 |
| --- | --- | --- |
| 可恢复基线与 GPL | Git 基线提交、`LICENSE`、`NOTICE`、`THIRD_PARTY_NOTICES`、`Docs/noname-source-ledger.md` | 完成 |
| 纯规则状态、结算栈、确定性 RNG | `CiyuanSha.GameCore/Domain`、`Events`、`Determinism`、`Engine` | 完成 |
| 统一选择与裁剪视图 | `Choices`、`Domain/GameView.cs`、三个专用渲染器和 `GenericChoicePanel` | 完成 |
| 网络 V2、握手、令牌、序号和增量重连 | `CiyuanSha.GameCore/Networking`、`Scripts/Networking` | 完成 |
| 六阶段与标准/军争结算 | `Engine/GameEngine.cs`、`Data/Packs/standard-cards`、`military-cards` | 完成 |
| Skill V2、稳定触发排序与五内容包 | `Skills`、`Content`、`Data/Packs` | 完成 |
| 12 名原创武将、16 个技能声明 | `Data/Packs/ciyuansha-generals` | 完成 |
| 单挑/身份/2v2/Boss | `Modes/GameModes.cs`、LAN 大厅模式配置 | 完成 |
| 简单/标准/困难确定性 AI | `AI/BotPolicies.cs` | 完成 |
| 日志、录像、观战、错误复现包 | `Replay`、`GameCoreRuntime.cs`、`LocalReplayCaptureV2.cs`、录像 UI | 完成 |
| xUnit、LAN、视觉和 12000 局模拟 | `CiyuanSha.GameCore.Tests`、`Scripts/Test` | 完成 |

## 权威状态边界

协议 V2 的唯一局内写入口是 `SubmitChoice`：主机校验后调用 `GameCoreRuntime`/`GameEngine`，再把按视角裁剪的 `GameView` 和规则日志发送给客户端。HUD 的手牌、目标、响应与通用面板均只把当前合法选项封装为 `ChoiceResult`，不再发送伤害、摸牌、移动牌等结果命令。

`GameManager` 在 V2 对局中只启动内核、提交选择、把 `GameView` 投影为 Godot 展示状态并驱动网络快照。仓库仍保留旧离线场景使用的历史规则方法和 `PlayerCharacter` 可变展示模型，便于回归旧场景；`IsCoreMatchActive` 会阻断该历史流程，它们不参与 V2 权威结算。新规则和新内容不得写入历史路径。

## 最终证据

- xUnit：52/52。
- LAN V2：8/8；录像场景还验证结束后返回大厅。
- 固定种子模拟：12000/12000，0 fault，0 拒绝选择；套件摘要 `4fc70ab53c4c1282747fcaec3614e6890ca09d20f46cd8a35a03393a37cfba17`。
- 12 名武将最少出场 2496 次。
- 161 张牌实体向量 SHA-256：`c09149c0d6a88d45b3533250f75c13a548df33f38f82e74851cdf8223be486fa`。
- Godot 主场景、录像面板、1366×768 与 1920×1080 统一选择面板均通过实际加载/截图检查。

逐项映射见 `p0-p1-acceptance-audit.md`，复现命令见 `testing-gamecore-v2.md`。
