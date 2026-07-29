# 次元杀 P0/P1 验收矩阵

本文把实施计划逐条映射到代码和可复现证据。状态均以协议 V2/纯 GameCore 路径为准。

## 0. 可恢复基线与 GPL 治理

| 要求 | 实现/证据 | 状态 |
| --- | --- | --- |
| 初始化 Git 并保存可编译基线 | `b20e767 chore: capture pre-GameCore baseline`；`.gitignore` 排除 Godot、构建、日志和内置引擎 | 通过 |
| GPL-3.0-only | 根目录 `LICENSE`、`NOTICE`、`THIRD_PARTY_NOTICES` | 通过 |
| 无名杀归属与移植记录 | `Docs/noname-source-ledger.md` 固定仓库与提交 `444c7278a6b4b4754eaad4299f77ab31ba8d1655` | 通过 |
| 不引入无名杀素材 | `NOTICE`、第三方声明和 `Docs/asset-license-ledger.md` 明确素材边界 | 通过 |
| 旧 LAN/定向场景迁移清单 | 本文“历史十项 LAN 基线”表；旧场景保留、行为意图迁入 xUnit/V2 | 通过 |

## P0-1. 纯规则内核与结算栈

| 要求 | 实现/证据 | 状态 |
| --- | --- | --- |
| Godot 无关的 .NET 8 内核 | `CiyuanSha.GameCore.csproj`；Domain/Engine 不引用 Godot | 通过 |
| 权威 GameState 与所有牌区 | `Domain/GameState.cs`、`PlayerState`、`CardState` | 通过 |
| 固定种子 RNG 和状态哈希 | `Determinism/DeterministicRandom.cs`、`GameState.ComputeCanonicalHash()`；Debug/Release 摘要一致 | 通过 |
| 父子事件、五阶段生命周期、next/after | `Events/RuleEvents.cs`、`ResolutionStack.cs`；`ChoiceAndEventTests` | 通过 |
| 稳定触发顺序 | `Skills/TriggerScheduler.cs`；特殊前置/后置、优先级、当前座次、技能 ID 测试 | 通过 |
| 选择时暂停并恢复原帧 | `SetChoice`/`ResumeChoiceFrame`；暂停恢复测试与阶段指令 after 队列测试 | 通过 |
| 深度/步数上限和错误包 | `ResolutionStack` 上限；`GameCoreRuntime.ExportFaultBundleAsync`；定向 fault 测试 | 通过 |

## P0-2. 统一选择、网络 V2 与 UI

| 要求 | 实现/证据 | 状态 |
| --- | --- | --- |
| 单一 ChoiceRequest/ChoiceResult | `Choices/ChoiceModels.cs`；局内网络只有 `SubmitChoiceV2` | 通过 |
| 出牌合法操作由主机发布 | `GameEngine.RequestPlayAction`；用牌/技能/重铸/结束阶段均为合法选项 | 通过 |
| 完整主机校验 | 选择 ID、操作者、版本、数量、重复、禁用、伪造与取消共 2 组 xUnit；LAN `stale` | 通过 |
| 公开/私有快照和私有选择 | `GameView`、`RuleJournalVisibility`、`NetworkPrivacyTests`；LAN identity/spectator | 通过 |
| HUD 统一写路径 | `HandZonePanel`、`TargetSelectionPanel`、`ResponseWindowPanel`、`GenericChoicePanel` 均提交 ChoiceResult | 通过 |
| 不可猜令牌、序号防重放 | 256 位令牌、固定时间比较、`EnvelopeSequenceGuard`；协议测试 | 通过 |
| 日志增量与快照回退 | `RuleJournal.TryGetDelta`、握手 `JournalCursor`；窗口回退 xUnit 与活动选择重连 LAN | 通过 |

## P0-3. 标准/军争规则

| 要求 | 实现/证据 | 状态 |
| --- | --- | --- |
| 准备/判定/摸牌/出牌/弃牌/结束 | `GamePhase` 与自动流程；模拟和录像覆盖完整回合 | 通过 |
| 跳过/额外/替换阶段走事件 | `PhaseDirective`、可哈希队列、`PhaseDirectiveScheduled`；4 项定向测试 | 通过 |
| 延时锦囊与判定 | 乐不思蜀/兵粮寸断/闪电移入判定区，改判和闪电传递由内核结算 | 通过 |
| 决斗、无懈、濒死、精确区域牌、逐目标 | `GameEngine` 对应连续选择；规则向量、录像重执行与模拟 | 通过 |
| 基本牌、属性连环、距离、手牌上限、奖惩 | 卡牌/模式定向测试与 12000 局模拟 | 通过 |
| 161 张牌逐张向量 | 148 条实体记录展开 161 张；规范哈希 `c09149…86fa` | 通过 |
| 朱雀羽扇与装备交互 | 43 个牌定义都有显式规则向量；武器/防具重点测试 | 通过 |
| 本地规则差异 | `Docs/gamecore-v2-rules.md` | 通过 |

## P0-4. Skill V2、内容包与武将

