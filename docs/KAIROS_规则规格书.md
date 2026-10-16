# 《KAIROS · 契机》规则规格书（开发用）v0.2

> 本文档是**实现依据**。如果与《KAIROS_契机_卡牌游戏原型设计.md》（概述文档）不一致，以本文档为准。
> 卡牌数据见 `docs/data/cards.json`，世界观见《KAIROS_世界观与叙事.md》。
>
> 规则编号格式为 `R-章节-序号`。代码注释和测试用例中引用规则编号，方便追溯。
>
> **技术栈：Unity 6 LTS + C#。** 文中代码都是 C# 参考实现，用来说明规则的精确语义。命名和细节可以调整，但语义必须一致。
>
> v0.2 变更：代码从 TypeScript 改为 C#；新增第 0.3 节（Unity 工程约定）；常量改为可配置的 `RulesConfig`；明确只有顶层结算可以暂停等待选择（R-9-4）；新增 R-13-1a（召回不触发部署）；新增 C# 测试示例。

---

## 0. 实现总览

### 0.1 架构原则

1. **纯 C# 规则引擎**：`Apply(state, command) → events`，不引用 `UnityEngine`，不依赖 UI，也不依赖网络。同一份源码供 Unity 客户端、.NET 模拟器和 .NET 服务器共用。
2. **确定性**：所有随机都来自带种子的 `KairosRng`。`回放 = 种子 + 规则配置 + 命令序列`。
3. **数据驱动**：卡牌效果用 JSON DSL 描述（第 15 节）。引擎只实现**操作（op）注册表**，不为单张卡写特殊代码。少数特例集中放在 `Effects/Special/` 目录，并且必须注明原因。
4. **只在顶层可暂停**：需要玩家选择的效果（预见、从往昔召回等）只能出现在行动的顶层结算中。引擎进入 `Pending` 状态，等待 `ChooseCommand` 后继续（R-9-4）。
5. **视图裁剪**：服务器保存完整状态；发给每个客户端的是按该玩家视角裁剪后的视图（第 16 节）。

### 0.2 工程目录结构（Unity 6 LTS）

```
KAIROS/                                   # Unity 项目根目录
  Assets/
    Kairos/
      Engine/                             # 纯 C# 规则引擎
        Kairos.Engine.asmdef              # "noEngineReferences": true，禁止引用 UnityEngine
        Core/        RulesConfig.cs  Types.cs  GameState.cs  KairosRng.cs  Commands.cs  Events.cs
        Setup/       GameSetup.cs  DeckValidator.cs
        Flow/        TurnOrder.cs  LegalCommands.cs  CostCalculator.cs  ActionPipeline.cs  InterjectWindow.cs
        Timeline/    TimelineService.cs  ChimeService.cs
        Combat/      CombatService.cs
        Effects/     TriggerQueue.cs  EffectContext.cs  Selectors.cs  Conditions.cs
                     Ops/*.cs（每个 op 一个类）  Special/
        Rules/       Checkpoint.cs
        Data/        CardDefinition.cs  CardDatabase.cs  CardValidator.cs
        View/        PlayerView.cs  ViewRedactor.cs  Preview.cs
        Replay/      ReplayFile.cs
      Ai/                                 # Kairos.Ai.asmdef，只依赖 Engine
        RandomAi.cs  GreedyAi.cs  Ismcts.cs  Evaluator.cs
      Client/                             # Kairos.Client.asmdef，依赖 Engine 和 UnityEngine
        Bootstrap/   GameController.cs（MonoBehaviour：持有 GameState，转发命令）
        Presentation/EventPlayer.cs（按事件队列播放动画）
        UI/          TimelineView.cs  HandView.cs  BoardView.cs  GhostPreviewView.cs  InterjectPrompt.cs
        Config/      RulesConfigAsset.cs（ScriptableObject，包装 RulesConfig）
      Data/
        cards.json                        # TextAsset，由 docs/data/cards.json 同步
    Tests/
      EditMode/
        Kairos.Engine.Tests.asmdef        # Unity Test Framework（NUnit），第 20 节用例
        Rules/*.cs
  Tools/                                  # Unity 之外的 .NET 8 工程，以链接方式引用 Engine 源码
    Kairos.Sim/                           # 批量自我对弈和参数扫描（第 21 节）
    Kairos.Tests/                         # 用 dotnet test 跑同一套 NUnit 用例（适合 CI）
    Kairos.Server/                        # 权威服务器（可选，第 17 节）
```

`Tools/*.csproj` 引用引擎源码的写法：

```xml
<ItemGroup>
  <Compile Include="../../Assets/Kairos/Engine/**/*.cs" LinkBase="Engine" />
  <PackageReference Include="Newtonsoft.Json" Version="13.*" />
</ItemGroup>
```

### 0.3 Unity / C# 工程约定（强制）

| 约定 | 原因 |
| --- | --- |
| Engine 程序集设置 `noEngineReferences: true` | 保证引擎能在 .NET 8 中运行，用于模拟器、服务器和 CI |
| 语言版本按 Unity 支持的 **C# 9** 编写；不使用 `record` 和 `init`（Unity 缺少 `IsExternalInit`） | 兼容 Unity 编译器 |
| 规则计算**只用 `int`**，禁止 `float` 和 `double` | 跨平台确定性 |
| 禁止使用 `System.Random`、`UnityEngine.Random`、`DateTime`、`Guid`；统一使用 `KairosRng`（xoshiro128**） | 回放与服务器校验的一致性 |
| 需要遍历的集合使用 `List<T>` 或排序后的结构；**不依赖 `Dictionary` 和 `HashSet` 的遍历顺序**；关键词用 `[Flags] enum` | 确定性 |
| 实体 ID 由 `GameState.NextId++` 分配 | 确定性、可回放 |
| JSON 使用 `com.unity.nuget.newtonsoft-json` | `JsonUtility` 不支持多态和字典 |
| Engine 内禁止 LINQ 出现在 AI 热路径（`LegalCommands`、`Clone`、`Apply`） | 降低 ISMCTS 的 GC 压力 |
| `GameState` 提供手写的深拷贝 `Clone()`，不用序列化来克隆 | 性能 |
| 引擎抛出 `RulesViolationException` 时附带规则编号 | 方便定位 |

---

## 1. 规则配置（`Core/RulesConfig.cs`）

所有数值都是可调参数，改动需要经过模拟器验证（第 21 节）。为了支持参数扫描，常量**不写成 `const`**，而是作为 `RulesConfig` 实例挂在 `GameState.Config` 上。Unity 中用 `RulesConfigAsset`（ScriptableObject）包装，模拟器用命令行参数覆盖。

```csharp
[Serializable]
public sealed class RulesConfig
{
    public int TimelineEnd = 64;            // 时轴终点
    public int BarLength = 4;               // 钟点线位于 BarLength 的倍数
    public int LifeStart = 20;
    public int LifeMax = 20;
    public int DeckSize = 30;
    public int CopyLimit = 2;
    public int LegendaryLimit = 1;
    public int MaxSchools = 2;              // 必须包含钟主所属学派；中立卡不计入
    public int HandStart = 5;
    public int SecondPlayerBonusCards = 1;
    public int HandMax = 8;                 // 手牌已满时抽到的牌直接焚毁
    public int BoardMax = 5;
    public int SigilMax = 3;
    public int ChannelMax = 2;
    public int OmenMax = 3;                 // 可被效果修改
    public int OmenSetCost = 2;
    public int OmenOffsetMin = 1;           // 相对于“支付后时标”
    public int OmenOffsetMax = 6;
    public int FocusCost = 2;
    public int FocusPerBar = 1;
    public int HorizonBase = 2;             // 视界 = min(HorizonMax, HorizonBase + 已鸣钟次数)
    public int HorizonMax = 7;
    public int InterjectTax = 1;
    public int MinActionCost = 1;
    public int RewindMaxGap = 4;            // 后退后，最多比对手落后 4 刻
    public int PushSingleMax = 3;
    public int PushPerBarMax = 4;
    public int SandMax = 10;
    public int RefluxThreshold = 10;
    public int RefluxRewind = 2;
    public int[] DuskBellLines = { 48, 52, 56, 60 };
    public int[] DuskBellDamage = { 1, 2, 3, 4 };
    public int EmptyDeckDamage = 2;
    public int KairosLineMax = 60;          // 契机只认 4–60 刻的钟点线
    // 以下只用于客户端和服务器，不参与规则结算
    public int InterjectTimeoutMs = 8000;
    public int ActionTimeoutMs = 30000;
    public int ReserveTimeMs = 60000;
}
```

下文代码中用 `C` 表示 `s.Config`。

---

## 2. 数据模型（`Core/Types.cs`、`Core/GameState.cs`）

