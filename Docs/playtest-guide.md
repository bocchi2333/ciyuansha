# 次元杀局域网试玩说明

## 当前可测范围

- 2 人局域网开房、加入、选将、准备、开始对局。
- 出牌阶段使用基础牌、部分锦囊和装备。
- 响应窗口：闪、桃、杀、无懈可击等基础响应链。
- 弃牌阶段、回合轮转、濒死救援、身份胜负。
- 对局结束后，主机可点击“返回候战大厅”，双方重新准备后开始下一局。

## 生成朋友测试包

在项目根目录双击：

```text
BuildPlaytestPackage.bat
```

生成结果：

- `E:\CiyuanSha_PlaytestBuild\CiyuanSha_Playtest`：可直接发送的测试文件夹。
- `E:\CiyuanSha_PlaytestBuild\CiyuanSha_Playtest.zip`：压缩包，适合发给朋友。

2026-07-28 当前推荐发送版本：

- `E:\CiyuanSha_PlaytestBuild\CiyuanSha_Playtest_2026-07-28_InkUI.zip`

朋友解压后，从包内双击 `JoinPlaytest.bat` 加入主机。
主机也可以从包内双击 `HostPlaytest.bat` 开房。

注意：这是开发试玩包，里面包含 Godot Mono 和项目文件，体积会比较大；正式独立 exe 后续再做。

## 本机双开测试

自动双开并自动开房/加入/准备/开始：

在 PowerShell 中运行：

```powershell
powershell -ExecutionPolicy Bypass -File "E:\次元杀\Scripts\Test\StartLocalVisualPlaytest.ps1"
```

如需换端口：

```powershell
powershell -ExecutionPolicy Bypass -File "E:\次元杀\Scripts\Test\StartLocalVisualPlaytest.ps1" -Port 24568
```

如果想手动点击大厅流程：

```powershell
powershell -ExecutionPolicy Bypass -File "E:\次元杀\Scripts\Test\StartLocalVisualPlaytest.ps1" -Manual
```

操作流程：

1. 自动模式下，两个窗口会自动进入对局。
2. 手动模式下，第一个窗口输入名字后点击“创建房间”。
3. 手动模式下，第二个窗口地址保持 `127.0.0.1`，点击“加入房间”。
4. 手动模式下，双方选择武将，客户端点击“准备迎战”。
5. 全员就绪后，主机点击“开始对局”。
6. 打完后主机点击“返回候战大厅”，双方回到大厅。

## 快速启动参数

Godot 启动参数支持：

- `--lan-role=host`：启动后自动开房。
- `--lan-role=client`：启动后自动加入。
- `--address=127.0.0.1`：客户端加入的主机地址。
- `--port=24567`：开房或加入使用的 UDP 端口。
- `--player-name=Host`：设置玩家名。
- `--general=guardian`：设置武将 id。
- `--auto-ready=true`：进入大厅后自动 Ready。
- `--auto-start=true`：主机在所有人 Ready 后自动开始。

## 两台电脑局域网测试

最简单流程：

1. 主机双击 `ShowLanInfo.bat`，把显示出来的 LAN IP 告诉朋友。
2. 主机双击 `HostPlaytest.bat`，输入自己的名字并回车。
3. 朋友双击 `JoinPlaytest.bat`，输入主机 LAN IP，再输入自己的名字。
4. 自动模式下，双方会自动准备，主机在全员就绪后自动开始。
5. 打完后主机点击“返回候战大厅”，双方回到大厅。

手动流程：

1. 主机运行游戏，点击“创建房间”。
2. 主机大厅会显示本机房间地址和端口，把这个 IP 告诉朋友。
3. 朋友运行游戏，在地址栏填主机 IP，点击“加入房间”。
4. 双方选将并准备，主机开始对局。
5. 如果地址栏写成 `192.168.1.23:24568`，客户端会使用 IP `192.168.1.23` 和端口 `24568`。
6. 如果连接失败，优先检查 Windows 防火墙是否允许 Godot 使用 UDP 端口 `24567`。

## 防火墙准备

主机电脑可以用管理员 PowerShell 运行：

```powershell
powershell -ExecutionPolicy Bypass -File "E:\次元杀\Scripts\Test\PrepareLanFirewall.ps1"
```

或者在项目根目录右键管理员运行：

```text
PrepareFirewall.bat
```

如需换端口：

```powershell
powershell -ExecutionPolicy Bypass -File "E:\次元杀\Scripts\Test\PrepareLanFirewall.ps1" -Port 24568
```

排查顺序：

1. 两台电脑必须在同一个局域网或同一个热点下。
2. 客户端地址填主机大厅显示的 `LAN IP`，不是 `127.0.0.1`。
3. 端口要和主机一致，默认 `24567`。
4. 主机 Windows 网络配置尽量选择“专用网络”，不要是“公用网络”。
5. 如果仍失败，先临时关闭主机防火墙测试是否是拦截问题，确认后再恢复防火墙并添加规则。

## 反馈时请记录

- 当时处于哪个阶段：大厅、出牌、响应、弃牌、濒死、结算、结束。
- 谁在操作：主机还是客户端。
- UI 显示的等待信息、手牌区状态、目标区状态或响应窗口文案是什么。
- 用了哪张牌、目标是谁、预期是什么、实际发生了什么。
- 如果卡住，请保留 `E:\次元杀\host_*.log` 和 `E:\次元杀\client_*.log`，或截图当前画面。

## 试玩时看哪里

- 大厅底部状态会提示现在该开房、加入、准备、开始，还是等待房主。
- 对局状态栏会显示当前需要谁操作，以及要做什么。
- 手牌区会提示当前能不能出牌、该选牌还是该弃牌。
- 目标区会提示是否需要先选手牌，以及目标按钮为什么不能点。
- 响应窗口只在你需要响应时出现，可以出对应牌，也可以选择跳过。
- 中途与房主失去连接时会弹出错误详情；保持相同玩家名并点击“重新连接”可恢复原席位。

## 已知限制

- 当前已经接入商业化水墨 UI、基础战斗音效与卡牌动效，但仍处于试玩校准阶段。
- 3-4 人虽然已有部分基础，但首轮建议先 2 人试玩，方便定位问题。
- 部分复杂武将技能仍是原型技能，正式武将平衡后续再统一打磨。
