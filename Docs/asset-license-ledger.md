# 《次元杀》资产许可台账

> 最后更新：2026-07-28。此表用于工程追溯，不替代正式发行前的法律审核。

## 已纳入工程

| 资产 | 工程路径 | 来源与许可 | 用途 | SHA-256 |
| --- | --- | --- | --- | --- |
| 思源宋体 CN Regular | `Assets/Fonts/SourceHanSerifCN-Regular.otf` | Adobe Source Han Serif，SIL Open Font License 1.1；许可副本见 `Assets/Licenses/SourceHanSerif-OFL-1.1.txt` | 标题、阶段、品牌字 | `3754EA669C530E2473354F8F6D9F79680A44D7E26EC7D00EEABEE4A7E0753C5D` |
| 霞鹜文楷 Medium | `Assets/Fonts/LXGWWenKai-Medium.ttf` | LXGW WenKai，SIL Open Font License 1.1；许可副本见 `Assets/Licenses/LXGWWenKai-OFL-1.1.txt` | 正文、按钮、日志 | `D4BDEB38A39151D74D084CBA5090F8CB7D20BF83EEDB78C35939AE70B9F4E3F6` |
| 夜庭院对局背景 v1 | `Assets/UI/InkBattle/ink_courtyard_match_v1.png` | 2026-07-28 使用 OpenAI 内置图像生成工具按原创提示生成；未引用第三方图片 | 局内 16:9 背景 | `F51DD09052CD4F2FF892500EFFA49DFDD68AA534A4EB86AEC3C571F18ED3ECAD` |
| 山亭大厅背景 v1 | `Assets/UI/InkBattle/ink_mountain_lobby_v1.png` | 2026-07-28 使用 OpenAI 内置图像生成工具按原创提示生成；未引用第三方图片 | 大厅与选将 16:9 背景 | `4F13E58FF6D82A37DB0CC439EDDE3019F477B5A80B9EF661E4008F827D984201` |

## 已授权战斗特效与音效

项目负责人于 2026-07-28 明确确认 `E:\slay the spire` 素材已经获得授权。工程记录见 `Assets/Licenses/UserAuthorizedAssets-2026-07-28.txt`；正式商业发行前仍需把底层授权合同或许可文件归档。转换脚本 `Scripts/Tools/ConvertAuthorizedAssets.gd` 只挂载本机授权资源包并导出下表中的标准 PNG/MP3，不把原始 PCK、`.ctex`、`.mp3str` 或未采用画面放入项目。

### 纹理

| 原始资源键 | 工程路径 | 用途 | SHA-256 |
| --- | --- | --- | --- |
| `base_glow.png-382d5748a9ad57b75c7b5670d96de850.ctex` | `Assets/FX/Textures/base_glow.png` | 通用金光、属性光晕 | `269997626F967521A5F9E7BEC999A11E1C9DA948B8C9AA4F49CB17E25521A18B` |
| `big_slash_impact_smoke_flipbook.png-bf43229d8653b7d0cca2fdabad9ecf6f.ctex` | `Assets/FX/Textures/slash_smoke_flipbook.png` | 物理斩击烟墨，4x2 帧 | `72A23EC9F95B8C4B4A072BE1ADD9E2B7B89C2222654F30166C04EFF2E13189B8` |
| `bloodyimpact_anim_all.png-352801469a8ecc394826592d1efb0ea8.ctex` | `Assets/FX/Textures/bloody_impact_flipbook.png` | 朱红命中轮廓，5x2 帧 | `72D48680909D369C085FD3D6D324F2911F1137245AE4661C168E5DA4E0267978` |
| `common_impact_flare_flipbook.png-c9c99bf8237f76b1a2e4824e479a241d.ctex` | `Assets/FX/Textures/impact_flare_flipbook.png` | 金色/雷紫命中闪光，2x2 帧 | `5D1CAFEF7826A723CA8C050994B90AF4FE71285A1B3E2C7F51C9ECD5D320C434` |
| `common_round_smoke_flipbook.png-08a2bfa70d47a7172675e30ebbec027b.ctex` | `Assets/FX/Textures/round_smoke_flipbook.png` | 开场、火焰与阵亡烟雾，2x2 帧 | `D2B2BDC7E4F74D1341724A284B2C41D30C572FC3C187A36A05F75C647DD537F0` |
| `fire_impact_flipbook.png-59faa29304a0d4611b6a5b8f88ce04d0.ctex` | `Assets/FX/Textures/fire_impact_flipbook.png` | 火焰伤害，3x2 帧 | `50FFBB50F02929EBA1DCCD06D9D5FBDB973392961DD755CE84B00233805B8170` |
| `ground_spark.png-491a72c9af47967e6ee82964648cbc36.ctex` | `Assets/FX/Textures/ground_spark.png` | 物理伤害火星 | `55EDE2E7D5218CADA0A6054A6D95FBACF5A0E16630530CDA1FF5816CFB58C6AC` |
| `intent_heal.png-ec05ca102c161b71d91ef8aae48d5647.ctex` | `Assets/FX/Textures/intent_heal.png` | 治疗标识 | `26FE175442542E594D801F14223381E1E8B8FBF10B0BDB4E3B4618D4601BC6D3` |
| `lightning_flipbook_1.png-5cea12987abd1ffdb8c1e6421c7fa55e.ctex` | `Assets/FX/Textures/lightning_flipbook.png` | 雷电伤害，3x3 帧 | `860FA2D21408A762F4AF0EB75D7E1BE489ACA7B63FFE6216F12CB98FC3728B71` |