```csharp
[Flags] public enum Keyword { None = 0, Guard = 1 << 0, Swift = 1 << 1, Growth = 1 << 2 }
public enum Duration { Permanent, UntilControllerNextChime, UntilUsed }
public enum ActionCategory { Unit, Spell, Attack, Other }
public enum TimelineObjectKind { Channel, Omen, Echo, Calendar }
public enum Outcome { Ongoing, P0Win, P1Win, Draw }
public enum MoveCause { Pay, Pushed, Wait }

public sealed class GameState
{
    public RulesConfig Config;
    public string Seed;
    public KairosRng Rng;
    public PlayerState[] Players = new PlayerState[2];
    public List<TimelineObject> Timeline = new();
    public int MoveSeq;                          // 全局移动序号，每次任何时标移动都会 +1
    public int LastDeclaredActor = -1;           // 用于判定连拍（R-5-4），-1 表示尚无
    public int[] ChainLength = new int[2];       // 当前连续宣言次数
    public List<ActionRecord> ActionLog = new();
    public TriggerQueue Triggers = new();        // 分帧 FIFO 队列（第 9 节）
    public PendingDecision Pending;              // 不为 null 时，只接受对应的命令
    public Stack<InFlightAction> InFlight = new(); // 已宣言、尚未完成的行动；抢拍会压在原行动之上
    public Outcome Outcome = Outcome.Ongoing;
    public int NextId = 1;

    public static int Opp(int p) => 1 - p;
}

public sealed class PlayerState
{
    public int Id;
    public KeeperState Keeper;
    public int Life;
    public int Marker;                           // 0..TimelineEnd
    public int LastMoveSeq;                      // 平局时，值较大者先行动（R-4-2）
    public int ChimesRung;                       // 已鸣钟次数 = 已鸣响的最高钟点线 / BarLength
    public bool Finished;                        // 到达第 64 刻
    public List<CardInstance> Hand = new();
    public List<CardInstance> Deck = new();      // Deck[0] 为牌库顶
    public List<CardInstance> Past = new();      // 往昔
    public List<Unit> Board = new();             // 从左到右，Count ≤ BoardMax
    public List<SigilInstance> Sigils = new();
    public int Sand;
    public bool RefluxUsed, RefluxPending;
    public int PendingProphecyDraw;              // R-7-4：在检查点结算
    public List<PlayerStatus> Statuses = new();
    public BarCounters Bar = new();              // 每次本人鸣钟时重置
    public string LastPlayedCardKey;             // 最后一张正面打出的牌（供镜中之刻、零使用）
}

public sealed class BarCounters                  // “你的小节” = 你上一次鸣钟到下一次鸣钟之间
{
    public int FocusUsed;
    public bool InterjectTaxFreeUsed;            // 伊琳
    public bool ChainDiscountUsed;               // 先手之律
    public int PushReceived;                     // 本小节累计被推迟的刻数
    public bool AnyUnitDied;                     // 本小节内任意侍从死亡
    public bool FriendlyUnitDied;                // 摩尔甘
    public bool ChainDrawUsed;                   // 阿迅
}

public sealed class CardInstance { public int Iid; public string Key; public int Owner; }

public sealed class Unit
{
    public int Eid;
    public CardInstance Card;
    public int Atk, Hp, MaxHp;
    public int Cadence;
    public Keyword Keywords;
    public bool Exhausted;
    public bool Stasis;                          // 第 13.3 节
    public bool Shield;
    public int EnteredAtChime;                   // 入场时控制者的 ChimesRung
    public int AttacksSinceReady;
    public List<Buff> Buffs = new();
}

public sealed class Buff { public int Atk, Hp; public Duration Duration; public int SourceIid; }

public sealed class TimelineObject
{
    public int Oid;
    public TimelineObjectKind Kind;
    public int Owner;                            // 历法物件为 -1
    public int Pos;
    public CardInstance Card;
    public bool FaceUp;                          // 伏笔为 false
    public int PlacedSeq;                        // 同一刻有多个物件时，按放置顺序结算
    public List<TargetRef> LockedTargets = new(); // 咏唱在宣言时锁定的目标
    public ActionFlags LockedFlags;              // 宣言时的契机、连拍等标记
}

public struct ActionFlags { public bool Kairos, ChainBeat, Interjecting; }

public enum TargetKind { Unit, Keeper, TimelineObject }
public struct TargetRef { public TargetKind Kind; public int Id; }   // Keeper 时 Id 为玩家编号

public abstract class PlayerStatus { public Duration Duration; }
public sealed class NextCardCostStatus : PlayerStatus { public int Delta; public int MinBaseTime; }
public sealed class ProphecyStatus : PlayerStatus { public int Owner; public ActionCategory Predicted; }

public enum InFlightStage { AwaitInterject, Declared, Resolving, Moving }

public sealed class InFlightAction
{
    public int Actor;
    public Command Command;
    public int Cost;
    public ActionFlags Flags;
    public InFlightStage Stage;
    public bool Fizzled;
}
```

---

## 3. 对局流程状态机

```
 ┌────────┐   ┌──────────┐   ┌──────────────────────────────────────────┐   ┌─────┐
 │ SETUP  │──►│ MULLIGAN │──►│ MAIN LOOP                                │──►│ END │
 └────────┘   └──────────┘   │  NextActor → 等待行动命令                │   └─────┘
                              │      ↓ Declare                           │
                              │  [AwaitInterject]（可选）                │
                              │      ↓                                   │
                              │  Declared → Resolving（可能等待 Choose） │
                              │      ↓                                   │
                              │  Moving（不可暂停）→ Checkpoint          │
                              └──────────────────────────────────────────┘
```

### 3.1 SETUP（R-3-1）
1. 用 `DeckValidator` 校验牌组：张数为 30，满足同名上限和学派上限，且包含钟主所属学派。
2. 用 `KairosRng(seed)` 随机决定先行者 `first`。
3. 双方洗牌（Fisher–Yates，使用 `KairosRng`）。双方的 `Marker` 都设为 0。先行者的 `LastMoveSeq` 设为 1，后行者设为 0（结合 R-4-2，先行者先行动）。`MoveSeq` 设为 1。
4. 先行者抽 `HandStart` 张，后行者抽 `HandStart + SecondPlayerBonusCards` 张。

### 3.2 MULLIGAN（R-3-2）
- 双方**同时**提交 `MulliganCommand`：把选中的牌放回牌库，洗牌，再抽同样数量的牌。每人只能调度一次。
- 双方都提交后，进入主循环。

---

## 4. 行动权判定（`Flow/TurnOrder.cs`）

```csharp
public static class TurnOrder
{
    // R-4-1 落后者行动；R-4-2 平局时后到者先行；R-4-3 已到达终点者不再行动
    // 返回 -1 表示双方都已到达终点，转入时尽判定
    public static int NextActor(GameState s)
    {
        var a = s.Players[0];
        var b = s.Players[1];
        if (a.Finished && b.Finished) return -1;
        if (a.Finished) return 1;
        if (b.Finished) return 0;
        if (a.Marker != b.Marker) return a.Marker < b.Marker ? 0 : 1;
        return a.LastMoveSeq > b.LastMoveSeq ? 0 : 1;
    }
}
```

- **R-4-4**：任何原因引起的时标位置变化（支付时间、被推迟、后退、逆流）都会让该玩家的 `LastMoveSeq = ++s.MoveSeq`。位置没有实际变化（例如推迟被上限削减为 0）时不更新。
- **R-4-5**：只要 `NextActor` 仍然返回同一个玩家，该玩家就继续行动。不存在“回合结束”的概念。

---

## 5. 行动管线（`Flow/ActionPipeline.cs`）

### 5.1 命令（`Core/Commands.cs`）

```csharp
public abstract class Command { public int Player; }

public sealed class PlayCardCommand     : Command { public int CardIid; public List<TargetRef> Targets = new(); public string Choice; public int Sand; }
public sealed class AttackCommand       : Command { public int AttackerEid; public TargetRef Target; }
public sealed class KeeperAbilityCommand: Command { public List<TargetRef> Targets = new(); public string Choice; }
public sealed class FocusCommand        : Command { }
public sealed class SetOmenCommand      : Command { public int CardIid; public int Offset; }   // OmenOffsetMin..Max
public sealed class WaitCommand         : Command { }
public sealed class InterjectCommand    : Command { public int CardIid; public List<TargetRef> Targets = new(); public int Sand; }
public sealed class PassCommand         : Command { }                                         // 放弃抢拍
public sealed class ChooseCommand       : Command { public int[] Selection; }                 // 回应 Pending
public sealed class MulliganCommand     : Command { public int[] CardIids; }
public sealed class ConcedeCommand      : Command { }
```

### 5.2 管线参考实现

管线是一个**分阶段的状态机**。`InFlight` 是一个栈：抢拍会作为新的行动压在原行动之上，抢拍完成后弹出，原行动从 `Declared` 阶段继续。

