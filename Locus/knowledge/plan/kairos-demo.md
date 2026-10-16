---
id: kd_6142a95d-a5e3-477a-ad8d-84a1f0f85935
injectMode: inherit
injectAgents:
- unity
aiEditMode: inherit
---

# KAIROS Demo 执行计划

## 实现依据与范围
- 规则以 `docs/KAIROS_规则规格书.md` v0.2 为准，卡牌来自 `docs/data/cards.json`，不修改原始规划。
- 保留现有 Unity 2022.3.62f3；独立纯 C# Engine、AI 与 Unity Client 程序集。
- PVP：本地热座，双预组，调度、时轴、目标选择、抢拍、伏笔、咏唱、钟主、结算。
- PVE：同引擎 AI 练习；逆流之塔短流程（战斗、三选一奖励、休整、Boss、通关/失败），非完整三幕。
- 不含联网/账号/天梯、正式美术、ISMCTS、大规模平衡调参、扩展机制与完整剧情。

## 检查点
1. [x] 阅读规则、卡池、世界观，检查工程（仅默认场景；不是 Git 仓库）。
2. [x] 数据加载校验、确定性规则引擎与双预组。
3. [x] 热座界面、隐藏信息交接、时轴预览。
4. [x] AI 与 PVE 短流程。
5. [x] 自动化规则测试、Unity 编译/运行检查、截图与运行说明。

## 验证策略
- 用规则编号对应核心边界测试；测试非法命令不改变状态、暂停选择可克隆、确定性回放与多种子自动对局。
- Unity EditMode 测试和 PlayMode 冒烟；实际查看中文字体、交互布局与控制台。
- 交付说明明确已实现与后续扩展，不把基础 PVE 占位敌人等同于文档完整 Boss 机制。

## 已验证结果与交付
- 47/47 .NET 与 Unity EditMode 测试通过；50 种子随机合法对局、100 局 AI/回放一致性通过；demo-0 Unity/.NET 状态哈希一致。
- Play Mode 实际测试 UI 调度、出牌与隐私交接；PVE 对局/失败流程自然运行，奖励/休整/通关用临时胜利夹具验证。
- Windows x64 开发构建成功，`Builds/KairosDemo/KairosDemo.exe`；D3D11 独立播放器启动检查无异常。
- 场景 `Assets/Kairos/Scenes/KairosDemo.unity`；说明 `docs/DEMO_README.md`，包含完整范围与局限。
- 平衡尚未验收（固定贪心 AI 对阵，破晓星轨 15/100 胜）；未实现联网、完整 PVE 专属机制、正式美术和扩展规则。