### 音效

| 原始资源键 | 工程路径 | 用途 | SHA-256 |
| --- | --- | --- | --- |
| `battle_start_1.mp3-9b591fdbcfa11858ece735905b44d601.mp3str` | `Assets/FX/Audio/battle_start.mp3` | 对局开场 | `C1820D7448F98553717D610807112928CA3FC1B07388D66AB6E74EC90C76522A` |
| `card_deal.mp3-c0981c796fa9900913006b74f202f30e.mp3str` | `Assets/FX/Audio/card_deal.mp3` | 摸牌、装备、判定 | `2EB835973E1248628224CF94820FA290E8CA98F868CE642A9D976C820F3D859E` |
| `card_select.mp3-1dd5f2cdd037cf3bc133d85c63c302eb.mp3str` | `Assets/FX/Audio/card_select.mp3` | 出牌、响应、弃牌 | `190AA6B92C6F5C293E3055552BA613064428D23FEACEE19E6CD237E88F0BCE28` |
| `dark_orb_evoke.mp3-6dbc753003fb2b1ce7a283758feb9bfb.mp3str` | `Assets/FX/Audio/physical_impact.mp3` | 物理伤害 | `585443E22A905046510CB670F385D9FBD9B37E4C5E4A06C372798FE8DADDF736` |
| `gain_potion.mp3-887e2efd2a1d28220c4f5d929337f717.mp3str` | `Assets/FX/Audio/heal.mp3` | 治疗与救援 | `84D06C877F39FE0440426EE3593B20CE88A4EC7BFBFCA473D0378F4561D9463A` |
| `lightning_orb_evoke.mp3-e6bd5a1f285f34a67d7ed8cf982a73f6.mp3str` | `Assets/FX/Audio/thunder.mp3` | 雷电伤害 | `124F2CAC238CDC810E746E78F7BA324B5AAFD3DBAFDADEF6E770EB125CC53BFF` |
| `STS_DeathStinger_v4_Short_SFX.mp3-a76f07e7a07023da43d677d5c65c496c.mp3str` | `Assets/FX/Audio/defeat.mp3` | 武将阵亡 | `C77D218DEA875BAFBF84F1CE2D9DE13EB8B503E71674E1EABEAAB4BF087B34E3` |
| `STS_SFX_BurnCard_v1.mp3-9476eba50d3f88ff7f3e6dde92b55f57.mp3str` | `Assets/FX/Audio/fire.mp3` | 火焰伤害 | `444B17BA82A0F5C17D6670C0C8762F7A275E8412D9ED02EC8101DD588E9A6DB6` |

## 原创生成提示摘要

- 对局背景：原创 16:9 暗色水墨武侠夜庭院，中央湿润石质圆形牌桌舞台，远山、檐廊、灯笼与低雾；边缘压暗、中央可读；无人物、卡牌、文字、徽标和现成 IP 元素。
- 大厅背景：原创 16:9 山亭与远处城寨夜景，层叠群山、云雾、暖色灯火和开阔中景；为三栏大厅留出安静区域；无人物、文字、徽标和现成 IP 元素。

## 用户确认与待归档资产

| 资产范围 | 当前状态 | 发布前要求 |
| --- | --- | --- |
| `E:\次元杀\武将卡牌\*` | 项目负责人于 2026-07-28 确认图片均由 AI 生成 | 商业发行前归档所用模型/平台条款、提示来源与必要的人物权利检查；继续逐张校对敏感标识和近似 IP 风险 |
| `E:\slay the spire\*` | 项目负责人于 2026-07-28 确认素材已获得授权；本轮只纳入上表选定资源 | 商业发行前归档底层授权合同、范围、期限、署名、修改和再分发条款；不得把未登记资源自动并入构建 |
| 网络开源纹理/动效 | 本轮未下载 | 只接受明确 SPDX/许可证文本、原始项目链接和可再分发条款完整的资源 |

## 字体上游

- 思源宋体：`https://github.com/adobe-fonts/source-han-serif`
- 霞鹜文楷：`https://github.com/lxgw/LxgwWenKai`

## 发布检查

- 构建前核对本表中的文件哈希和许可副本。
- 将 OFL 文本随正式发行包一并保留。
- 构建内只保留台账列出的标准 PNG/MP3；转换源缓存和资源包不得进入发行包。
- 对武将图、图标、音效、音乐和生成式资产分别完成平台条款与地区合规复核。