```csharp
public static class ActionPipeline
{
    // 入口：所有命令都经由 GameEngine.Apply 分派到这里
    public static void Declare(GameState s, int actor, Command cmd, IEventSink ev, bool interjecting = false)
    {
        // ① 校验（R-5-1）
        if (!interjecting)
            Rules.Require(TurnOrder.NextActor(s) == actor && s.Pending == null, "R-5-1");
        Rules.Require(LegalCommands.IsLegal(s, actor, cmd), "R-5-1");

        // ② 计算并锁定费用（R-5-2）；修正和消费都在这一步发生，也包含视界校验
        var P = s.Players[actor];
        int cost = CostCalculator.ComputeAndConsume(s, actor, cmd);

        // ③ 锁定标记（R-5-3、R-5-4）
        var flags = new ActionFlags
        {
            Kairos = Rules.IsKairos(s.Config, P.Marker + cost),
            ChainBeat = s.LastDeclaredActor == actor,
            Interjecting = interjecting,
        };
        Chain.OnDeclared(s, actor);              // LastDeclaredActor = actor；更新 ChainLength
        Prophecy.ConsumeOnDeclare(s, actor, cmd); // R-7-4：命中时记录 PendingProphecyDraw

        // ④ 卡牌离开手牌，进入“宣言中”
        CardZones.MoveToInFlight(s, actor, cmd);
        var f = new InFlightAction { Actor = actor, Command = cmd, Cost = cost, Flags = flags, Stage = InFlightStage.Declared };
        s.InFlight.Push(f);
        ev.Emit(new ActionDeclaredEvent(actor, cmd, cost, flags));

        // ⑤ 抢拍窗口（R-12-1）；抢拍本身不再开窗（R-12-4）
        int responder = GameState.Opp(actor);
        if (!interjecting && InterjectWindow.Opens(cmd) && InterjectWindow.CanInterject(s, responder))
        {
            f.Stage = InFlightStage.AwaitInterject;
            s.Pending = PendingDecision.InterjectWindow(responder);
            ev.Emit(new InterjectWindowEvent(responder));
            return;
        }
        Advance(s, ev);
    }

    // 收到 InterjectCommand：清除 Pending，以抢拍身份 Declare（会压栈）
    // 收到 PassCommand：清除 Pending，把栈顶的 Stage 改为 Declared，再调用 Advance
    // 收到 ChooseCommand：把选择交给 TriggerQueue，清除 Pending，再调用 Advance

    public static void Advance(GameState s, IEventSink ev)
    {
        while (s.InFlight.Count > 0)
        {
            if (s.Outcome != Outcome.Ongoing) { s.InFlight.Clear(); return; }
            var f = s.InFlight.Peek();
            switch (f.Stage)
            {
                case InFlightStage.AwaitInterject:
                    return;                                              // 等待 Interject / Pass

                case InFlightStage.Declared:
                    // ⑥ 先效果，后时间（R-5-5）；抢拍后不再合法则失效，但时间照付（R-5-6）
                    if (Legality.StillLegal(s, f)) Resolver.EnqueueActionEffects(s, f, ev);
                    else { f.Fizzled = true; Resolver.Fizzle(s, f, ev); }
                    f.Stage = InFlightStage.Resolving;
                    break;

                case InFlightStage.Resolving:
                    if (!s.Triggers.DrainTopLevel(s, ev)) return;        // 等待 Choose（R-9-4）
                    f.Stage = InFlightStage.Moving;
                    break;

                case InFlightStage.Moving:
                    // ⑦ 推进时标（逐刻处理，第 6 节），这一段不可暂停
                    TimelineService.MoveForward(s, f.Actor, f.Cost, MoveCause.Pay, ev);
                    // ⑧ 检查点（第 10 节）
                    Checkpoint.Run(s, ev);
                    s.ActionLog.Add(ActionRecord.From(f));
                    s.InFlight.Pop();
                    // 抢拍完成：原行动从 Declared 阶段继续
                    if (s.InFlight.Count > 0) s.InFlight.Peek().Stage = InFlightStage.Declared;
                    break;
            }
        }
    }
}
```

### 5.3 管线规则

- **R-5-1**：只有当前行动者可以提交行动命令；`Pending` 不为空时，只接受 `Pending` 指定的命令。
- **R-5-2**：费用在宣言时锁定。宣言之后发生的任何变化（例如抢拍让光环消失）都不会改变已锁定的费用。
- **R-5-3（契机）**：`Kairos = (marker + cost) % BarLength == 0 && BarLength <= marker + cost && marker + cost <= KairosLineMax`。按宣言时的时标和最终费用计算。
- **R-5-4（连拍）**：`ChainBeat = (LastDeclaredActor == actor)`。**任何**行动的宣言（包括抢拍、蓄势、静候、放置伏笔、时器技能）都会更新 `LastDeclaredActor`。因此，对手的一次抢拍会打断你的连拍。
- **R-5-5（先效果后时间）**：行动发生在“出发时刻”，结算完效果之后时标才前进。所以，行动者在时标前进途中踩到伏笔时，刚打出的侍从已经在场上。
- **R-5-6（失效）**：抢拍结算后，如果原行动不再合法（攻击者或目标离场、法术被反制、侍从位已满等），原行动失效。卡牌按“打出”处理进入往昔（被反制的除外，见该卡），**原行动者照常支付时间**。
- **R-5-7**：`PlayCardCommand` 打出侍从时，侍从位必须有空位，否则不合法。
- **R-5-8**：`WaitCommand` 的耗时是 `对手时标 + 1 − 自己时标`，结果上限为 64；不受视界限制，也不受费用修正影响，但仍然会逐刻处理。
- **R-5-9**：`FocusCommand` 每小节最多 1 次。结算：抽 1 张。
- **R-5-10**：`SetOmenCommand` 见第 11.2 节；不开抢拍窗口。

---

## 6. 时标移动与逐刻处理（`Timeline/TimelineService.cs`）

```csharp
public static class TimelineService
{
    // R-6-1 向前移动时逐刻处理。这一过程不可暂停（R-9-4），推迟等效果会递归调用本方法
    public static void MoveForward(GameState s, int p, int delta, MoveCause cause, IEventSink ev)
    {
        var C = s.Config;
        var P = s.Players[p];
        if (P.Finished || delta <= 0) return;
        int from = P.Marker;
        int target = Math.Min(C.TimelineEnd, P.Marker + delta);
        while (P.Marker < target)
        {
            P.Marker++;
            ev.Emit(new TickEvent(p, P.Marker));
            ProcessTick(s, p, P.Marker, ev);             // R-6-2
            if (s.Outcome != Outcome.Ongoing) break;     // 有人死亡则立即停止
        }
        P.LastMoveSeq = ++s.MoveSeq;                     // R-4-4
        ev.Emit(new MarkerMovedEvent(p, from, P.Marker, cause));
        if (P.Marker >= C.TimelineEnd) Finish(s, p, ev); // R-11-4、R-14-3
    }

    // R-6-2 同一刻的处理顺序：① 伏笔 → ② 本人的咏唱和回声 → ③ 鸣钟
    // 每个物件先从时轴上移除，再结算，并在独立的触发帧内排空
    static void ProcessTick(GameState s, int p, int t, IEventSink ev)
    {
        foreach (var o in TakeObjects(s, maxPos: t, owner: -1, TimelineObjectKind.Omen))
            s.Triggers.RunFrame(s, ev, () => OmenService.Reveal(s, o, triggerer: p, ev));
        foreach (var o in TakeObjects(s, maxPos: t, owner: p, TimelineObjectKind.Channel, TimelineObjectKind.Echo))
            s.Triggers.RunFrame(s, ev, () => ChannelService.Resolve(s, o, ev));
        var P = s.Players[p];
        if (t % s.Config.BarLength == 0 && t < s.Config.TimelineEnd && t / s.Config.BarLength > P.ChimesRung)
            s.Triggers.RunFrame(s, ev, () => ChimeService.Chime(s, p, t, ev));
    }
    // TakeObjects(s, maxPos, owner, params kinds)：筛选 Pos <= maxPos、类型属于 kinds 的物件
    // （owner 为 -1 表示不限所有者），按 PlacedSeq 升序返回到新的 List，并从 s.Timeline 移除

    // R-6-3 后退：不做逐刻处理；受 RewindMaxGap 限制
    public static void Rewind(GameState s, int p, int amount, IEventSink ev)
    {
        var P = s.Players[p];
        var O = s.Players[GameState.Opp(p)];
        int floor = Math.Max(0, O.Marker - s.Config.RewindMaxGap);
        int next = Math.Max(floor, P.Marker - amount);
        if (next >= P.Marker) return;
        ev.Emit(new MarkerMovedEvent(p, P.Marker, next, MoveCause.Pushed));
        P.Marker = next;
        P.LastMoveSeq = ++s.MoveSeq;
    }

    // R-6-6 推迟玩家时标
    public static void Push(GameState s, int p, int amount, IEventSink ev)
    {
        var P = s.Players[p];
        if (P.Finished) return;                          // R-14-3
        int allowed = Math.Min(amount, Math.Min(s.Config.PushSingleMax, s.Config.PushPerBarMax - P.Bar.PushReceived));
        if (allowed <= 0) return;
        P.Bar.PushReceived += allowed;
        MoveForward(s, p, allowed, MoveCause.Pushed, ev); // 被推迟者照常逐刻处理
    }
}
```

- **R-6-4**：`Pos <= t` 的写法保证了被推迟或因故被“跳过”的物件不会遗漏。物件只要在它应生效的位置或之前被越过，就会结算。
- **R-6-5（每条钟点线只鸣一次）**：由 `t / BarLength > ChimesRung` 保证。后退以后再越过同一条线，不会再次鸣钟。
- **R-6-6（推迟玩家时标）**：被推迟者照常逐刻处理：会踩伏笔、触发自己的咏唱，也会鸣钟。被推迟的玩家可能在推迟途中鸣钟，鸣钟会重置 `PushReceived`。这是有意的设计：上限按“被推迟者的小节”计算。
- **R-6-7（单次行动净耗时下限）**：如果某次行动的效果会让**行动者本人**后退（基础系列没有这类卡，预留给扩展），引擎在该行动的检查点保证 `markerAfter ≥ markerBefore + 1`。由他人行动触发的后退（例如余音、逆流）不受这条限制。

### 6.1 鸣钟（`Timeline/ChimeService.cs`，R-6-8）

玩家 p 越过钟点线 L（L = k × BarLength）时，依次执行：