| 要求 | 实现/证据 | 状态 |
| --- | --- | --- |
| trigger/filter/check/cost/content 能力模型 | `SkillDefinitionV2`、`ISkillEffect`、统一费用/目标 Choice | 通过 |
| 主动/可选/锁定/限定/转换/标记/临时/子技能 | 16 个技能声明；`SkillV2Tests` 检查全部标签，限定临时子技能和锁定风避实际结算 | 通过 |
| EffectId 驱动 | 主动与被动机制按 `SkillDefinitionV2.EffectId` 分派；未知 EffectId 禁止注册 | 通过 |
| 五个内容包 | `core-rules`、`standard-cards`、`military-cards`、`ciyuansha-generals`、`ciyuansha-boss` | 通过 |
| Manifest 依赖/许可证/哈希 | 内容加载器和缺依赖、未知效果、哈希错误测试 | 通过 |
| 仅白名单 C# 效果 | `EffectRegistry`；无脚本执行或动态程序集加载 | 通过 |
| 7 名迁移 + 5 名新增 | 共 12 名原创武将、16 个技能；转换/判定/元素/支援/限定能力齐全 | 通过 |
| 每武将至少 20 局 | 正式模拟最少 2496 次出场 | 通过 |

## P1-1. 四模式

| 模式 | 验证内容 | 状态 |
| --- | --- | --- |
| 单挑 | 固定 2 席、无隐藏身份、最后存活胜 | 通过 |
| 四人身份 | 主公公开、其他隐藏；主忠反内全部胜负；反贼奖励与主杀忠清空牌 | 通过 |
| 四人 2v2 | A/B 交错入座，消灭对方全队获胜 | 通过 |
| Boss/PvE | 1 Boss + 1–3 英雄；单人机器人/多人；阈值二阶段与双方胜负 | 通过 |
| 大厅约束/机器人/断线保留 | 模式人数校验、机器人填位和难度、席位令牌重连由 xUnit 与 LAN 覆盖 | 通过 |

## P1-2. 三级确定性 AI

| 要求 | 实现/证据 | 状态 |
| --- | --- | --- |
| 简单/标准/困难 | `DeterministicBotPolicy` 的加权、单步效用、固定节点两层搜索 | 通过 |
| 只读合法 GameView | `IBotPolicy.Choose` 只返回 ChoiceResult；隐私视图测试 | 通过 |
| 身份关系与 Boss 策略 | 模式 `GetAttitude` 与 Boss 独立权重/阶段标签 | 通过 |
| 完全确定性 | Debug/Release 240 局摘要相同；正式 11 个严格 20000 内组合的两次 1000 局摘要逐片相同 | 通过 |
| 每组合 1000 局 | `Docs/simulation-report-2026-07-30.md`：12000/12000，零 fault、零拒绝 | 通过 |

## P1-3. 日志、录像与观战

| 要求 | 实现/证据 | 状态 |
| --- | --- | --- |
| 选择/随机/事件/状态哈希日志 | `RuleJournal` 与 SHA-256 前向哈希链 | 通过 |
| ZIP 录像与版本/包校验 | `header.json`、`events.jsonl`、检查点；篡改/版本/协议/缺包测试 | 通过 |
| 离线播放控制 | 主界面录像入口；播放/暂停、前后单步、0.5/1/2/4 倍、时间轴、玩家/公开/全知视角 | 通过 |
| 录像不启动网络/AI | `ReplayController` 只构造 `ReplayPlaybackSession` | 通过 |
| 中途公开观战 | LAN `spectator`，不能提交选择 | 通过 |
| 主机全知/参与者有限录像 | `LocalReplayCaptureV2`；完整 LAN 对局同时落盘并重开验证 | 通过 |
| 异常复现包 | 种子、模式、包哈希、最近日志和状态哈希写入 ZIP | 通过 |

## 历史十项 LAN 基线迁移

旧场景使用已禁止的结果式局内命令，不再作为 V2 协议入口；以下行为意图已保留：

| 旧场景 | 新验收证据 |
| --- | --- |
| `general_select` | 内容包/大厅模式校验 + LAN duel/identity/boss |
| `dodge` | 闪/八卦阵/锁定风避 xUnit + 完整对局录像 |
| `peach_save` | 桃/酒濒死座次救援规则向量 |
| `peach_rescue` | 濒死响应链与完整模拟 |
| `death_no_save` | 四模式死亡/胜负测试 |
| `slash_limit` | 杀次数、诸葛连弩和转换杀测试/模拟 |
| `weapon_range` | 距离、坐骑、武器范围与目标合法性向量 |
| `full_round_basic` | 六阶段、12000 局和录像重执行 |
| `identity_victory` | 身份全部胜负组合和击杀奖惩 xUnit + LAN identity4 |
| `return_lobby_after_match` | LAN V2 `replay` 完整对局后全体返回大厅且取消准备 |

## 最终闸门结果

- Debug/Release 全解决方案：零警告、零错误。
- xUnit：52/52。
- LAN V2：8/8；同一轮 `replay` 场景包含录像返回大厅检查。
- 12000 局：12000/12000，0 fault，0 拒绝选择。
- Godot 4.6.2 主场景和录像场景实际加载通过。
- 1366×768、1920×1080 统一选择截图和 1920×1080 录像 UI 截图通过人工检查。
