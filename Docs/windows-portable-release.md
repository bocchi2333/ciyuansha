# Windows 自包含便携版

正式便携包通过 `Scripts/Release/BuildWindowsPortable.ps1` 生成。玩家端不需要 Godot、.NET Runtime 或 .NET SDK。

## 前置条件

- Windows x64。
- Godot 4.6.2 stable Mono 编辑器。
- Godot 4.6.2 Mono export templates，安装目录为 `%APPDATA%\Godot\export_templates\4.6.2.stable.mono`。
- 干净的 Git 工作区；发布版本必须能映射到唯一源码提交。

## 生成

```powershell
& ./Scripts/Release/BuildWindowsPortable.ps1
```

默认输出到 `E:\CiyuanSha_ReleaseBuild`。构建过程会：

1. 以 Release 配置编译解决方案并运行 xUnit。
2. 使用 `Windows x64 Portable` 预设导出独立游戏。
3. 复制内容包和 12 张正式映射武将立绘，并检查映射完整性。
4. 检查本地 .NET 8 运行时文件是否齐全。
5. 禁用全局 .NET 查找后启动导出游戏进行烟测，并拒绝含 GameCore 初始化错误的构建。
6. 附带 GPL、第三方声明、无名杀移植台账和对应提交的源码 ZIP。
7. 生成逐文件校验表、便携包 ZIP 及 ZIP 的 SHA-256。

便携包没有商业代码签名，因此 Windows SmartScreen 可能显示“未知发布者”。这不影响自包含运行，但在公开发行前需要采购证书并签名。