```csharp
public static void Chime(GameState s, int p, int line, IEventSink ev)
{
    var C = s.Config;
    var P = s.Players[p];
    P.ChimesRung = line / C.BarLength;                         // 1
    ev.Emit(new ChimeEvent(p, line));
    CardZones.Draw(s, p, 1, ev);                               // 2（R-10-4）
    foreach (var u in P.Board)                                 // 3 重整
    {
        if (u.Stasis) { u.Stasis = false; continue; }          //   停滞：解除但不重整（R-13-2）
        u.Exhausted = false;
        u.AttacksSinceReady = 0;
    }
    Buffs.ExpireForController(s, p, Duration.UntilControllerNextChime);   // 4
    Statuses.Expire(P, Duration.UntilControllerNextChime);
    P.Bar = new BarCounters();                                 // 5
    s.Triggers.CollectOnYourChime(s, p);                       // 6 蓄力、光辉圣约
    int bellIndex = Array.IndexOf(C.DuskBellLines, line);      // 7 暮钟（来源：无，不可减免）
    if (bellIndex >= 0) Damage.ToPlayerUnpreventable(s, p, C.DuskBellDamage[bellIndex], ev);
}
```

视界不单独存储，随时通过 `Math.Min(C.HorizonMax, C.HorizonBase + P.ChimesRung)` 计算。

---

## 7. 费用计算（`Flow/CostCalculator.cs`）

```csharp
public static class CostCalculator
{
    // consume = false 用于合法性枚举和幽灵预览（只计算不消费）
    public static int Compute(GameState s, int p, Command cmd, bool consume)
    {
        var C = s.Config;
        int baseCost;
        switch (cmd)
        {
            case PlayCardCommand pc:      baseCost = Cards.Def(s, pc.CardIid).Time; break;
            case InterjectCommand ic:     baseCost = Cards.Def(s, ic.CardIid).Time; break;
            case AttackCommand ac:        baseCost = Units.Get(s, ac.AttackerEid).Cadence; break;
            case KeeperAbilityCommand _:  baseCost = s.Players[p].Keeper.Def.Ability.Cost; break;
            case FocusCommand _:          return C.FocusCost;      // 不受修正
            case SetOmenCommand _:        return C.OmenSetCost;    // 不受修正
            case WaitCommand _:           return WaitCost(s, p);   // R-5-8
            default: throw new RulesViolationException("R-5-1", "此命令没有费用");
        }

        int c = baseCost;
        c += Auras.SumCostMods(s, p, cmd);                 // 日晷法官（咏唱 +1）、静滞领域（攻击 +1）
        c += Statuses.NextCardCost(s, p, cmd, consume);    // 虚空凝视（+2）、阿迅（-1）；只对正面打出卡牌生效
        c += Prophecy.Penalty(s, p, cmd);                  // R-7-4
        if (cmd is InterjectCommand) c += Interject.Tax(s, p, consume);   // 通常 +1；伊琳每小节第一次为 0
        c -= Auras.ChainDiscount(s, p, cmd, consume);      // 先手之律：本小节第一次连拍行动 -1
        int sand = cmd is PlayCardCommand pc2 ? pc2.Sand : cmd is InterjectCommand ic2 ? ic2.Sand : 0;
        c -= sand;
        c = Math.Max(C.MinActionCost, c);
        if (consume) s.Players[p].Sand -= sand;            // R-7-2：投入的时砂全部消耗
        return c;
    }

    public static int ComputeAndConsume(GameState s, int p, Command cmd)
    {
        int cost = Compute(s, p, cmd, consume: false);
        Rules.Require(cmd is WaitCommand || cost <= Horizon(s, p), "R-7-1");
        return Compute(s, p, cmd, consume: true);
    }

    public static int Horizon(GameState s, int p) =>
        Math.Min(s.Config.HorizonMax, s.Config.HorizonBase + s.Players[p].ChimesRung);
}
```

- **R-7-1（视界）**：视界校验的是**最终费用**（所有修正和时砂都计算之后）。时砂因此可以让玩家提前打出大牌，这是暮学派的身份特征。
- **R-7-2（时砂）**：只能用于 `PlayCardCommand` 和 `InterjectCommand`。投入数量 ≤ 持有数量。即使投入过多导致费用被下限截断，投入的时砂也会全部消耗（UI 需要提示）。
- **R-7-3（消费时机）**：“下一张牌”类状态和先手之律的折扣在**宣言时**消费。即使行动之后失效，也不会返还。
- **R-7-4（预言）**：如果行动者身上有 `ProphecyStatus`，该状态在其**下一次宣言时**（任何行动类型）被消费。按以下方式归类：打出侍从或以侍从抢拍为 `Unit`；打出术式或刻印，或以术式抢拍为 `Spell`；攻击为 `Attack`；其他（时器、蓄势、伏笔、静候）为 `Other`。如果归类与预言一致：行动费用 +1（`Wait`、`Focus`、`SetOmen` 除外），预言的主人在该行动的检查点抽 2 张（`PendingProphecyDraw`）。预言内容是公开信息，所以“费用 +1 后超过视界”会被合法行动生成器正确排除。

---

## 8. 战斗（`Combat/CombatService.cs`）

- **R-8-1（宣言）**：攻击者必须是己方未疲惫、未停滞的侍从。目标可以是敌方侍从或敌方钟主（`TargetKind.Keeper`）。如果敌方场上有守卫侍从，目标必须是其中之一。宣言时攻击者立即疲惫，`AttacksSinceReady += 1`。
- **R-8-2（结算）**：如果攻击者仍在场、目标仍然合法，双方**同时**造成伤害：攻击者对目标造成等同于其攻击力的伤害；如果目标是侍从，目标同时对攻击者造成等同于其攻击力的伤害。钟主不反击。
- **R-8-3（重定向）**：拦截者的 `REDIRECT_ATTACK` 会把攻击目标改为拦截者本身，**无视守卫限制**。
- **R-8-4（伤害）**：

```csharp
public static void DealDamage(GameState s, TargetRef target, int amount, IEventSink ev)
{
    if (amount <= 0) return;
    if (target.Kind == TargetKind.Unit)
    {
        var u = Units.Get(s, target.Id);
        if (u == null) return;
        if (u.Shield)
        {
            u.Shield = false;                                   // 护盾抵消一次伤害，无论多少
            ev.Emit(new ShieldBrokenEvent(u.Eid));
            s.Triggers.Collect(s, TriggerKey.OnFriendlyShieldBroken, u.Card.Owner, u);
            return;
        }
        u.Hp -= amount;                                         // 死亡在状态检查中处理（R-10-1）
        ev.Emit(new DamageEvent(target, amount));
    }
    else
    {
        var P = s.Players[target.Id];
        P.Life -= amount;
        ev.Emit(new DamageEvent(target, amount));
        if (!P.RefluxUsed && P.Life <= s.Config.RefluxThreshold && P.Life > 0) P.RefluxPending = true;
    }
}
```

`-X/-X` 与“消灭”不算伤害，护盾无法抵挡。

- **R-8-5**：攻击力 ≤ 0 的侍从可以攻击（不造成伤害）。攻击力取当前值，最低按 0 计算。

---

## 9. 触发器与结算队列（`Effects/TriggerQueue.cs`）

### 9.1 触发时机（`TriggerKey`）

| 触发键 | 时机 | 典型卡 |
| --- | --- | --- |
| `ON_PLAY` | 侍从：部署时；术式：结算时；咏唱：在**咏唱结算时**触发 | 大部分卡 |
| `ON_DEATH` | 该侍从死亡后（已进入往昔） | 灰烬侍僧、余烬巨像 |
| `ON_REVEAL` | 伏笔揭示时 | 黄昏契约、虚空凝视 |
| `ON_YOUR_CHIME` | 控制者鸣钟时 | 蓄力、光辉圣约 |
| `ON_ANY_UNIT_DEATH` | 任意侍从死亡后 | 暮色收割者 |
| `ON_FRIENDLY_UNIT_DEATH` | 友方侍从死亡后 | 摩尔甘 |
| `ON_FRIENDLY_UNIT_ENTER` | 友方侍从入场后 | 古尔德 |
| `ON_OWN_OMEN_REVEALED` | 你的伏笔揭示后（包括虚张声势） | 命运织者 |
| `ON_FRIENDLY_SHIELD_BROKEN` | 友方护盾被打破 | 珂莱 |
| `ON_YOUR_WAIT` | 你执行静候之后 | 小满 |
| `ON_CHAIN_LENGTH` | 你的连续宣言次数达到 N | 阿迅 |
| `STATIC` | 常驻光环，不进入队列 | 日晷法官、静滞领域 |

