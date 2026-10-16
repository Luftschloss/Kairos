# KAIROS 数学平衡报告

- 卡池：`0.1.0`；源数据 SHA256：`3496149a2e63059b6af38f1c65097fda5682912c4849d382b31463e476d2928f`
- 模型 SHA256：`0a261958a3b38f474a3b60ac94ae6502362278d4def49a9605538a73001f1769`；候选 SHA256：`无`
- 方法：精确超几何概率、确定性战斗断点、时间状态枚举、条件化敏感性分析。**没有 AI，也没有估算胜率。**
- CSV 是完整明细；本页为摘要。所有存活率、可用率都是输入假设，不是实测事实。

## 构筑与起手

|构筑|侍从/30|常规可部署|≤初始视界的常规侍从|守卫|蓄力|先手5张无早期侍从|后手6张无早期侍从|
|---|---:|---:|---:|---:|---:|---:|---:|
|破晓星轨|12|10|8|0|0|18.48%|12.57%|
|烈阳琥珀|20|20|10|10|6|10.88%|6.53%|

## 学派覆盖（不能当作职业强度排名）

|学派|种数|侍从种数|早期常规侍从种数|守卫种数|蓄力种数|
|---|---:|---:|---:|---:|---:|
|NEUTRAL|6|4|2|1|0|
|DAWN|8|4|2|0|0|
|NOON|8|4|2|3|0|
|DUSK|8|5|2|0|0|
|NIGHT|8|2|1|0|0|
|ETERNAL|8|4|2|2|3|

## 侍从交换压力（非对局胜率）

假设双方侍从均存活至指定鸣钟次数、满血；应用各自钟主固定入场属性，不应用条件技能、部署、守卫目标限制或费用光环。按牌组复制数加权。分母是所有跨牌组侍从配对，不是实际遇敌分布。

|攻击构筑→防守构筑|已存活鸣钟|防守有盾|一击击杀占比|击杀且存活占比|
|---|---:|---|---:|---:|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|0|False|61.67%|53.33%|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|0|True|0.00%|0.00%|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|1|False|70.00%|61.67%|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|1|True|0.00%|0.00%|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|3|False|78.33%|70.00%|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|3|True|0.00%|0.00%|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|5|False|78.33%|70.00%|
|PRECON_NOON_ETERNAL → PRECON_DAWN_NIGHT|5|True|0.00%|0.00%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|0|False|21.67%|13.33%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|0|True|0.00%|0.00%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|1|False|15.00%|6.67%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|1|True|0.00%|0.00%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|3|False|13.33%|5.00%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|3|True|0.00%|0.00%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|5|False|13.33%|5.00%|
|PRECON_DAWN_NIGHT → PRECON_NOON_ETERNAL|5|True|0.00%|0.00%|

## 局部概率试验（尚未实现）

固定预见1，50%再看1张；只改变可筛选的信息量，不随机化命中、伤害、费用、鸣钟或守卫。

|方案|期望可看牌数|方差|看到目标牌概率|
|---|---:|---:|---:|
|fixed_base|1.00|0.00|32.00%|
|bounded_bonus|1.50|0.25|43.33%|
|fixed_high|2.00|0.00|54.67%|

## 边界与复核

- effects.csv 完整列出所有 DSL 分支、被动、光环和自动蓄力；未把复合技能强行折算成攻击力。技能触发由 keepers.csv 做可用率敏感性，战斗结果仍依赖状态与操作者。
- 不包含玩家决策分布、实际行动权争夺、手牌焚毁率、连锁解场、精确整局伤害、收藏价格或 PvE 奖励平衡。
- 比较重构候选与正式基线时使用同一模型。不能仅凭某列接近50%宣称平衡。
- 正式采用候选前须补：引擎语义测试、双人交叉试玩与概率机制实现。研发期按用户要求不做旧存档兼容；本工具仍不删除或修改任何存档。
