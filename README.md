# 次元杀

次元杀是 Godot 4.6 + .NET 8 实现的主机权威局域网卡牌游戏。当前稳定基线包含标准/军争 161 张牌、12 名原创武将、单挑/四人身份/四人 2v2/Boss PvE、三级确定性 AI、公开观战、断线重连和可验证录像。

规则层位于 `CiyuanSha.GameCore`，不依赖 Godot。局内数据流为：

```text
客户端 ChoiceResult
  -> 主机协议/版本/序号/席位/合法性校验
  -> GameEngine + ResolutionStack
  -> RuleJournal
  -> 按玩家或观战视角裁剪的 GameView
  -> Godot HUD
```

协议 V2 不兼容旧客户端，局内唯一写命令是 `SubmitChoice`。内容包只能声明已注册的 C# EffectId，不执行任意脚本或动态程序集。

## 开发与验证

```powershell
dotnet build CiyuanSha.sln -c Debug
dotnet test CiyuanSha.GameCore.Tests/CiyuanSha.GameCore.Tests.csproj -c Debug
& ./Scripts/Test/RunLanV2Acceptance.ps1 -TimeoutSeconds 90
```

正式 12000 局 AI 验收：

```powershell
& ./Scripts/Test/RunSimulationAcceptance.ps1 `
  -MatchesPerCombination 1000 `
  -MaximumDecisions 50000
```

详细结果见：

- `Docs/p0-p1-acceptance-audit.md`
- `Docs/testing-gamecore-v2.md`
- `Docs/simulation-report-2026-07-30.md`
- `Docs/gamecore-v2-rules.md`
- `Docs/noname-source-ledger.md`

Windows x64 自包含便携版的生成方法见 `Docs/windows-portable-release.md`。

## 许可证与上游归属

项目采用 `GPL-3.0-only`。规则事件、统一选择、技能模型、内容包、AI、录像和标准/军争机制参考无名杀仓库 `libnoname/noname` 的提交 `444c7278a6b4b4754eaad4299f77ab31ba8d1655`，详细对应关系见移植台账。

仓库不包含无名杀角色美术、音频或来源不明确的素材。发布源码或二进制时必须一并保留 `LICENSE`、`NOTICE`、`THIRD_PARTY_NOTICES` 和对应源码。