### 9.2 队列规则
- **R-9-1（分帧 FIFO）**：`TriggerQueue` 是一个**帧栈**，每一帧是一个 FIFO 队列。一个效果执行过程中产生的触发，追加到**当前帧**的尾部。时标移动中的每个物件、每次鸣钟都在一个新帧里执行并排空（`RunFrame`），之后才回到外层帧。
- **R-9-2（同批排序）**：同一个事件引发的多个触发，入队顺序为：**当前行动者**的触发优先，然后是对手的。每一方内部的顺序为：侍从（从左到右）→ 刻印（按放置顺序）→ 钟主 → 时轴物件。
- **R-9-3（状态检查）**：每完成一个队列任务，执行一次状态检查（第 10 节）。
- **R-9-4（只有顶层可以暂停）**：
  - 只有**行动的顶层结算**（帧深度为 1，即 `DrainTopLevel`）中的 op 可以请求玩家选择。这时引擎设置 `s.Pending = PendingDecision.Choice(...)`，把当前任务和 op 下标作为断点保存在 `TriggerQueue` 中，然后暂停。收到 `ChooseCommand` 后，从断点继续执行。
  - **被动触发的效果**（`ON_DEATH`、`ON_REVEAL`、`ON_YOUR_CHIME`、`ON_*_DEATH`、`ON_*_ENTER`、`ON_FRIENDLY_SHIELD_BROKEN`、`ON_YOUR_WAIT`、`ON_CHAIN_LENGTH`、咏唱和回声的 `ON_PLAY`）**不得**包含需要选择的 op（`SCRY`、`PICK_FROM_TOP`、`SUMMON_FROM_PAST`）。卡牌数据加载时由 `CardValidator` 校验，违反时拒绝加载。
  - 这样设计的原因：时标移动可以写成普通的递归调用，不需要可续体；任何时刻的 `GameState` 都可以直接 `Clone()`，ISMCTS 不需要处理“移动到一半”的状态。
- **R-9-5（来源离场）**：触发入队后，即使来源已经离场，触发照样结算。效果里的 `$self` 指向已离场的实体时，相关 op 不执行。

```csharp
public sealed class TriggerQueue
{
    readonly List<Queue<EffectTask>> _frames = new() { new Queue<EffectTask>() };
    public EffectTask Suspended;          // 顶层暂停时的断点
    public int SuspendedOpIndex;

    public void Enqueue(EffectTask t) => _frames[_frames.Count - 1].Enqueue(t);

    // 顶层：可暂停。返回 false 表示正在等待 ChooseCommand
    public bool DrainTopLevel(GameState s, IEventSink ev) { /* 从 Suspended 断点或队首继续，每个任务后 Checkpoint.StateBased */ }

    // 嵌套帧：不可暂停
    public void RunFrame(GameState s, IEventSink ev, Action body)
    {
        _frames.Add(new Queue<EffectTask>());
        body();
        var q = _frames[_frames.Count - 1];
        while (q.Count > 0 && s.Outcome == Outcome.Ongoing)
        {
            EffectRunner.RunToEnd(s, q.Dequeue(), ev);     // 遇到需要选择的 op 时抛出 RulesViolationException("R-9-4")
            Checkpoint.StateBased(s, ev);
        }
        _frames.RemoveAt(_frames.Count - 1);
    }
}
```

---

## 10. 检查点与状态检查（`Rules/Checkpoint.cs`）

### 10.1 状态检查（`StateBased`，每个队列任务完成后执行）
- **R-10-1**：所有 `Hp ≤ 0` 的侍从**同时**死亡：移出场地，进入其所有者的往昔；双方 `Bar.AnyUnitDied = true`，该侍从所有者的 `Bar.FriendlyUnitDied = true`；把 `ON_DEATH`、`ON_ANY_UNIT_DEATH`、`ON_FRIENDLY_UNIT_DEATH` 触发按 R-9-2 的顺序加入当前帧。
- **R-10-2**：任一玩家 `Life ≤ 0` 时立即结束对局：只有一方则对方胜；双方同时则判 `Draw`。

### 10.2 行动检查点（`Run`，每个行动完成后执行）
1. 确认当前帧已排空。
2. 结算 `PendingProphecyDraw`（R-7-4）。
3. **逆流**（R-10-3）：对 `RefluxPending` 为真的玩家执行 `TimelineService.Rewind(p, RefluxRewind)`，然后设置 `RefluxUsed = true`、`RefluxPending = false`。双方同时满足时，先处理当前行动者。
4. 如果双方都已 `Finished`，进入时尽判定（R-14-2）。

### 10.3 抽牌
- **R-10-4**：牌库为空时，抽牌改为受到 `EmptyDeckDamage` 点伤害（每次）。手牌已满时，抽到的牌公开并直接进入往昔（“焚毁”），不触发任何效果。

---

## 11. 时轴物件

### 11.1 咏唱（Channel）
- **R-11-1**：打出带 `channel: X` 的卡牌时，支付 `cost`；然后**不立即结算效果**，而是创建一个正面朝上的物件，`Pos = 宣言时时标 + cost + X`。目标和标记（契机、连拍）在宣言时锁定，并对双方公开。
- **R-11-2**：放置时，如果你已有 `ChannelMax` 个咏唱，行动不合法。
- **R-11-3**：物件所有者的时标到达 `Pos` 或越过 `Pos` 时，执行该卡的 `ON_PLAY`。锁定的目标如果已经离场或不再合法，则对应的 op 不执行（**不会重新选择目标**）。结算后，卡牌进入往昔。
- **R-11-4**：咏唱物件不能被“消灭”。基础系列中只能推迟它，或通过让所有者到达终点使它永远不结算。所有者到达第 64 刻时，所有未结算的咏唱和回声直接进入往昔。

### 11.2 伏笔（Omen）
- **R-11-5（放置）**：`SetOmenCommand` 固定耗时 `OmenSetCost`。`post = Marker + OmenSetCost`，`Pos = post + Offset`，`Offset ∈ [OmenOffsetMin, OmenOffsetMax]`。必须同时满足：`Pos > 对手时标`、`Pos < TimelineEnd`、该位置没有你自己的伏笔，且你的伏笔数量小于上限。任意手牌都可以盖放。
- **R-11-6（揭示）**：任一玩家的时标**到达或越过** `Pos` 时揭示。造成这次移动的玩家就是**触发者**，包括伏笔主人自己。牌面朝上展示给双方。
  - 如果该牌的 `type` 是 `OMEN`：执行其 `ON_REVEAL`，然后进入往昔。
  - 否则视为**虚张声势**：直接进入往昔，不执行任何效果。
  - 无论真假，都触发主人的 `ON_OWN_OMEN_REVEALED`。
- **R-11-7**：伏笔牌（`type: OMEN`）**只能**以伏笔方式使用，不能正面打出。
- **R-11-8**：伏笔可以被推迟（例如秩序之刻、烈阳天使），推迟后的位置不受 R-11-5 的范围限制。

### 11.3 回声（Echo，扩展预留）
- **R-11-9**：带 `echo: N` 的卡牌结算完 `ON_PLAY` 后，创建一个正面朝上的 `Echo` 物件，`Pos = 当前时标 + N`。到达时执行一次减半的 `ON_PLAY`：所有数值参数 `amount` 按 `amount / 2`（整数除法）计算，值为 0 的 op 跳过。回声不会再产生回声。

### 11.4 历法（Calendar，扩展 2 预留）
- **R-11-10**：开局时放置，`Owner = -1`。由**最先到达**的玩家触发，效果对双方生效，依次以触发者、非触发者的顺序结算。

---

## 12. 抢拍窗口（`Flow/InterjectWindow.cs`）

- **R-12-1（开窗条件）**：宣言 `PlayCardCommand`、`AttackCommand`、`KeeperAbilityCommand` 时开窗；`FocusCommand`、`SetOmenCommand`、`WaitCommand`、`InterjectCommand` 不开窗。
- **R-12-2（资格）**：同时满足以下条件才算有资格：响应者没有 `Finished`；手牌中有至少一张带 `interject: true` 的牌，满足该牌的 `interjectCondition`，且 `费用（含税和修正）≤ 响应者视界`。
- **R-12-3（响应）**：响应者提交 `InterjectCommand` 或 `PassCommand`。数字版超时自动 `Pass`。
- **R-12-4（抢拍管线）**：抢拍以 `interjecting: true` 调用 `ActionPipeline.Declare`，压在原行动之上，并且**不再开窗**（一呼一应）。抢拍会更新 `LastDeclaredActor`，完整执行“结算 → 推进时标 → 检查点”之后出栈，原行动从 `Declared` 阶段继续。
- **R-12-5（主动打出）**：带 `interject` 的卡牌，在自己的行动中也可以作为普通 `PlayCardCommand` 打出，不付税，除非标记为 `interjectOnly`（例如拦截者、时空回溯，它们需要一个“正在宣言的行动”作为前提）。
- **R-12-6（抢拍条件枚举）**：

| `interjectCondition` | 含义 |
| --- | --- |
| `ANY` | 任何开窗的宣言 |
| `DECLARED_ATTACK` | 对手正在宣言攻击 |
| `DECLARED_SPELL` | 对手正在宣言打出术式或刻印 |
| `DECLARED_UNIT` | 对手正在宣言打出侍从 |

- **R-12-7（信息安全，网络层）**：为了避免泄露“你手里有没有抢拍牌”，服务器在**每一个**开窗宣言之后统一插入 `600–1200ms` 的随机静默期（这个随机数来自服务器，不进入规则 RNG）。只有确实有资格的响应者才会看到选择界面。玩家可以在设置中关闭自动跳过，改为“始终停顿”。本地对战和 AI 对战不需要静默期。

---

## 13. 持续效果、关键词与状态

### 13.1 关键词实现

| 关键词 | 实现方式 |
| --- | --- |
| `GUARD`（守卫） | R-8-1 中的目标约束 |
| `SWIFT`（迅捷） | 入场时 `Exhausted = false` |
| `GROWTH`（蓄力） | `CardDatabase` 加载时展开为 `{trigger: ON_YOUR_CHIME, effects: [BUFF $self +1/+1 PERMANENT]}`；`GROW_ALL` op 会直接调用这个展开后的效果 |
| `CHANNEL X`（咏唱） | 第 11.1 节 |
| `OMEN`（伏笔） | 卡牌类型，第 11.2 节 |
| `INTERJECT`（抢拍） | 第 12 节 |
| 部署 / 余音 | `ON_PLAY` / `ON_DEATH` |
| 契机 / 连拍 | 条件 `KAIROS` / `CHAIN_BEAT`，读取当前行动的 `ActionFlags` |

