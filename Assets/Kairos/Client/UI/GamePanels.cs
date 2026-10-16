using System;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        void DrawSidebar()
        {
            var controls = Box(root, new Rect(32, 674, 1156, 24), background);
            Label(controls, new Rect(0, 0, 1156, 24), ActionHint(), 15, gold);
            var side = Box(root, new Rect(1210, 90, 358, 783), panel);
            bool active = CanAct && View.MainPhase && (View.Pending == null || View.Pending.Kind == PendingKind.Interject);
            bool normal = active && View.Pending == null;
            timerLabel = Label(side, new Rect(16, 12, 326, 30), View.Pending?.Kind == PendingKind.Interject ? "抢拍窗口" : CanAct ? "你的行动" : "对手行动中", 23, gold);
            var keeper = Database.Keeper(View.Players[viewer].Keeper);
            Button(side, new Rect(14, 52, 330, 43), "时器 · " + keeper.Ability.Name + " (" + keeper.Ability.Cost + " 刻)", () => { ClearSelection(); ability = true; RebuildBattle(); }, normal && View.Players[viewer].ChimesRung >= View.Players[viewer].AbilityReadyAt, ability ? new Color32(72, 92, 87, 255) : background, 18);
            Button(side, new Rect(14, 102, 157, 38), "蓄势 · 2 刻", () => Submit(new Command { Player = viewer, Kind = CommandKind.Focus }), normal && View.Players[viewer].FocusUsed < View.Config.FocusPerBar, background);
            Button(side, new Rect(182, 102, 162, 38), "静候", () => Submit(new Command { Player = viewer, Kind = CommandKind.Wait }), normal, background);
            var draft = Draft();
            string details = selectedCard != 0 ? Database.Card(GameEngine.FindCard(View.Players[viewer].Hand, selectedCard).Key).Text : ability ? keeper.Text : selectedUnit != 0 ? "选择敌方侍从或钟主。\n守卫在场时必须先攻击守卫。" : "点击手牌出牌，或点击己方侍从攻击。\n点击时轴物件可将其选为目标。";
            Label(side, new Rect(16, 152, 324, 79), details, 16, ink);
            if (selectedCard != 0)
            {
                Button(side, new Rect(14, 236, 160, 35), placingOmen ? "盖放伏笔" : "正面打出", () => { placingOmen = !placingOmen; selectedTarget = default; RebuildBattle(); }, normal, background, 16);
                if (placingOmen)
                {
                    Button(side, new Rect(182, 236, 45, 35), "−", () => { offset = Math.Max(1, offset - 1); RebuildBattle(); }, normal, background);
                    Label(side, new Rect(230, 242, 54, 28), "+" + offset, 18, gold, TextAnchor.MiddleCenter);
                    Button(side, new Rect(292, 236, 50, 35), "+", () => { offset = Math.Min(6, offset + 1); RebuildBattle(); }, normal, background);
                }
                else
                {
                    Button(side, new Rect(182, 236, 160, 35), "时砂 " + sand + "（点击增加）", () => { sand = (sand + 1) % (View.Players[viewer].Sand + 1); RebuildBattle(); }, active && View.Players[viewer].Sand > 0, background, 14);
                    var d = Database.Card(GameEngine.FindCard(View.Players[viewer].Hand, selectedCard).Key);
                    if (d.Choice != null) Button(side, new Rect(14, 276, 330, 32), "预言：" + prophecy + "（点击切换）", () => { prophecy = d.Choice[(Array.IndexOf(d.Choice, prophecy) + 1) % d.Choice.Length]; RebuildBattle(); }, active, background, 16);
                }
            }
            else Label(side, new Rect(16, 236, 326, 65), "时器冷却：" + Math.Max(0, View.Players[viewer].AbilityReadyAt - View.Players[viewer].ChimesRung) + " 次鸣钟\n" + (View.TopCard == null ? "每小节最多蓄势一次。" : "牌库顶：" + Database.Card(View.TopCard).Name), 16, muted);
            Label(side, new Rect(16, 315, 326, 30), "目标：" + TargetName(selectedTarget), 16, teal);
            string reason = draft == null ? null : GameEngine.IllegalReason(State, draft);
            string preview = "选择行动后预览时间。";
            if (draft != null)
            {
                int cost = GameEngine.Cost(State, draft); int landing = Math.Min(View.Config.TimelineEnd, View.Players[viewer].Marker + cost);
                preview = "预计 " + View.Players[viewer].Marker + " → " + landing + "，支付 " + cost + " 刻\n" + (reason ?? "合法 · 可确认行动");
            }
            previewLabel = Label(side, new Rect(16, 352, 326, 59), preview, 16, reason == null ? gold : red);
            Button(side, new Rect(14, 417, 246, 44), View.Pending?.Kind == PendingKind.Interject ? "确认抢拍" : "确认行动", () => { var cmd = Draft(); if (cmd != null) Submit(cmd); }, active && draft != null && reason == null, new Color32(30, 88, 85, 255), 21);
            Button(side, new Rect(270, 417, 74, 44), "取消", () => { ClearSelection(); RebuildBattle(); }, active, background, 16);
            if (View.Pending?.Kind == PendingKind.Interject) Button(side, new Rect(14, 467, 330, 36), "放弃抢拍", () => Submit(new Command { Player = viewer, Kind = CommandKind.Pass }), active, background);
            else Label(side, new Rect(16, 467, 326, 33), "未来物件（可点击选取）", 17, gold);
            var scrollRoot = Box(side, new Rect(14, 507, 330, 98), background);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Box(scrollRoot, new Rect(0, 0, 330, 98), background); viewport.gameObject.AddComponent<RectMask2D>();
            var content = Element(viewport, "Objects", new Rect(0, 0, 326, Math.Max(98, View.Timeline.Count * 36))); scroll.viewport = viewport; scroll.content = content; scroll.movementType = ScrollRect.MovementType.Clamped;
            int row = 0; foreach (var o in View.Timeline)
            {
                var target = new Target(TargetKind.Object, o.Id);
                string name = o.Card == null ? "未知伏笔" : Database.Card(o.Card.Key).Name;
                string lockText = o.Kind == ObjectKind.Channel && o.Context != null ? " → " + TargetName(o.Context.Target) : "";
                var objectButton = Button(content, new Rect(0, row++ * 36, 326, 32), "@" + o.Pos + " P" + (o.Owner + 1) + " " + name + lockText, () => SelectTarget(target), active, selectedTarget.Equals(target) ? new Color32(92, 77, 43, 255) : panel, 14);
                Highlight(objectButton, TargetAvailable(target));
            }
            if (row == 0) Label(content, new Rect(10, 14, 300, 60), "时轴上暂无咏唱或伏笔。", 16, muted);
            Label(side, new Rect(16, 618, 326, 26), "时序记录", 17, gold);
            string log = ""; int count = 0;
            for (int i = history.Count - 1; i >= 0 && count < 5; i--) if (history[i].PrivateTo < 0 || history[i].PrivateTo == viewer) { log = history[i].Text + "\n" + log; count++; }
            Label(side, new Rect(16, 650, 326, 125), message ?? log, 14, message == null ? muted : gold);
        }
        void DrawMulligan()
        {
            var bar = Box(root, new Rect(80, 345, 1060, 105), background);
            Label(bar, new Rect(20, 15, 665, 76), "起手调度 · P" + (viewer + 1) + "\n点击下方手牌标记换牌；也可全部保留。已选择 " + selection.Count + " 张", 21, gold);
            Button(bar, new Rect(730, 29, 290, 53), "确认调度", () => Submit(new Command { Player = viewer, Kind = CommandKind.Mulligan, Selection = selection.ToArray() }), true, new Color32(30, 88, 85, 255), 23);
        }
        RectTransform Modal(string title)
        {
            var shade = Box(root, new Rect(0, 73, 1600, 827), new Color(0.025f, 0.04f, 0.06f, 0.97f));
            var box = Box(shade, new Rect(230, 95, 1140, 620), panel);
            Label(box, new Rect(40, 30, 1055, 70), title, 33, gold); return box;
        }
        void DrawChoice()
        {
            var pending = View.Pending;
            var box = Modal(pending.Kind == PendingKind.Summon ? "往昔召回" : pending.Kind == PendingKind.Scry ? "预见 · 编排未来" : "窥时 · 选择一张加入手牌");
            Label(box, new Rect(40, 107, 1050, 66), pending.Kind == PendingKind.Summon ? "点击一张侍从召回；不会再次触发部署。" : "按希望的顺序点击卡牌。可再次点击取消。\n预见：前 N 张置顶，其余按所选顺序置底；窥时：第一张入手，其余置底。", 20, ink);
            var cards = pending.Kind == PendingKind.Summon ? View.Players[viewer].Past : View.Players[viewer].Deck;
            var area = Box(box, new Rect(40, 188, 1060, 230), background); var scroll = area.gameObject.AddComponent<ScrollRect>(); scroll.vertical = false;
            var vp = Box(area, new Rect(0, 0, 1060, 230), background); vp.gameObject.AddComponent<RectMask2D>();
            var content = Element(vp, "Choices", new Rect(0, 0, Math.Max(1060, pending.Options.Count * 240), 230)); scroll.viewport = vp; scroll.content = content; scroll.movementType = ScrollRect.MovementType.Clamped;
            for (int i = 0; i < pending.Options.Count; i++)
            {
                int id = pending.Options[i]; var card = GameEngine.FindCard(cards, id); var d = Database.Card(card.Key); int order = selection.IndexOf(id);
                Button(content, new Rect(12 + i * 240, 12, 220, 205), (order >= 0 ? "顺序 " + (order + 1) + "\n" : "") + d.Name + "  " + d.Time + "刻\n\n" + d.Text, () =>
                {
                    if (pending.Kind == PendingKind.Summon) { Submit(new Command { Player = viewer, Kind = CommandKind.Choose, Selection = new[] { id } }); return; }
                    if (!selection.Remove(id)) selection.Add(id); choiceTop = Math.Min(choiceTop, selection.Count); RebuildBattle();
                }, true, order >= 0 ? new Color32(92, 77, 43, 255) : panel, 20);
            }
            if (pending.Kind != PendingKind.Summon)
            {
                if (pending.Kind == PendingKind.Scry) Button(box, new Rect(40, 447, 410, 48), "置顶 " + choiceTop + " 张（点击切换）", () => { choiceTop = (choiceTop + 1) % (pending.Options.Count + 1); RebuildBattle(); });
                Button(box, new Rect(500, 447, 270, 48), "保持原顺序", () => { selection.Clear(); selection.AddRange(pending.Options); choiceTop = selection.Count; RebuildBattle(); });
                Button(box, new Rect(800, 447, 300, 48), "确认编排", () => Submit(new Command { Player = viewer, Kind = CommandKind.Choose, Selection = selection.ToArray(), TopCount = choiceTop }), selection.Count == pending.Options.Count, new Color32(30, 88, 85, 255));
            }
        }
        void DrawResult()
        {
            var box = Modal(State.Outcome == Outcome.Draw ? "时间账单 · 平局" : "时间账单 · " + (State.Outcome == Outcome.P0Win ? "P1" : "P2") + " 获胜");
            for (int p = 0; p < 2; p++)
            {
                int attacks = 0, cards = 0, waits = 0; foreach (var c in State.Commands) if (c.Player == p) { if (c.Kind == CommandKind.Attack) attacks++; if (c.Kind == CommandKind.Play || c.Kind == CommandKind.Interject) cards++; if (c.Kind == CommandKind.Wait) waits++; }
                Label(box, new Rect(40 + p * 550, 129, 500, 190), "P" + (p + 1) + "  " + Database.Keeper(State.Players[p].Keeper).Name + "\n\n最终时标 " + State.Players[p].Marker + " / 64    命烛 " + State.Players[p].Life + "\n打牌 " + cards + " 次    攻击 " + attacks + " 次    静候 " + waits + " 次", 23, ink);
            }
            Label(box, new Rect(40, 340, 1050, 90), message ?? "你的每次宣言都已记录，可导出确定性回放。\n时间不再替你做决定。", 20, muted);
            Button(box, new Rect(40, 474, 295, 64), "导出本局回放", SaveReplay, true, background, 23);
            if (Mode == DemoMode.Tower) Button(box, new Rect(367, 474, 350, 64), "继续旅程", ContinueTower, true, new Color32(30, 88, 85, 255), 23);
            else Button(box, new Rect(367, 474, 350, 64), "再来一局", () => StartMatch(Mode), true, new Color32(30, 88, 85, 255), 23);
            Button(box, new Rect(750, 474, 350, 64), "返回主菜单", ShowMenu, true, background, 23);
        }
        void DrawHelp()
        {
            string[] tabs = { "入门与操作", "时轴与鸣钟", "战斗与关键词", "抢拍与未来", "资源与终局" };
            var box = Modal("规则手册 · 不必一次记住所有规则");
            for (int i = 0; i < tabs.Length; i++)
            {
                int page = i;
                Button(box, new Rect(40 + i * 213, 104, 202, 47), tabs[i], () => OpenRules(page), true, helpPage == i ? new Color32(30, 88, 85, 255) : background, 20);
            }
            string heading, body, tip;
            switch (helpPage)
            {
                case 1:
                    heading = "时标不是法力：越靠前，越要等对手。";
                    body = "时标较小的一方行动；相同时，后到达该位置的一方先行。\n例如你在 1 刻、对手在 2 刻：花 1 刻到达 2，你后到，仍由你行动。\n每次到达新的 4 / 8 / 12…60 刻钟点：抽 1 张，侍从重整，视界 +1。\n视界是单次行动的最终费用上限，开局 2，最高 7；时砂可降低费用。\n每条钟点只鸣一次，后退后不会重复抽牌；64 刻终点不鸣钟。\n「契机」：宣言时预计落点恰好在 4～60 刻钟点；「连拍」：上次宣言也是你。";
                    tip = "看时轴金色落点预览。它不预测对手抢拍或隐藏伏笔带来的改变。"; break;
                case 2:
                    heading = "先点己方侍从，再点敌人，最后确认。";
                    body = "攻 / 血 / 节奏：每次攻击的伤害、剩余生命、攻击所花时间。\n新侍从通常疲惫；「迅捷」可立即攻击。攻击后疲惫，等己方鸣钟重整。\n敌人有「守卫」时，攻击必须先选守卫；一般伤害术式不受守卫限制。\n侍从交战同时互相造成伤害，钟主不反击；「护盾」抵消一次伤害。\n「停滞」立即疲惫，不能重整；下一次鸣钟只解除，再下一次才重整。\n「部署」入场触发；「余音」死亡触发；「蓄力」在己方鸣钟时 +1/+1。";
                    tip = "金色边框指出当前所选行动的合法目标。没有目标时可取消，换一个行动。"; break;
                case 3:
                    heading = "未来是公开的，只有伏笔藏着答案。";
                    body = "「抢拍」在对方出牌、攻击或用时器时响应，通常多付 1 刻税。\n抢拍先结算，只允许一层；原行动若失效，仍支付时间。可点「放弃抢拍」。\n「咏唱 X」公开放在宣言时标 + 费用 + X；自己的时标经过时才生效。\n咏唱目标已锁定，目标离场不重选。右侧未来物件列表可点击选为目标。\n「伏笔」花 2 刻盖任意手牌：放到支付后 +1～+6 且双方尚未到达的位置。\n任一方先经过就揭示；普通牌是无效果的虚张声势，真正伏笔执行揭示效果。";
                    tip = "伊琳每小节第一次抢拍免税。仅限抢拍的牌不能在平时正面打出。"; break;
                case 4:
                    heading = "蓄势补牌，静候让出行动权。";
                    body = "「蓄势」2 刻抽 1 张，每小节一次；「静候」走到对手时标 +1（最多 64）。\n「时器」是钟主技能，冷却按己方鸣钟次数；「时砂」只抵扣出牌/抢拍费用。\n手牌最多 8 张，溢出公开焚毁；侍从最多 5 个，刻印 3 个，咏唱 2 个。\n「预见」按想要的顺序点牌，前 N 张置顶，其余置底；窥时第一张入手。\n命烛首次降至 10 或以下且未死亡，在检查点逆流后退 2 刻（落后差最多 4）。\n48 / 52 / 56 / 60 刻暮钟各扣 1 / 2 / 3 / 4；空牌库每次抽牌扣 2 命烛。";
                    tip = "命烛归零即输；双方都到 64 刻，比命烛 → 总场攻 → 手牌数，相同则平局。"; break;
                default:
                    heading = "目标：把对方命烛（生命）降至 0。";
                    body = "你的牌与侍从在下方，对手在上方。没有回合，也没有要积攒的法力。\n卡牌左上角的“刻”就是费用：先执行效果，再让自己的时标前进。\n操作顺序：点手牌 / 点己方侍从 → 如需要，点金色目标 → 确认行动。\n无目标侍从可以直接出；卡牌说明里的特殊词可在其他分页查询。\n正式开局可调度一次：点要换的牌，再确认；新手建议先保留 1～2 刻牌。\n热座换人要先交接屏幕再揭开牌面；本地帮助窗口暂停 AI 与抢拍倒计时。";
                    tip = "第一次玩？主菜单的「新手教学」会带你亲手赢一局，约 2 分钟。"; break;
            }
            Label(box, new Rect(40, 174, 1050, 46), heading, 25, teal);
            Label(box, new Rect(40, 237, 1050, 243), body, 22, ink);
            Label(box, new Rect(40, 488, 1050, 44), tip, 18, gold);
            Button(box, new Rect(770, 549, 330, 47), State == null ? "返回主菜单" : "回到当前对局", CloseRules, true, new Color32(30, 88, 85, 255), 22);
        }
        void DrawLeave()
        {
            if (lesson != null)
            {
                var tutorialBox = Modal("暂时离开教学？");
                Label(tutorialBox, new Rect(40, 160, 1050, 120), "退出不会记为完成。\n可以随时从主菜单重新开始；教学内没有时间压力。", 25, ink);
                Button(tutorialBox, new Rect(40, 380, 500, 70), "继续教学", () => { leaving = false; RebuildBattle(); });
                Button(tutorialBox, new Rect(600, 380, 500, 70), "退出到主菜单", ShowMenu); return;
            }
            var box = Modal("离开本局？");
            Label(box, new Rect(40, 160, 1050, 110), "返回菜单会丢弃当前对局；PVE 旅程不会自动存档。\n也可以认输，进入结算页导出回放。", 24, ink);
            Button(box, new Rect(40, 370, 300, 68), "继续游戏", () => { leaving = false; RebuildBattle(); });
            Button(box, new Rect(400, 370, 300, 68), "认输", () => { leaving = false; Submit(new Command { Player = viewer, Kind = CommandKind.Concede }); }, State.Outcome == Outcome.Ongoing);
            Button(box, new Rect(760, 370, 300, 68), "返回菜单", ShowMenu);
        }
        void ShowCampaign()
        {
            Clear(); Header("逆流之塔  /  第一幕 · 打卡之城  /  基础流程 Demo");
            Label(root, new Rect(100, 126, 1400, 70), Run.Stage == RunStage.Complete ? "你不必按它的指标赢。" : Run.Stage == RunStage.Failed ? "命烛熄灭，但时间还会重来。" : "腕钟失灵之后", 43, gold);
            Label(root, new Rect(104, 209, 1350, 65), "命烛 " + Run.Life + " / 20    |    牌组 " + Run.Deck.Cards.Count + " 张    |    " + Database.Keeper(Run.Deck.Keeper).Name + "\n短流程：战斗 → 奖励 → 休整 → 战斗 → 奖励 → 休整 → Boss", 22, muted);
            if (Run.Stage == RunStage.Map)
            {
                for (int i = 0; i < 3; i++)
                {
                    int node = i; bool current = i == Run.Encounter;
                    Button(root, new Rect(104 + i * 457, 358, 419, 202), (i == 2 ? "BOSS" : "战斗 " + (i + 1)) + "\n\n" + CampaignRun.EnemyNames[i] + "\n" + (i < Run.Encounter ? "已通过" : current ? "进入战斗" : "尚未抵达"), StartTowerBattle, current, current ? new Color32(30, 75, 79, 255) : panel, 25);
                }
                Label(root, new Rect(104, 611, 1370, 92), "城市仍按旧日程运转。你可以选择下一步，不必赶路。\n当前敌人复用卡牌 AI 与公开的下一步行动意图；剧情专属截止/KPI 机制留待扩展。", 22, muted);
            }
            else if (Run.Stage == RunStage.Reward)
            {
                Label(root, new Rect(104, 301, 1320, 50), "战利品 · 选择一张牌加入牌组", 28, teal);
                for (int i = 0; i < Run.Rewards.Count; i++)
                {
                    string key = Run.Rewards[i]; var d = Database.Card(key);
                    Button(root, new Rect(104 + i * 457, 379, 419, 226), d.Name + "  /  " + d.Time + " 刻\n\n" + d.Text, () => { Run.ChooseReward(key); ShowCampaign(); }, true, panel, 24);
                }
                Button(root, new Rect(1050, 659, 390, 61), "跳过奖励", () => { Run.ChooseReward(null); ShowCampaign(); });
            }
            else if (Run.Stage == RunStage.Rest)
            {
                Label(root, new Rect(104, 315, 1300, 50), "调律 · 让齿轮歇一歇", 30, teal);
                Button(root, new Rect(104, 410, 620, 130), "修复命烛 +6\n恢复后前往下一节点", () => { Run.Rest(); ShowCampaign(); }, true, panel, 26);
                Button(root, new Rect(770, 410, 660, 130), "精简牌组\n选择移除一张牌，替代治疗", ShowRemoveCard, Run.Deck.Cards.Count > 10, panel, 26);
            }
            else
            {
                Label(root, new Rect(104, 346, 1330, 160), Run.Stage == RunStage.Complete ? "第一幕短流程已完成。\n完整三幕、专属敌人、遗物与叙事分支，将在后续版本展开。" : "本次旅程结束。换一套预组，或先在对战练习中熟悉时轴。", 28, ink);
                Button(root, new Rect(104, 612, 500, 80), "重新出发", StartTower, true, new Color32(30, 75, 79, 255), 27);
            }
            Button(root, new Rect(1230, 800, 280, 54), "返回主菜单", ShowMenu);
        }
        void ShowRemoveCard()
        {
            Clear(); Header("调律 / 选择移除一张牌");
            for (int i = 0; i < Run.Deck.Cards.Count; i++)
            {
                int index = i; var d = Database.Card(Run.Deck.Cards[i]);
                Button(root, new Rect(70 + i % 5 * 295, 140 + i / 5 * 185, 275, 165), d.Name + "\n" + d.Text, () => { Run.Rest(index); ShowCampaign(); }, true, panel, 20);
            }
            Button(root, new Rect(1190, 810, 300, 50), "取消", ShowCampaign);
        }
    }
}
