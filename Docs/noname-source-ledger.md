# 无名杀源码学习与移植台账

本工程整体采用 `GPL-3.0-only`。无名杀参考仓库固定为
`https://github.com/libnoname/noname`，审阅提交为
`444c7278a6b4b4754eaad4299f77ab31ba8d1655`。

| 上游文件 | 本地子系统 | 使用方式 |
| --- | --- | --- |
| `apps/core/noname/library/element/gameEvent.ts` | `CiyuanSha.GameCore/Events` | 将父子事件、next/after 队列、Before/Begin/End/After 生命周期和确定性触发顺序重写为纯 C# |
| `docs/lib-skill-format.md` | `CiyuanSha.GameCore/Skills` | 采用 trigger/filter/check/cost/content、主动/锁定/限定/转换、标记、临时状态及子技能等声明语义 |
| `apps/core/noname/library/element/player.js` | `CiyuanSha.GameCore/Choices` | 将 chooseCard/chooseTarget/chooseControl/chooseBool 等交互抽象为统一 ChoiceRequest/ChoiceResult |
| `apps/core/card/standard.js` | 标准卡牌包 | 对照标准牌、装备、花色点数和规则说明 |
| `apps/core/card/extra.js` | 军争卡牌包 | 对照属性杀、酒、铁索连环、火攻及军争装备 |
| `apps/core/game/package.js` | `CiyuanSha.GameCore/Content` | 采用显式内容包清单、依赖、版本和哈希 |
| `apps/core/noname/ai/basic.js` | `CiyuanSha.GameCore/AI` | 参考合法候选估值，使用固定种子和固定节点预算独立实现三级 AI |
| `apps/core/noname/game/index.js` | `CiyuanSha.GameCore/Replay` | 参考录像日志概念，改为记录权威规则事件和状态哈希 |

上述本地实现均为按公开机制和接口语义重新设计的 C# 实现，没有逐行复制 JavaScript。若以后出现直接翻译或实质改写，必须新增一行，
记录上游文件、具体提交、原作者信息、本地文件和修改说明。

## 边界

- 不嵌入无名杀 JavaScript 运行时，不执行任意扩展脚本。
- 不复制无名杀角色美术、音频、翻译文本或其他来源不明确资源。
- 每次新增直接翻译或实质改写的上游算法，都必须在本表追加来源与本地位置。
- 发布构建必须携带 `LICENSE`、`NOTICE`、`THIRD_PARTY_NOTICES` 与对应源码。