### 13.2 侍从入场
- **R-13-1**：新侍从放在最右侧。`Exhausted = !Swift`，`EnteredAtChime = 控制者.ChimesRung`，然后应用常驻光环（例如奥瑞斯给守卫的 +0/+1）。之后依次执行 `ON_PLAY`（部署），并将 `ON_FRIENDLY_UNIT_ENTER` 触发入队。
- **R-13-1a**：通过 `SUMMON_FROM_PAST` 召回的侍从**不触发部署**（`ON_PLAY`），但会触发 `ON_FRIENDLY_UNIT_ENTER`。召回的侍从入场时处于疲惫状态（即使带迅捷）。这条规则可以防止召回链，并保证 R-9-4 的“被动效果不需要选择”。

### 13.3 停滞
- **R-13-2**：`STASIS` 会让目标 `Exhausted = true` 且 `Stasis = true`。`Stasis` 为真时，**任何效果都不能让它重整**（包括疾风连奏、晨鸣）。控制者下一次鸣钟时，`Stasis` 变为假，但侍从在这次鸣钟中不重整；再下一次鸣钟才恢复正常。对已经停滞的侍从再次施加停滞，不叠加。

### 13.4 光环（STATIC）
- **R-13-3**：光环是随时计算的修正，不写入实体。来源离场后立即失效。生命值上限的光环失效时：`MaxHp` 减少，`Hp = Math.Min(Hp, MaxHp)`。如果因此 `Hp ≤ 0`，侍从在下一次状态检查时死亡。

### 13.5 增益持续时间
- **R-13-4**：`UntilControllerNextChime`：在受增益侍从的控制者下一次鸣钟时移除（第 6.1 节步骤 4）。

---

## 14. 胜负判定

- **R-14-1**：任一时刻有人 `Life ≤ 0`，对局立即结束（R-10-2）。
- **R-14-2（时尽）**：双方都到达第 64 刻时，依次比较：命烛 → 场上侍从的攻击力总和 → 手牌数。仍然相同则判 `Draw`。
- **R-14-3**：一方 `Finished` 后，另一方单独继续行动，直到也到达第 64 刻（通常会用静候，静候的目标位置上限为 64）。`Finished` 的玩家不能抢拍、不会被推迟，也不能成为伏笔的触发者。
- **R-14-4**：`ConcedeCommand` 可以随时提交，提交者立即判负。

---

## 15. 效果 DSL

### 15.1 卡牌结构（JSON）

```jsonc
{
  "key": "DAWN_SCOUT",           // 唯一键
  "name": "晨光斥候",
  "school": "DAWN",              // NEUTRAL | DAWN | NOON | DUSK | NIGHT | ETERNAL
  "type": "UNIT",                // UNIT | SPELL | SIGIL | OMEN
  "rarity": "COMMON",            // COMMON | RARE | EPIC | LEGENDARY
  "time": 1,
  "atk": 2, "hp": 1, "cadence": 1,   // 仅侍从有这些字段
  "keywords": ["SWIFT"],
  "interject": false,
  "interjectOnly": false,
  "interjectCondition": "ANY",
  "channel": null,               // 咏唱的 X
  "echo": null,
  "targets": [                   // 宣言时选择的目标
    { "id": "t1", "selector": "ANY_CHARACTER", "optional": false }
  ],
  "choice": null,                // 例如 ["UNIT","SPELL","ATTACK","OTHER"]
  "abilities": [
    { "trigger": "ON_PLAY", "effects": [ /* op 列表 */ ] }
  ],
  "static": [ /* 光环 */ ],
  "text": "迅捷",
  "flavor": "……"
}
```

### 15.2 C# 定义与加载（`Data/CardDefinition.cs`、`Data/CardDatabase.cs`）

JSON 用 Newtonsoft 反序列化成 DTO，再在加载阶段**编译**成强类型的效果树。运行时不再解析 JSON，也不使用反射。

```csharp
public sealed class CardDefinition
{
    public string Key, Name, School, Type, Rarity, Text, Flavor;
    public int Time;
    public int Atk, Hp, Cadence;
    public Keyword Keywords;
    public bool Interject, InterjectOnly;
    public InterjectCondition InterjectCondition;
    public int? Channel, Echo;
    public List<TargetSpec> Targets = new();
    public string[] Choice;
    public List<Ability> Abilities = new();
    public List<StaticAura> Static = new();
}

public sealed class Ability { public TriggerKey Trigger; public Condition Cond; public string Limit; public List<EffectNode> Effects = new(); }

public abstract class EffectNode { }
public sealed class OpNode : EffectNode { public IEffectOp Op; public OpArgs Args; }   // Args 在加载时解析成强类型
public sealed class IfNode : EffectNode { public Condition Cond; public List<EffectNode> Then = new(), Else = new(); }

public static class CardDatabase
{
    // Unity：Resources / Addressables 读取 TextAsset；.NET 工具：File.ReadAllText
    public static CardDatabase Load(string json, OpRegistry registry)
    {
        // 1. Newtonsoft 反序列化为 DTO
        // 2. 展开关键词（GROWTH → ON_YOUR_CHIME）
        // 3. 把每个 { "op": ... } 编译为 OpNode；未知 op 或缺参数时抛异常
        // 4. CardValidator：校验 R-9-4（被动触发中禁止使用需要选择的 op）、引用合法、数值范围
    }
}
```

### 15.3 引用（Ref）

| Ref | 含义 |
| --- | --- |
| `$t1`、`$t2` | 宣言时选择的目标 |
| `$self` | 这张卡对应的实体（侍从、刻印、伏笔物件） |
| `$you` / `$opponent` | 卡牌控制者 / 其对手（玩家） |
| `$youKeeper` / `$opponentKeeper` | 作为伤害目标的钟主 |
| `$triggerer` | 伏笔的触发者（玩家） |
| `$declared` | 当前被抢拍的那个行动 |
| `$choice` | 宣言时选择的选项 |
| `$entered` | `ON_FRIENDLY_UNIT_ENTER` 中刚入场的侍从 |

### 15.4 选择器（Selector）

| 选择器 | 含义 |
| --- | --- |
| `ANY_CHARACTER` | 任意侍从，或任一钟主 |
| `ANY_UNIT` / `FRIENDLY_UNIT` / `ENEMY_UNIT` | 侍从 |
| `TIMELINE_OBJECT` | 任意时轴物件（包括背面朝上的伏笔） |
| `ALL_UNITS` / `ENEMY_UNITS` / `FRIENDLY_UNITS` | 群体 |
| `FRIENDLY_GROWTH_UNITS` | 带蓄力的友方侍从 |
| `ENEMY_UNIT_HIGHEST_ATK` | 敌方攻击力最高的侍从；相同时取最左侧（确定性） |
| `FRIENDLY_UNIT_LOWEST_HP` | 友方当前生命值最低的侍从；相同时取最左侧 |
| `ENEMY_OBJECTS` | 敌方拥有的全部时轴物件 |

选择器可以带过滤器 `filter`：`minAtk`、`maxBaseTime`、`enteredThisBar`、`notAttacked`、`notStasis`、`exhausted`、`keyword`、`hasChannel`、`type`。

### 15.5 条件（Condition，用于 `IF`）

`KAIROS`、`CHAIN_BEAT`、`INTERJECTING`、`TRIGGERER_IS_OPPONENT`、`TRIGGERER_IS_SELF`、`ANY_UNIT_DIED_THIS_BAR`、`TARGET_DIED: "$t1"`（引用前面某个 op 的结果）、`YOUR_LIFE_LT_OPPONENT`。

### 15.6 操作（Op）注册表

| op | 参数 | 需要选择 | 说明 |
| --- | --- | --- | --- |
| `DAMAGE` | target, amount | | R-8-4 |
| `DAMAGE_ALL` | selector, amount | | 同时造成伤害后，统一做状态检查 |
| `HEAL` | target, amount | | 不超过上限 |
| `DRAW` | player, count | | R-10-4 |
| `SCRY` | player, count | ✔ | 由玩家排序或置底 |
| `PICK_FROM_TOP` | player, count, pick | ✔ | 未选中的牌按玩家指定的顺序置底 |
| `SUMMON_FROM_PAST` | player, filter | ✔ | 从符合条件的牌中选择；没有合法选项则跳过；R-13-1a |
| `DESTROY` | target | | 不算伤害 |
| `DESTROY_ALL` | selector | | 同时消灭 |
| `BUFF` | target, atk, hp, duration | | `hp` 增加时 `MaxHp` 一起增加；`-X/-X` 时 `MaxHp` 也减少 |
| `BUFF_ALL` | selector, atk, hp, duration | | |
| `SHIELD` | target | | 已有护盾时不叠加 |
| `STASIS` / `STASIS_ALL` | target / selector | | R-13-2 |
| `REFRESH` | target | | 停滞中的侍从无效；`AttacksSinceReady` 归零 |
| `SACRIFICE` | target | | 等同于消灭友方侍从，结果记为 `sacrificedTime` |
| `GAIN_SAND` | player, amount 或 `fromSacrificed` + max | | 上限 `SandMax` |
| `PAY_LIFE` | player, amount | | 算作失去生命，不算伤害，可以触发逆流 |
| `PUSH_MARKER` | player, amount | | `TimelineService.Push`，R-6-6 |
| `PUSH_OBJECT` | target, amount | | 推迟物件 |
| `PUSH_ALL_OBJECTS` | selector, amount | | |
| `NEXT_CARD_COST` | player, delta, minBaseTime?, duration | | 写入玩家状态 |
| `PROPHECY` | target 玩家, predicted = `$choice` | | 写入对手状态，覆盖旧预言 |
| `COUNTER_DECLARED` | destination: `DECK_BOTTOM` | | 让 `$declared` 失效，卡牌放到其所有者的牌库底 |
| `REDIRECT_ATTACK` | to | | 改变 `$declared` 攻击的目标 |
| `COPY_LAST_PLAYED` | from 玩家, to 玩家, costDelta? | | 用对方的 `LastPlayedCardKey` 生成一张新的卡牌实例放入手牌；为 null 则跳过 |
| `GROW_ALL` | selector, times | | 对每个目标执行 times 次蓄力效果 |
| `IF` | cond, then[], else[] | | 控制流（编译为 `IfNode`） |

