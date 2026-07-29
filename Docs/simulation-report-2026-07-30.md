# GameCore V2 固定种子模拟验收报告（2026-07-30）

## 正式长跑

本地单命令外层有 60 分钟壁钟，因此按模式/难度拆成 12 个独立分片。每个分片使用相同的固定种子公式、内容包和
20000 决策上限；拆分只改变任务调度，不改变对局输入。例如：

```powershell
dotnet run --project CiyuanSha.GameCore.Simulations/CiyuanSha.GameCore.Simulations.csproj -c Release --no-build -- --matches 1000 --mode identity --difficulty Hard --parallelism 10 --max-decisions 20000
```

| 模式 | 难度 | 完成 | fault | 拒绝选择 | 最大决策数 |
| --- | --- | ---: | ---: | ---: | ---: |
| `duel` | Easy | 1000 | 0 | 0 | 285 |
| `duel` | Standard | 1000 | 0 | 0 | 2066 |
| `duel` | Hard | 1000 | 0 | 0 | 1154 |
| `identity` | Easy | 1000 | 0 | 0 | 483 |
| `identity` | Standard | 1000 | 0 | 0 | 7132 |
| `identity` | Hard | 1000 | 0 | 0 | 5566 |
| `team_2v2` | Easy | 1000 | 0 | 0 | 540 |
| `team_2v2` | Standard | 1000 | 0 | 0 | 17598 |
| `team_2v2` | Hard | 1000 | 0 | 0 | 18778 |
| `boss_pve` | Easy | 1000 | 0 | 0 | 549 |
| `boss_pve` | Standard | 1000 | 0 | 0 | 2167 |
| `boss_pve` | Hard | 1000 | 0 | 0 | 1778 |

汇总：12000/12000 正常结束，`Faulted=0`，`RejectedChoices=0`。所有组合均未达到 20000 决策上限。

分片确定性摘要：

| 组合 | SHA-256 摘要 |
| --- | --- |
| `duel/Easy` | `0a8a1ac05b88f2acb192a3073fe38ea84a18290499d5cb2ff88b4bee37cc06e3` |
| `duel/Standard` | `f9f024fd04385f716b0445a815fe09485b62b46dbb37846374b5a2e21d32200c` |
| `duel/Hard` | `d4db849fd22fa5f974068483aeb8c412a324548cb911b6e72b7e475cd7e0abd3` |
| `identity/Easy` | `d683aa0900536c05d767b280931916faea26b87401d181f9999dd932c1f420f9` |
| `identity/Standard` | `f0c13db0a886bc9f1fac1619fd2cd3929a63e16a8c951d0f75a23d6aaa2c74cd` |
| `identity/Hard` | `c5da844a7d46fc3bcdb21b1361b8c895a2313a185dbf6a24589b376e18fdd104` |
| `team_2v2/Easy` | `7966d8970e6b6e1c770b04e4ffbce684676a84369ce34d597d629443dfee8a38` |
| `team_2v2/Standard` | `2b72609bdc8f73b7073e13a42240ad6a18e141becaeb39faa2b5ae1435d20829` |
| `team_2v2/Hard` | `64b357a231207102e4233b3a199671432182995088c87dd8320b3d19f679db8f` |
| `boss_pve/Easy` | `0c22ec722ab37de61b0912374b46432ba72a37db7f20424c798f1a2c33ac9010` |
| `boss_pve/Standard` | `65cffca65ebc54810ba48501b8f8636715f03458747a784423c2d8a1cf372c87` |
| `boss_pve/Hard` | `3146f3f0f263ef7f46110cf0f419a9f35ed8233c14c7be6f1b296d04be818db4` |

`team_2v2/Standard` 和 `team_2v2/Hard` 的尾部长局分别达到 17598 和 18778 决策，仍在 20000 固定预算内。
后续增加卡包时必须继续观察这两个组合；不能用墙钟截断来隐藏长局。

## 快速确定性矩阵

在 Debug 和 Release 下分别运行 4 模式 × 3 难度 × 20 局，两次结果一致：

- 每次共 240 局
- 正常结束：240
- 异常：0
- 被内核拒绝的 AI 选择：0
- 单局最大决策数：4774
- Debug/Release 确定性摘要：`4ea0f401402c567c6ab62deeff038a7f90c029f4771dd74e858c6bc7117b2225`

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
