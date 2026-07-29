# 已授权战斗表现资源清单

> 状态：2026-07-28 已完成首批筛选、转换与接入。

## 授权边界

- 项目负责人已确认 `E:\slay the spire` 素材获得授权，可用于《次元杀》。工程记录见 `Assets/Licenses/UserAuthorizedAssets-2026-07-28.txt`。
- 正式发行前仍需归档底层合同或许可文本；此工程记录不替代法律文件。
- 当前只允许 `Docs/asset-license-ledger.md` 已登记的 9 张纹理和 8 个音效进入构建，不能因目录整体授权而无审查批量导入。

## 转换方式

- 候选目录：`E:\slay the spire\resource_catalog.json`。
- 旧 `extracted` 缓存存在固定 112 字节切片偏移，不能作为运行时资源。
- `Scripts/Tools/ConvertAuthorizedAssets.gd` 只读挂载本机授权资源包，按精确资源键导出标准 PNG/MP3。
- 项目不保存原始 PCK、`.ctex`、`.mp3str`、`.spatlas` 或 `.spskel`。
- 转换暂存与未采用画面位于 `E:\slay the spire\ciyuansha_conversion_staging`，不属于《次元杀》工程或构建。

## 已接入映射

- 开场：金光、圆烟、`battle_start.mp3`。
- 出牌/响应：程序化水墨牌影、金色脉冲、`card_select.mp3`。
- 摸牌/装备/判定：`card_deal.mp3`，保持低强度。
- 物理伤害：朱红斩击烟、命中闪光、火星、`physical_impact.mp3`。
- 火焰伤害：3x2 火焰序列、暗红烟、`fire.mp3`。
- 雷电伤害：3x3 雷电序列、雷紫光晕、`thunder.mp3`。
- 治疗：青玉光晕、治疗标识、上升微粒、`heal.mp3`。
- 阵亡：暗红压屏、消散烟、`defeat.mp3`。

## 已筛除

- `echoing_slash.png`：包含完整外部角色第一人称画面，与《次元杀》牌桌语境不符。
- `flying_slash.png`：属于骨骼动画图集，单独使用会出现不连续裁片。
- 其他候选资源：尚未逐项视觉验收和登记，不进入项目。

## 复核入口

- 开发场景：`Scenes/Test/BattleFxShowcase.tscn`。
- 支持参数：`--cue=card|physical|fire|thunder|heal|response|defeat`。
- 可选自动截图：`--capture=绝对路径 --auto-quit=true`。
- 每次增添资源后必须更新许可台账、哈希、用途和视觉截图。