```csharp
public enum OpResult { Done, NeedChoice }

public interface IEffectOp
{
    string Name { get; }                       // 与 JSON 中的 "op" 对应
    bool RequiresChoice { get; }               // R-9-4 校验依据
    OpArgs Parse(JObject json);                // 加载时调用：JSON → 强类型参数
    // 需要选择的 op：第一次执行时设置 s.Pending 并返回 NeedChoice；
    // 收到 ChooseCommand 后再次执行，此时 ctx.Choice 不为 null
    OpResult Execute(EffectContext ctx, OpArgs args);
}

public sealed class DamageOp : IEffectOp
{
    public string Name => "DAMAGE";
    public bool RequiresChoice => false;
    public sealed class Args : OpArgs { public RefExpr Target; public int Amount; }
    public OpArgs Parse(JObject j) => new Args { Target = RefExpr.Parse((string)j["target"]), Amount = (int)j["amount"] };
    public OpResult Execute(EffectContext ctx, OpArgs a)
    {
        var args = (Args)a;
        foreach (var t in ctx.Resolve(args.Target))
            CombatService.DealDamage(ctx.State, t, ctx.Scale(args.Amount), ctx.Events);   // Scale：回声减半
        return OpResult.Done;
    }
}
```

**新增 op 的流程**：在 `Effects/Ops/` 中新建一个实现 `IEffectOp` 的类，在 `OpRegistry` 中登记，并补充至少 1 个 NUnit 用例。

---

## 16. 隐藏信息与视图裁剪（`View/ViewRedactor.cs`）

| 信息 | 自己 | 对手 | 观战（延迟 2 分钟） |
| --- | --- | --- | --- |
| 手牌内容 | ✔ | 只看张数 | ✔ |
| 牌库顺序 | ✘（塞勒涅可以看牌库顶） | ✘ | ✘ |
| 伏笔内容 | ✔ | 只看位置 | ✔ |
| 预言内容 | ✔ | ✔（公开宣言） | ✔ |
| 往昔 | ✔ | ✔ | ✔ |
| 是否有抢拍资格 | ✔ | ✘（见 R-12-7） | ✘ |

`PlayerView ViewRedactor.For(GameState s, int viewer)` 生成裁剪后的视图。AI 只能读取 `PlayerView`，不能读取完整状态。ISMCTS 通过对隐藏信息做**确定化采样**进行推演。事件在发送给客户端前也要按同样的规则裁剪（例如对手抽到的牌只发 `CardDrawnEvent(hidden: true)`）。

---

## 17. 随机数、回放与网络

- **R-17-1**：`KairosRng`（xoshiro128**，种子用 SplitMix 从字符串哈希展开）只用于开局决定先行者、洗牌，以及将来带“随机”字样的效果。基础系列中没有任何随机效果。
- **R-17-2**：回放文件 `ReplayFile` 的结构为 `{ version, seed, rulesConfig, decks, commands[] }`，使用 JSON 存储。版本号与 `cards.json` 的版本号绑定。
- **R-17-3**：服务器权威。客户端只发送 `Command`；服务器调用 `GameEngine.Apply`，再把裁剪后的事件发回客户端，客户端根据事件播放动画。

### 17.1 联网方案（按阶段）

| 阶段 | 方案 |
| --- | --- |
| 原型 / 垂直切片 | 本地热座对战 + 本地 AI，引擎直接在 Unity 进程内运行 |
| 线上测试 | `Tools/Kairos.Server`：.NET 8 + ASP.NET Core SignalR（或 MagicOnion）。Unity 客户端通过 SignalR 客户端连接，服务器引用同一份 Engine 源码 |
| 正式上线 | 同一服务器部署到云端；或使用 Unity Gaming Services（Relay + Cloud Code）。核心原则不变：**规则只在服务器上执行** |

不建议使用 Netcode for GameObjects 同步规则状态：卡牌对战的状态同步是“命令加事件”，不是“对象同步”。

### 17.2 事件类型（`Core/Events.cs`，UI 动画驱动）

`ActionDeclaredEvent`、`InterjectWindowEvent`、`InterjectedEvent`、`CostPaidEvent`、`MarkerMovedEvent{from,to,cause}`、`TickEvent`、`ChimeEvent{line}`、`DuskBellEvent`、`CardDrawnEvent`、`CardBurnedEvent`、`UnitEnteredEvent`、`AttackEvent`、`DamageEvent`、`HealEvent`、`ShieldBrokenEvent`、`UnitDiedEvent`、`ObjectPlacedEvent`、`ObjectPushedEvent`、`OmenRevealedEvent{bluff}`、`ChannelResolvedEvent`、`ProphecySetEvent`、`ProphecyHitEvent`、`RefluxEvent`、`FizzleEvent`、`GameOverEvent`。

客户端的 `EventPlayer` 维护一个事件队列，按顺序播放动画，每个事件对应一个 `IEventPresenter`。规则状态在 `Apply` 返回时已经是最终状态；动画只用于表现，不回写规则状态。

---

## 18. AI 与工具接口

### 18.1 核心 API（`GameEngine`）
```csharp
public static class GameEngine
{
    public static GameState Create(RulesConfig config, CardDatabase db, DeckList deck0, DeckList deck1, string seed);
    public static IReadOnlyList<Event> Apply(GameState s, Command cmd);          // 原地修改，返回事件
    public static void LegalCommands(GameState s, int player, List<Command> buffer); // 枚举全部合法命令（含目标组合），写入复用的缓冲区
    public static GameState Clone(GameState s);                                  // 手写深拷贝
}

public static class Determinizer
{
    // 从某位玩家的视图采样出一个完整状态：对手手牌和双方牌库从剩余卡池中随机分配，伏笔内容同理
    public static GameState Sample(PlayerView view, CardDatabase db, KairosRng rng);
}
```

### 18.2 AI 分级（`Kairos.Ai` 程序集）
| 级别 | 实现 | 用途 |
| --- | --- | --- |
| L0 | `RandomAi`：随机合法命令 | 冒烟测试、模糊测试 |
| L1 | `GreedyAi`：贪心 + `Evaluator` | 新手 AI、平衡模拟的基线 |
| L2 | `Ismcts`：每步 500–2000 次迭代 | 主要平衡模拟、高难 AI |

**启发式评估特征（`Evaluator`）**：命烛差、场面总身材差、手牌数差、**时间差**（对手时标减自己时标，表示即将获得的行动窗口）、视界、时砂、咏唱的预期价值、伏笔数量。权重先手调，之后用模拟数据做回归。

**Unity 中运行 AI**：ISMCTS 放在 `Task.Run` 的后台线程中，处理状态的克隆，不接触 Unity 对象；结果通过主线程队列交回 `GameController`。WebGL 平台不支持线程，需要改为分帧迭代。

### 18.3 幽灵预览（`View/Preview.cs`）
```csharp
public struct MarkerPreview
{
    public int FinalMarker;
    public List<int> ChimesCrossed;
    public List<int> OmensCrossed;        // 只有位置，不含内容
    public List<int> ChannelsResolved;    // 物件 Oid
    public bool Kairos;
    public int OpponentWindow;            // FinalMarker − 对手时标（> 0 表示对手将获得的连续刻数）
}

public static MarkerPreview PreviewMarker(PlayerView view, Command cmd);
```
预览只计算确定性的部分（内部调用 `CostCalculator.Compute(consume: false)`），不推演伏笔内容和对手的抢拍。

---

## 19. 实体版与数字版的差异

规则完全一致。实体版额外提供：
- 钟盘外圈有 64 个卡槽，用来插放物件。
- 每位玩家有一张“小节记录卡”，用来记录 `BarCounters`（蓄势、先手之律、被推迟刻数）。
- 用横置表示疲惫，用一枚琥珀色标记表示停滞。

---

## 20. 验收测试用例（`Assets/Tests/EditMode/Rules`）

用 Unity Test Framework（NUnit）编写，放在 EditMode 中运行；同一批源文件也由 `Tools/Kairos.Tests` 链接，可以在 CI 中用 `dotnet test` 运行。测试名以用例 ID 开头。

