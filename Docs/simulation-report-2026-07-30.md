# GameCore V2 固定种子模拟验收报告（2026-07-30）

## 验收口径

- 引擎 API：`2.0.0`
- 网络协议：`2`
- 每个“模式 × AI 难度”固定运行 1000 局，共 `4 × 3 × 1000 = 12000` 局。
- AI 搜索使用固定节点预算；单局模拟安全上限为 50000 次合法选择，不使用墙钟改变搜索结果。
- 通过条件：全部对局正常结束、零引擎 fault、零非法/被拒绝 AI 选择、12 名武将均至少出场 20 次。

内容包哈希：

| 内容包 | 版本 | SHA-256 |
| --- | --- | --- |
| `ciyuansha-boss` | 2.0.0 | `a78609fa5a21ac0cdc594ae098948ea4db7bd8bea638a7d0297dc54d115767d5` |
| `ciyuansha-generals` | 2.0.0 | `f30dae124c64643282c0c024f9f690b7ada503a1c77892e2bc890d1a452e82b9` |
| `core-rules` | 2.0.0 | `6421282a15d8b1663747f318466059f71816d5be93afb3961eab6869b8e745ad` |
| `military-cards` | 2.0.0 | `59db7e376de74c18bbe7b540f05ac5793b006823f8bc0efd153b47a988a9e22a` |
| `standard-cards` | 2.0.0 | `2e83ba4876095233f5f885aba827b1e66116ed2200ff66669ee0c305172aeb6e` |

## 正式长跑结果

| 模式 | 难度 | 完成 | fault | 拒绝选择 | 最大决策数 | 确定性摘要 |
| --- | --- | ---: | ---: | ---: | ---: | --- |
| `duel` | Easy | 1000 | 0 | 0 | 584 | `4e0d2be138f3643b49c9c9c356259d290262bc5d2d7778f6a3999e45ed302649` |
| `duel` | Standard | 1000 | 0 | 0 | 2066 | `7a8c50acfb79d3c51be187be801144da542a6eecc8fd7ffb1b126e46b2411c4a` |
| `duel` | Hard | 1000 | 0 | 0 | 1266 | `091a1b56ce82ea2aec79fe670e6fc511a646ce6cedca8e978acf93c10416e870` |
| `identity` | Easy | 1000 | 0 | 0 | 1382 | `a628ce5cb2d885d4c9fdb71d8071c5f6e17a74ae7daa4dc6d6bd923b52e2ae48` |
| `identity` | Standard | 1000 | 0 | 0 | 7427 | `d9542582afee5052055cb41805befe037dea2f13c89fe3c9960eb9516a29a95c` |
| `identity` | Hard | 1000 | 0 | 0 | 8854 | `8a995f913d4da3bf6376228157796ce91086d995fa0071015a3cc4ae5059cbd2` |
| `team_2v2` | Easy | 1000 | 0 | 0 | 1965 | `35a706f72bca62cddd74629f2515e581dce0d4b0998914d9712270ecf716460d` |
| `team_2v2` | Standard | 1000 | 0 | 0 | 34619 | `905124a916cd0de18c2e1a5cf26826400fcd942e56d5135818fae2b8921e73f5` |
| `team_2v2` | Hard | 1000 | 0 | 0 | 11093 | `2816e2568b2419df4daae041e61e11912d1e2542f93407d7454cd573f634d08a` |
| `boss_pve` | Easy | 1000 | 0 | 0 | 893 | `024de52b9c636426a087678ea1bf98fab8025ee7058612277e8df470606e9ddb` |
| `boss_pve` | Standard | 1000 | 0 | 0 | 2498 | `41ee724b796de3439c9570e37b9293f29e2a3950c18ed11f38ccf5b801ad6330` |
| `boss_pve` | Hard | 1000 | 0 | 0 | 2428 | `a8c9d1f0c945ce1b266fe748796de4a3d4701c348144bec24bf045c0b2ea6a89` |

汇总：`12000/12000` 正常结束，`Faulted=0`，`RejectedChoices=0`；套件摘要为
`4fc70ab53c4c1282747fcaec3614e6890ca09d20f46cd8a35a03393a37cfba17`。

`team_2v2/Standard` 存在两个确定性的极端长局，分别在 21445 和 34619 次选择正常结束。它们在 10 万预算定向复现中持续推进且正常产生胜者，不是状态循环或死锁；因此正式安全上限固定为 50000。该上限只约束整局模拟，不改变困难 AI 的固定搜索节点预算。

原始汇总位于 `Build/SimulationAcceptance/formal-20260730-071720/summary.json`，12 个分片 JSON 和标准输出位于同一目录。`Build/` 是本机验收产物，不进入源码提交。

## 武将覆盖

| 武将 ID | 出场次数 |
| --- | ---: |
| `chenchen` | 2505 |
| `funingna` | 3255 |
| `hanfeiyang` | 3255 |
| `huanglubaiquan` | 2505 |
| `jiefeng` | 3249 |
| `jifenxin` | 6249 |
| `leixi` | 2499 |
| `lishengming` | 3249 |
| `luoxingye` | 3246 |
| `shenyuebai` | 2496 |
| `sujinglan` | 3246 |
| `xingjianya` | 3246 |

所有武将远高于“至少 20 局固定种子对局”门槛。

## Debug/Release 确定性快检

Debug 与 Release 各运行 240 局（4 模式 × 3 难度 × 20 局），两者结果一致：

- 完成：`240/240`
- fault：`0`
- 拒绝选择：`0`
- 最大决策数：`1928`
- 相同摘要：`9280877bc5e76566a7569a2d9f90a54bc717e43f35e7ac7df68e98cf62ef1ad0`
- 12 名武将最少出场：`45`

原始报告：`Build/SimulationAcceptance/debug-20.json` 与
`Build/SimulationAcceptance/release-20.json`。

## 复现命令

```powershell
& ./Scripts/Test/RunSimulationAcceptance.ps1 `
  -MatchesPerCombination 1000 `
  -ParallelismPerShard 2 `
  -MaximumDecisions 50000
```

只重新汇总已完成分片：

```powershell
& ./Scripts/Test/RunSimulationAcceptance.ps1 `
  -MatchesPerCombination 1000 `
  -MaximumDecisions 50000 `
  -ExistingRunRoot ./Build/SimulationAcceptance/formal-20260730-071720 `
  -NoBuild
```
