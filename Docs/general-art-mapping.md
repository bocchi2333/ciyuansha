# General Art Mapping

Current art root:

- `E:\次元杀\武将卡牌`

Purpose:

- The source art folder currently uses auto-generated file names.
- `GeneralArtRegistry` resolves `generalId -> image file name`.
- Catalog defaults live in `Scripts/Gameplay/Generals/GeneralCatalog.cs`.
- Local overrides live in `Data/general_art_mapping.json`.

Recognized generals mapped so far:

- `xingjianya` -> `星见雅` -> `IMG_20260423_125518.png`
- `huanglubaiquan` -> `黄露白泉` -> `IMG_20260423_161312.png`
- `lishengming` -> `李圣鸣` -> `mmexport1777169744471.jpg`
- `chenchen` -> `陈晨` -> `file_000000000f78720981f51191eb4b20cc.png`
- `hanfeiyang` -> `韩飞扬` -> `file_0000000017b07209a00bb1ec28212d8e.png`
- `funingna` -> `芙宁娜` -> `file_000000003cdc720992d0d8f04661fe90.png`
- `jiefeng` -> `捷风` -> `file_000000005864720998ce9b8a58e8e031.png`

Operational notes:

- `PlayerCharacter.LoadGeneralCardTexture()` uses `GeneralArtRegistry.ResolveImagePath(...)`.
- `GeneralArtMapperPanel.tscn` can be opened in Godot to inspect images and save overrides.
- If a general has no override, the registry falls back to the default art file from `GeneralCatalog`.
- Unknown or newly added image files stay available in the mapper even before a general entry exists.