```csharp
public class TurnOrderTests
{
    [Test]
    public void T02_TieGoesToLaterArrival()
    {
        var s = TestGame.Empty();                 // 测试工具：空场、默认 RulesConfig
        s.Players[0].Marker = 4; s.Players[0].LastMoveSeq = 7;
        s.Players[1].Marker = 4; s.Players[1].LastMoveSeq = 9;
        Assert.AreEqual(1, TurnOrder.NextActor(s));
    }

    [Test]
    public void T07_WaitCrossesTwoBarLines()
    {
        var s = TestGame.Empty();
        s.Players[0].Marker = 3; s.Players[1].Marker = 8;
        var events = GameEngine.Apply(s, new WaitCommand { Player = 0 });
        Assert.AreEqual(9, s.Players[0].Marker);
        Assert.AreEqual(2, s.Players[0].ChimesRung);
        Assert.AreEqual(2, events.OfType<ChimeEvent>().Count());   // 测试代码中允许使用 LINQ
    }
}
```

格式：**前置条件 → 操作 → 预期结果**。M 表示时标。

| ID | 规则 | 前置条件 | 操作 | 预期结果 |
| --- | --- | --- | --- | --- |
| T01 | R-4-1 | P0 M=3，P1 M=5 | 求下一个行动者 | P0 |
| T02 | R-4-2 | P0 M=4（seq 7），P1 M=4（seq 9） | 求下一个行动者 | P1 |
| T03 | R-3-1 | 开局，先行者为 P1 | 求下一个行动者 | P1；P0 起手 6 张 |
| T04 | R-7 | 卡牌时值 2，投入 3 时砂 | 计算费用 | 1；时砂 -3 |
| T05 | R-7-1 | ChimesRung=0，卡牌时值 3 | 合法性检查 | 不合法 |
| T06 | R-7-1 | ChimesRung=0，时值 4，2 时砂 | 合法性检查 | 合法，费用 2 |
| T07 | R-6-8 | P0 M=3，ChimesRung=0；P1 M=8 | P0 静候 | P0 → 9，依次在第 4、8 刻鸣钟，抽 2 张，ChimesRung=2 |
| T08 | R-6-5 | M=9，ChimesRung=2，后退到 7 后又打出时值 2 的牌 | 推进时标 | 越过第 8 刻时不鸣钟 |
| T09 | R-5-3 | M=2，打出时值 2 的沙漏 | 结算 | 契机成立，抽 2 张 |
| T10 | R-5-3 | M=62，打出时值 2 的牌 | 判定契机 | 第 64 刻不算，契机不成立 |
| T11 | R-5-4 | P0 连续行动两次，第二次宣言后 P1 抢拍 | P0 再行动一次 | 第三次行动没有连拍 |
| T12 | R-12-4 | P0 宣言攻击，P1 用破晓一击（1+1）抢拍 | P0 想再抢拍回去 | 不允许；P1 时标 +2 |
| T13 | R-5-6 | P0 用侍从 A 攻击，P1 抢拍消灭 A | 继续结算 | 攻击失效，P0 照付节奏耗时 |
| T14 | R-11-5 | P0 M=3，P1 M=7 | P0 放置伏笔，Offset=1 | Pos=6 ≤ 7，不合法；Offset=3 时 Pos=8，合法 |
| T15 | R-11-6 | 伏笔中实际盖的是普通侍从 | 被揭示 | 进入往昔，没有效果；命运织者仍然抽 1 张 |
| T16 | R-6-6 / R-11-6 | P0 的伏笔在第 9 刻，P1 M=7 | P0 用缓流推迟 P1 2 刻 | P1 越过第 9 刻，P1 是触发者 |
| T17 | R-6-2 | P0 自己的咏唱和 P1 的伏笔都在第 8 刻，P0 M=6 | P0 前进 2 刻 | 顺序：伏笔揭示 → 咏唱结算 → 鸣钟 |
| T18 | R-11-3 | 星陨锁定侍从 X，X 在咏唱结算前死亡 | 咏唱结算 | 不造成伤害，也不重新选择目标 |
| T19 | R-8-1 / R-8-3 | P1 场上有守卫 | P0 宣言攻击钟主 | 不合法；但如果 P1 用拦截者抢拍，P0 对守卫的攻击会被改为攻击拦截者 |
| T20 | R-13-2 | 侍从 A 被停滞 | 疾风连奏重整 A | 无效；A 在下一次鸣钟时不重整，再下一次鸣钟才重整 |
| T21 | R-10-3 | P0 命烛 12，M=20；P1 M=19；P0 受到 3 点伤害 | 检查点 | 命烛 9，后退到 max(15, 18) = 18；之后再降到 10 以下不再触发 |
| T22 | R-6-8 | P0 越过第 48、52 刻 | 鸣钟 | 依次受到 1、2 点暮钟伤害 |
| T23 | R-14-2 | 双方都到达第 64 刻，命烛 8 比 8，场攻 5 比 3 | 判定 | 场攻 5 的一方获胜 |
| T24 | R-10-4 | 手牌 8 张 | 抽牌 | 抽到的牌公开焚毁 |
| T25 | R-10-4 | 牌库为空 | 抽牌 | 受到 2 点伤害 |
| T26 | R-6-6 | P1 本小节已经被推迟 3 刻 | 再推迟 P1 3 刻 | 实际只推迟 1 刻 |
| T27 | R-7-4 | P0 预言 ATTACK，P1 下一个行动是攻击 | 结算 | 攻击费用 +1；P0 在检查点抽 2 张；预言被消费 |
| T28 | R-10-2 | 双方命烛都是 2。基础系列中没有能在同一个 op 里同时伤害双方的卡，因此用测试专用 op `DAMAGE_BOTH_PLAYERS 2` 构造 | 状态检查 | 判 Draw；如果先后由两个队列任务分别造成伤害，则先降到 0 的一方判负 |
| T29 | R-7 | 先手之律在场，本小节第一次连拍，打出时值 1 的牌 | 计算费用 | 仍为 1（下限）；折扣标记为已用 |
| T30 | R-7 | P1 场上有日晷法官 | P0 打出星陨 | 费用 2 |
| T31 | R-12-5 | 自己行动时 | 正常打出破晓一击 | 费用 1，不付税 |
| T32 | R-12-5 | 自己行动时 | 正常打出拦截者 | 不合法（interjectOnly） |
| T33 | R-14-3 | P1 已到达第 64 刻，P0 M=50 | P0 静候 | P0 前进到 64，依次越过第 52、56、60 刻，受到 2、3、4 点暮钟伤害，然后时尽判定 |
| T34 | R-11-4 | P0 到达第 64 刻时还有未结算的咏唱 | — | 咏唱直接进入往昔 |
| T35 | R-6-7 | （扩展预留）一张行动效果为“加速自己 3 刻”、费用为 1 的牌 | 检查点 | 时标最终仍为 marker + 1 |
| T36 | R-9-4 | 构造一张在 `ON_DEATH` 中使用 `SCRY` 的测试卡 | 加载卡牌数据 | `CardValidator` 拒绝加载 |
| T37 | R-9-4 | P0 打出观星学徒，引擎等待选择 | 在等待期间调用 `GameEngine.Clone` 并提交 `ChooseCommand` | 原状态与克隆的状态分别继续，结果一致 |
| T38 | R-13-1a | 往昔中有观星学徒 | 亡者回响召回它 | 观星学徒入场且疲惫，不触发预见 |
| T39 | 确定性 | 同一种子、同一配置、同一命令序列 | 分别在 Unity EditMode 和 dotnet test 中回放 | 最终状态的哈希值相同 |

---

## 21. 待调参数与模拟计划

| 参数（`RulesConfig` 字段） | 初始值 | 扫描范围 | 主要观察指标 |
| --- | --- | --- | --- |
| `LifeStart` | 20 | 16–26 | 对局时长、进入暮钟的比例 |
| `BarLength` | 4 | 3–5 | 抽牌节奏、换手间隔 |
| `HorizonBase` | 2 | 2–4（或关闭视界） | 大牌出场的时间点、先行者胜率 |
| `InterjectTax` | 1 | 0–2 | 每局抢拍次数 |
| `OmenSetCost` | 2 | 1–3 | 伏笔使用率、虚张声势比例 |
| `SecondPlayerBonusCards` | 1 | 0–1，或改为后行者从第 −1 刻开始 | 先行者胜率 |
| `RefluxRewind` | 2 | 0–3 | 翻盘率 |
| `PushPerBarMax` | 4 | 3–6 | 控制卡组的胜率 |

**模拟器（`Tools/Kairos.Sim`，.NET 8 控制台）**

```
dotnet run -c Release --project Tools/Kairos.Sim -- \
  --cards Assets/Kairos/Data/cards.json \
  --deckA PRECON_DAWN_NIGHT --deckB PRECON_NOON_ETERNAL \
  --ai ismcts:1000 --games 2000 --threads 16 \
  --sweep LifeStart=16..26:2 --sweep HorizonBase=2..4 \
  --out results/sweep_001.csv
```

- 每局使用独立的种子；用 `Parallel.For` 跑多局，每个线程有自己的状态和 RNG。
- 输出 CSV：参数组合、先行者胜率、平均对局刻数、平均换手间隔、抢拍次数、进入暮钟比例、逆流翻盘率、单卡胜率贡献。
- 对 2 套预组 × 每组参数，进行镜像对战和交叉对战。达标的参数组合（见概述文档第 10.3 节）写回 `RulesConfigAsset`，再进入真人测试。
