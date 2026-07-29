# GameCore V2 验证手册

所有命令从仓库根目录执行。

## 快速闸门

```powershell
dotnet build CiyuanSha.sln --no-restore
dotnet run --project CiyuanSha.GameCore.Tests/CiyuanSha.GameCore.Tests.csproj --no-build
dotnet run --project CiyuanSha.GameCore.Simulations/CiyuanSha.GameCore.Simulations.csproj --no-build -- --matches 20
& ./Scripts/Test/RunLanSmokeTests.ps1
```

测试执行器是仓库内的零依赖反射式测试程序，避免首次验证依赖 NuGet 网络。非零退出码表示失败。

## 发布前长跑

```powershell
dotnet run --project CiyuanSha.GameCore.Simulations/CiyuanSha.GameCore.Simulations.csproj -c Release -- --matches 1000
```

`--matches` 是“每模式、每难度”的局数。因此该命令会运行 `4 × 3 × 1000 = 12000` 局。验收条件：

- `FaultedMatches = 0`
- `RejectedChoices = 0`
- 同样参数连续两次的 `DeterminismDigest` 完全相同
- `GeneralAppearances` 中 12 名武将均大于零

本地执行器有单命令壁钟限制时，可按矩阵分片；例如：

```powershell
dotnet run --project CiyuanSha.GameCore.Simulations/CiyuanSha.GameCore.Simulations.csproj -c Release --no-build -- --matches 1000 --mode team_2v2 --difficulty Standard --parallelism 4
```

`--mode` 接受 `duel`、`identity`、`team_2v2`、`boss_pve`，`--difficulty` 接受 `Easy`、`Standard`、`Hard`。
分片验收要求 12 个组合各自 `Completed=1000`；不能用多个分片的总数替代缺失组合。
失败项可用 `--start-index <索引> --matches 1` 精确复现；索引和种子都会保留在 `Failures` 中。

## 联机与隐私检查

LAN 脚本覆盖 2/3/4 人、机器人、响应链、全回合和重连等既有场景。协议 V2 另由纯内核测试覆盖：

- 错误协议、引擎版本、内容包版本或哈希必须拒绝握手。
- 重复、过期、错误操作者或伪造选项不能改变状态。
- 其他玩家和公开观战视角看不到手牌身份、隐藏身份或私有候选项。
- 日志游标可用时下发增量；不可用时回退为按视角裁剪的完整快照。
- 观战席不能提交局内选择；重连只认 256 位随机席位令牌。

## 录像检查

`.cysreplay` 是 ZIP，至少包含 `header.json` 和 `events.jsonl`。测试要求哈希链、防篡改、内容包验证和离线重执行通过。
播放控制器支持暂停、单步、倍速、事件跳转以及玩家/公开/全知视角切换；播放时不启动网络或 AI。
