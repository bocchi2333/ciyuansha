# 《次元杀》本机音画设置

## 范围

`GameUserSettings` 只保存当前电脑的表现偏好，不进入 `NetworkPlayCommand`、`MatchStateSnapshot` 或规则结算。大厅和局内共用 `PresentationSettingsPanel`，右上角“音画”按钮与 `F10` 均可打开，`Esc` 只负责关闭已经打开的面板。

## 持久化

- 自动加载节点：`/root/GameUserSettings`。
- 配置文件：`user://ciyuansha_settings.cfg`。
- 音效开关、0-100 音量、战斗特效、屏幕闪光、低动态模式和特效质量会即时生效。
- “保存并关闭”、质量切换、音量拖动结束和恢复默认会写入本机配置。
- 0% 音量是硬静音，不继续创建或播放极低音量声道。

## 质量档

- 低：最多 12 个临时视觉节点、3 个音效声道，省略烟雾、火花和额外冲击层。
- 标准：最多 28 个临时视觉节点、5 个音效声道，作为默认推荐档。
- 高：最多 48 个临时视觉节点、7 个音效声道，并提高治疗微粒等装饰密度。
- 同类音效有 40-75 毫秒防叠间隔，连续结算不会无限叠加同一声音。

## 可访问性

- 关闭屏幕闪光后，保留目标局部特效和中央结算文字。
- 关闭战斗特效后，保留规则文字和战斗日志，不影响音效开关。
- 低动态模式缩短位移、缩放和淡入淡出，取消 HUD 墨痕扫动，但不隐藏结算结果。

## 验证

配置往返烟测：

```powershell
Godot_v4.6.2-stable_mono_win64_console.exe --headless --path E:\次元杀 res://Scenes/Test/UserSettingsSmokeTest.tscn
```

弹窗自动截图：

```powershell
Godot_v4.6.2-stable_mono_win64_console.exe --path E:\次元杀 --resolution 1366x768 -- --open-settings --capture-settings=E:/次元杀/Build/VisualChecks/settings.png --auto-quit=true
```

`Build/.gdignore` 保证截图、日志和其他校准产物不会被 Godot 当作正式资源导入。
