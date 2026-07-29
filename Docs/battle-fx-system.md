# 《次元杀》战斗表现系统

## 边界

`BattleFxDirector` 只消费权威规则产生的演出事件，不修改体力、手牌、响应、动作队列或胜负。关闭声音、关闭视觉特效或启用低动态模式后，规则结果保持完全一致。

## 数据流

1. 房主 `GameManager.EmitRuleEvent` 完成规则事件广播。
2. 需要表现的事件被压缩为 `NetworkPresentationEvent`，分配递增 `Sequence`。
3. 最近 32 条随 `MatchStateSnapshot.PresentationEvents` 同步。
4. 客户端按序列号只播放尚未处理的新事件；首次进入进行中的对局不会重放历史表现。
5. `CyberMatchHudPanel` 负责中文结算大字，`BattleFxDirector` 负责纹理、动画与音效。

## 当前事件

- `MatchStarted`：开场声、金光与烟雾。
- `CardUsed`：手牌区到中央舞台的短牌影、选牌声。
- `DamageApplied`：按 `Physical`、`Fire`、`Thunder` 分流纹理和音效。
- `Healed`：青玉脉冲、治疗标识与上升微粒。
- `ResponseUsed`：金色响应闪光与高音高选牌声。
- `CharacterDefeated`：暗红压屏、消散烟与阵亡音效。
- `CardsDrawn`、`CardsDiscarded`、`EquipmentEquipped`、`JudgementPerformed`：低强度音频或局部闪光。

## 音频

- 运行时按低/标准/高质量档使用最多 3/5/7 个 `AudioStreamPlayer` 声道，优先复用空闲声道，满载时轮换最早声道。
- 同类音效加入轻微随机音高，减少连续伤害的机械重复感。
- 默认玩家音量为 70%，0% 是硬静音；`GameUserSettings` 会把线性百分比换算为 dB。
- `StopAllAudio` 用于静音切换与安全退出，防止长音效残留。

## 视觉

- 所有序列帧运行时按红通道重建遮罩，兼容授权素材中的通道打包纹理并去除方形底色。
- 动效定位优先使用目标 `GeneralCardView.BoundPeerId` 的屏幕中心；找不到目标时回退到中央舞台。
- 物理、火焰、雷电和治疗使用不同色彩与形态，不能只靠颜色区分。
- 大多数演出为 0.18-0.80 秒，节点在 Tween 完成后自动释放，不阻塞 UI 点击。
- `ReducedMotion` 会压缩位移和持续时间，但保留结算可见性。
- 低/标准/高质量档的同时存活视觉节点预算为 12/28/48；超出时优先清理最早的临时演出。
- 同一目标 220 毫秒内连续受影响时会使用小幅错位，避免多段伤害完全重叠。

## 本机设置

- `/root/GameUserSettings` 使用 `ConfigFile` 保存到 `user://ciyuansha_settings.cfg`。
- `PresentationSettingsPanel` 在大厅和局内共用，支持音效、音量、特效、屏幕闪光、低动态与质量档。
- 关闭屏幕闪光只关闭全屏明暗变化，局部目标效果与规则文字仍然保留。
- 详细接口和烟测命令见 `Docs/presentation-settings.md`。

## 校准场景

在 Godot 中打开并运行 `Scenes/Test/BattleFxShowcase.tscn`，或使用：

```powershell
Godot_v4.6.2-stable_mono_win64_console.exe --path E:\次元杀 res://Scenes/Test/BattleFxShowcase.tscn -- --cue=fire
```

自动截图示例：

```powershell
Godot_v4.6.2-stable_mono_win64_console.exe --path E:\次元杀 res://Scenes/Test/BattleFxShowcase.tscn -- --cue=thunder --capture=E:/次元杀/Build/VisualChecks/thunder.png --auto-quit=true
```

连续爆发和质量档示例：

```powershell
Godot_v4.6.2-stable_mono_win64_console.exe --headless --path E:\次元杀 res://Scenes/Test/BattleFxShowcase.tscn -- --cue=burst --quality=balanced --auto-quit=true
```

## 后续

- 增加目标到目标的牌轨迹和装备专属反馈，但继续限制屏幕噪声。
- 朋友实机反馈后校准不同耳机/音箱上的响度和连续触发主观密度。
