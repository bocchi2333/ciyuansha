# GameCore V2 固定种子模拟验收报告（2026-07-30）

## 正式长跑

本地单命令外层有 60 分钟壁钟，因此按模式/难度拆成 12 个独立分片。每个分片使用相同的固定种子公式、内容包和
20000 决策上限；拆分只改变任务调度，不改变对局输入。例如：

```powershell
dotnet run --project CiyuanSha.GameCore.Simulations/CiyuanSha.GameCore.Simulations.csproj -c Release --no-build -- --matches 1000 --mode identity --difficulty Hard --parallelism 10 --max-decisions 20000
```

| 模式 | 难度 | 完成 | fault | 拒绝选择 | 最大决策数 |
| --- | --- | ---: | ---: | ---: | ---: |
| `duel` | Easy | 1000 | 0 | 0 | 242 |
| `duel` | Standard | 1000 | 0 | 0 | 577 |
| `duel` | Hard | 1000 | 0 | 0 | 523 |
| `identity` | Easy | 1000 | 0 | 0 | 419 |
| `identity` | Standard | 1000 | 0 | 0 | 2578 |
| `identity` | Hard | 1000 | 0 | 0 | 7217 |
| `team_2v2` | Easy | 1000 | 0 | 0 | 399 |
| `team_2v2` | Standard | 1000 | 0 | 0 | 3904 |
| `team_2v2` | Hard | 1000 | 0 | 0 | 12819 |
| `boss_pve` | Easy | 1000 | 0 | 0 | 280 |
| `boss_pve` | Standard | 1000 | 0 | 0 | 709 |
| `boss_pve` | Hard | 1000 | 0 | 0 | 760 |

汇总：12000/12000 正常结束，`Faulted=0`，`RejectedChoices=0`。所有组合均未达到 20000 决策上限。

`identity/Hard` 索引 346 曾被 5000 决策诊断线捕获；用正式预算按种子 `3249688794` 单独重放，
在 7217 决策正常结束，随后整个分片用正式预算通过。这证明它是长局，不是死锁。

## 快速确定性矩阵

在 Debug 和 Release 下分别运行 4 模式 × 3 难度 × 20 局，两次结果一致：

- 每次共 240 局
- 正常结束：240
- 异常：0
- 被内核拒绝的 AI 选择：0
- 单局最大决策数：3453
- 确定性摘要：`0b5427a917bc959bdc25d50770341e41132de55e515e6c0b76e5264ce60021de`

| 武将 ID | 出场次数 |
| --- | ---: |
| `chenchen` | 54 |
| `funingna` | 69 |
| `hanfeiyang` | 69 |
| `huanglubaiquan` | 54 |
| `jiefeng` | 66 |
| `jifenxin` | 126 |
| `leixi` | 51 |
| `lishengming` | 66 |
| `luoxingye` | 60 |
| `shenyuebai` | 45 |
| `sujinglan` | 60 |
| `xingjianya` | 60 |

12 名武将均在快速矩阵中出场不少于 45 次；正式分片按武将 ID 轮转，每个非 Boss 组合中每名约出场 166–334 次。
执行和复现参数见 [`testing-gamecore-v2.md`](testing-gamecore-v2.md)。
