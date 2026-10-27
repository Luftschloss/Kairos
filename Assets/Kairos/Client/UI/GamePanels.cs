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
            var controls = Box(root, new Rect(32, 681, 1156, 19), background);
            Label(controls, new Rect(0, 0, 1156, 19), ActionHint(), 15, WindowColor);
            var side = ui.Subpanel(root, "BattleSidebar", new Rect(1210, 90, 358, 783));
            bool active = BattleInput;
            bool normal = active && actionWindow.Normal;
            timerLabel = Label(side, new Rect(16, 12, 326, 30), actionWindow.Response ? "仅可抢拍" : actionWindow.Normal ? "你可行动 · 选择下一步" : "暂不可出牌 · 等待提示", 21, WindowColor);
            UpdateResponseClock();
            var keeper = Database.Keeper(View.Players[viewer].Keeper);
            var abilityButton = Button(side, new Rect(14, 52, 330, 43), "时器 · " + keeper.Ability.Name + " (" + keeper.Ability.Cost + " 刻)", () => { ClearSelection(); ability = true; RebuildBattle(); }, normal && HasAction(CommandKind.Ability), ability ? new Color32(72, 92, 87, 255) : background, 18);
            if (ability) sourceRect = abilityButton.GetComponent<RectTransform>();
            Button(side, new Rect(14, 102, 157, 38), ActionLanding(CommandKind.Focus, "蓄势"), () => SelectAction(CommandKind.Focus), normal && HasAction(CommandKind.Focus), background, 16);
            Button(side, new Rect(182, 102, 162, 38), ActionLanding(CommandKind.Wait, "静候"), () => SelectAction(CommandKind.Wait), normal && HasAction(CommandKind.Wait), background, 16);
            var draft = Draft();
            string details = selectedCard != 0 ? Database.Card(GameEngine.FindCard(View.Players[viewer].Hand, selectedCard).Key).Text : ability ? keeper.Text : selectedUnit != 0 ? "选择敌方侍从或钟主。\n守卫在场时必须先攻击守卫。" : actionWindow.Explanation;
            if (View.Pending?.Kind == PendingKind.Interject) details = DeclarationText();
            else if (Mode == DemoMode.Tower && !CanAct) details = AiIntent() ?? details;
            else if (selectedAction.HasValue) details = selectedAction == CommandKind.Focus ? "蓄势：抽 1 张牌，随后支付时间。\n先看中央鸣钟预览，再确认。" : "静候不是结束回合：付出时间前进到对手时标 +1（最多到终点）。\n先看中央预览，再确认。";
            cardDetailsLabel = Label(side, new Rect(16, 152, 324, 79), details, 17, ink);
            if (selectedCard != 0)
            {
                Button(side, new Rect(14, 236, 160, 35), placingOmen ? "盖放伏笔" : "正面打出", () => { placingOmen = !placingOmen; selectedTarget = default; offset = View.Config.OmenOffsetMin; timelineStart = -1; RebuildBattle(); }, normal && Database.Card(GameEngine.FindCard(View.Players[viewer].Hand, selectedCard).Key).Type != "OMEN", background, 16);
                if (placingOmen)
                {
                    Label(side, new Rect(182, 239, 160, 65), "点中央金色刻度\n放置 @" + Preview(draft).FuturePosition, 16, gold);
                }
                else
                {
                    Button(side, new Rect(181, 236, 36, 35), "−", () => { sand--; RebuildBattle(); }, active && sand > 0, background);
                    Label(side, new Rect(219, 242, 84, 25), "时砂 " + sand, 16, gold, TextAnchor.MiddleCenter);
                    Button(side, new Rect(305, 236, 37, 35), "+", () => { sand++; RebuildBattle(); }, active && sand < View.Players[viewer].Sand, background);
                    var d = Database.Card(GameEngine.FindCard(View.Players[viewer].Hand, selectedCard).Key);
                    if (d.Choice != null) Button(side, new Rect(14, 276, 330, 32), "预言：" + prophecy + "（点击切换）", () => { prophecy = d.Choice[(Array.IndexOf(d.Choice, prophecy) + 1) % d.Choice.Length]; RebuildBattle(); }, active, background, 16);
                }
            }
            else Label(side, new Rect(16, 236, 326, 65), "时器冷却：" + Math.Max(0, View.Players[viewer].AbilityReadyAt - View.Players[viewer].ChimesRung) + " 次鸣钟\n" + (View.TopCard == null ? "每小节最多蓄势一次。" : "牌库顶：" + Database.Card(View.TopCard).Name), 16, muted);
            Label(side, new Rect(16, 315, 326, 30), "目标：" + TargetName(selectedTarget), 16, teal);
            string reason = draft == null ? null : GameEngine.IllegalReason(State, draft);
            string preview = actionWindow.Response ? "金色亮边可抢拍；其他牌此时不能出。" : actionWindow.Normal ? "青色亮边可出牌；灰牌可悬停查看原因。" : actionWindow.Explanation;
            if (draft != null)
            {
                int cost = GameEngine.Cost(State, draft); int landing = Math.Min(View.Config.TimelineEnd, View.Players[viewer].Marker + cost);
                preview = "预计 " + View.Players[viewer].Marker + " → " + landing + "，支付 " + cost + " 刻\n" + (reason ?? "合法 · 可确认行动");
            }
            previewLabel = Label(side, new Rect(16, 352, 326, 59), preview, 16, reason == null ? gold : red);
            Button(side, new Rect(14, 417, 246, 44), View.Pending?.Kind == PendingKind.Interject ? "确认抢拍" : "确认行动", ConfirmDraft, active && LegalDraft() != null, new Color32(30, 88, 85, 255), 21);
            Button(side, new Rect(270, 417, 74, 44), "取消", () => { ClearSelection(); RebuildBattle(); }, active, background, 16);
            if (View.Pending?.Kind == PendingKind.Interject) Button(side, new Rect(14, 467, 330, 36), "放弃抢拍", () => Submit(new Command { Player = viewer, Kind = CommandKind.Pass }), active, background);
            else Label(side, new Rect(16, 467, 326, 33), "未来物件（可点击选取）", 17, gold);
            var scrollRoot = Box(side, new Rect(14, 507, 330, 98), background);
            var scroll = scrollRoot.GetComponent<ScrollRect>() ?? scrollRoot.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var viewport = Box(scrollRoot, new Rect(0, 0, 330, 98), background); if (viewport.GetComponent<RectMask2D>() == null) viewport.gameObject.AddComponent<RectMask2D>();
            var content = Element(viewport, "Objects", new Rect(0, 0, 326, Math.Max(98, View.Timeline.Count * 36))); scroll.viewport = viewport; scroll.content = content; scroll.movementType = ScrollRect.MovementType.Clamped;
            int row = 0; foreach (var o in View.Timeline)
            {
                var target = new Target(TargetKind.Object, o.Id);
                string name = o.Card == null ? "未知伏笔" : Database.Card(o.Card.Key).Name;
                string lockText = o.Kind == ObjectKind.Channel && o.Context != null ? " → " + TargetName(o.Context.Target) : "";
                var objectButton = Button(content, new Rect(0, row++ * 36, 326, 32), "@" + o.Pos + " P" + (o.Owner + 1) + " " + name + lockText, () => SelectTarget(target), active, selectedTarget.Equals(target) ? new Color32(92, 77, 43, 255) : panel, 14);
                BindTarget(objectButton, target);
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
            var bar = ui.Subpanel(root, "Mulligan", new Rect(80, 345, 1060, 105));
            Label(bar, new Rect(20, 15, 665, 76), "起手调度 · P" + (viewer + 1) + "\n点击下方手牌标记换牌；也可全部保留。已选择 " + selection.Count + " 张", 21, gold);
            Button(bar, new Rect(730, 29, 290, 53), "确认调度", () => Submit(new Command { Player = viewer, Kind = CommandKind.Mulligan, Selection = selection.ToArray() }), true, new Color32(30, 88, 85, 255), 23);
        }
        RectTransform Modal(string id, string title)
        {
            var box = ui.ShowModal(id);
            Label(box, new Rect(40, 30, 1055, 70), title, 33, gold); return box;
        }
        void DrawChoice()
        {
            var pending = View.Pending;
            var box = Modal("Choice", pending.Kind == PendingKind.Summon ? "往昔召回" : pending.Kind == PendingKind.Scry ? "预见 · 编排未来" : "窥时 · 选择一张加入手牌");
            Label(box, new Rect(40, 107, 1050, 66), pending.Kind == PendingKind.Summon ? "点击一张侍从召回；不会再次触发部署。" : "按希望的顺序点击卡牌。可再次点击取消。\n预见：前 N 张置顶，其余按所选顺序置底；窥时：第一张入手，其余置底。", 20, ink);
            var cards = pending.Kind == PendingKind.Summon ? View.Players[viewer].Past : View.Players[viewer].Deck;
            var area = Box(box, new Rect(40, 188, 1060, 230), background); var scroll = area.GetComponent<ScrollRect>() ?? area.gameObject.AddComponent<ScrollRect>(); scroll.vertical = false;
            var vp = Box(area, new Rect(0, 0, 1060, 230), background); if (vp.GetComponent<RectMask2D>() == null) vp.gameObject.AddComponent<RectMask2D>();
            var content = Element(vp, "Choices", new Rect(0, 0, Math.Max(1060, pending.Options.Count * 240), 230)); scroll.viewport = vp; scroll.content = content; scroll.movementType = ScrollRect.MovementType.Clamped;
            for (int i = 0; i < pending.Options.Count; i++)
            {
                int id = pending.Options[i]; var card = GameEngine.FindCard(cards, id); var d = Database.Card(card.Key); int order = selection.IndexOf(id);
                var visual = CardWidget(content, "CollectionTile", new Rect(12 + i * 240, 12, 220, 205));
                BindCard(visual, d, order >= 0 ? "顺序 " + (order + 1) : "点击选择", true, order >= 0);
                visual.Interaction.onClick.AddListener(() =>
                {
                    if (pending.Kind == PendingKind.Summon) { Submit(new Command { Player = viewer, Kind = CommandKind.Choose, Selection = new[] { id } }); return; }
                    if (!selection.Remove(id)) selection.Add(id); choiceTop = Math.Min(choiceTop, selection.Count); RebuildBattle();
                });
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
            var box = Modal("Result", State.Outcome == Outcome.Draw ? "时间账单 · 平局" : "时间账单 · " + (State.Outcome == Outcome.P0Win ? "P1" : "P2") + " 获胜");
            for (int p = 0; p < 2; p++)
            {
                int attacks = 0, cards = 0, waits = 0; foreach (var c in State.Commands) if (c.Player == p) { if (c.Kind == CommandKind.Attack) attacks++; if (c.Kind == CommandKind.Play || c.Kind == CommandKind.Interject) cards++; if (c.Kind == CommandKind.Wait) waits++; }
                Label(box, new Rect(40 + p * 550, 129, 500, 190), "P" + (p + 1) + "  " + Database.Keeper(State.Players[p].Keeper).Name + "\n\n最终时标 " + State.Players[p].Marker + " / 64    命烛 " + State.Players[p].Life + "\n打牌 " + cards + " 次    攻击 " + attacks + " 次    静候 " + waits + " 次", 23, ink);
            }
            Label(box, new Rect(40, 320, 1050, 115), message ?? ReceiptText(), 20, gold);
            Button(box, new Rect(40, 453, 250, 55), "导出本局回放", SaveReplay, true, background, 20);
            if (Mode == DemoMode.Tower) Button(box, new Rect(305, 453, 250, 55), "继续旅程", ContinueTower, true, new Color32(30, 88, 85, 255), 21);
            else Button(box, new Rect(305, 453, 250, 55), "再来一局", ReplayMatch, true, new Color32(30, 88, 85, 255), 21);
            Button(box, new Rect(570, 453, 250, 55), "收藏 / 调整牌组", () => CloseResultTo(() => OpenCollection()), true, background, 19);
            Button(box, new Rect(835, 453, 265, 55), "返回主菜单", () => CloseResultTo(ShowMenu), true, background, 21);
            Label(box, new Rect(40, 534, 1050, 56), "永久收益自动入账，无需再次领取。PvE 选牌仅本次旅程有效。", 18, muted);
        }
        void DrawHelp()
        {
            string[] tabs = { "入门与操作", "时轴与鸣钟", "战斗与关键词", "抢拍与未来", "资源与终局" };
            var box = Modal("Rules", "规则手册 · 不必一次记住所有规则");
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
                    heading = "拖动侍从到敌人；也可点击选择后确认。";
                    body = "攻 / 血 / 节奏：每次攻击的伤害、剩余生命、攻击所花时间。\n新侍从通常疲惫；「迅捷」可立即攻击。攻击后疲惫，等己方鸣钟重整。\n敌人有「守卫」时，攻击必须先选守卫；一般伤害术式不受守卫限制。\n侍从交战同时互相造成伤害，钟主不反击；「护盾」抵消一次伤害。\n「停滞」立即疲惫，不能重整；下一次鸣钟只解除，再下一次才重整。\n「部署」入场触发；「余音」死亡触发；「蓄力」在己方鸣钟时 +1/+1。";
                    tip = "金色边框指出当前所选行动的合法目标。没有目标时可取消，换一个行动。"; break;
                case 3:
                    heading = "未来是公开的，只有伏笔藏着答案。";
                    body = "「抢拍」在对方出牌、攻击或用时器时响应，通常多付 1 刻税。\n抢拍先结算，只允许一层；原行动若失效，仍支付时间。可点「放弃抢拍」。\n「咏唱 X」公开放在宣言时标 + 费用 + X；自己的时标经过时才生效。\n咏唱目标已锁定，目标离场不重选。点时轴物件可查看锁定连线。\n「伏笔」花 2 刻盖任意手牌：放到支付后 +1～+6 且双方尚未到达的位置。\n任一方先经过就揭示；普通牌是无效果的虚张声势，真正伏笔执行揭示效果。";
                    tip = "伏笔：选牌切到盖放，点时轴金色格再确认；可拖到该格。伊琳每小节首次抢拍免税。"; break;
                case 4:
                    heading = "蓄势补牌，静候让出行动权。";
                    body = "「蓄势」2 刻抽 1 张，每小节一次；「静候」走到对手时标 +1（最多 64）。\n「时器」是钟主技能，冷却按己方鸣钟次数；「时砂」只抵扣出牌/抢拍费用。\n手牌最多 8 张，溢出公开焚毁；侍从最多 5 个，刻印 3 个，咏唱 2 个。\n「预见」按想要的顺序点牌，前 N 张置顶，其余置底；窥时第一张入手。\n命烛首次降至 10 或以下且未死亡，在检查点逆流后退 2 刻（落后差最多 4）。\n48 / 52 / 56 / 60 刻暮钟各扣 1 / 2 / 3 / 4；空牌库每次抽牌扣 2 命烛。";
                    tip = "命烛归零即输；双方都到 64 刻，比命烛 → 总场攻 → 手牌数，相同则平局。"; break;
                default:
                    heading = "目标：把对方命烛（生命）降至 0。";
                    body = "你的牌与侍从在下方，对手在上方。没有回合，也没有要积攒的法力。\n卡牌左上角的“刻”就是费用：先执行效果，再让自己的时标前进。\n操作：选牌看中央时轴；拖牌到部署区 / 目标，拖侍从到敌人，松手提交。\n也可点击来源、目标，再确认；无效落区、右键或 Esc 取消未提交操作。\n正式开局可调度一次：点要换的牌，再确认；新手建议先保留 1～2 刻牌。\n热座换人要先交接屏幕再揭开牌面；本地帮助窗口暂停 AI 与抢拍倒计时。";
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
                var tutorialBox = Modal("TutorialLeave", "暂时离开教学？");
                Label(tutorialBox, new Rect(40, 160, 1050, 120), "退出不会记为完成。\n可以随时从主菜单重新开始；教学内没有时间压力。", 25, ink);
                Button(tutorialBox, new Rect(40, 380, 500, 70), "继续教学", () => { leaving = false; RebuildBattle(); });
                Button(tutorialBox, new Rect(600, 380, 500, 70), "退出到主菜单", ShowMenu); return;
            }
            var box = Modal("Leave", "离开本局？");
            Label(box, new Rect(40, 160, 1050, 110), "每次确认行动后自动保存，返回菜单可稍后继续。\n认输不产生本场成长收益；已入账的永久收益保留。", 24, ink);
            Button(box, new Rect(40, 370, 300, 68), "继续游戏", () => { leaving = false; RebuildBattle(); });
            Button(box, new Rect(400, 370, 300, 68), "认输", () => { leaving = false; Submit(new Command { Player = viewer, Kind = CommandKind.Concede }); }, State.Outcome == Outcome.Ongoing);
            Button(box, new Rect(760, 370, 300, 68), "返回菜单", ShowMenu);
        }
        void ShowCampaign()
        {
            SyncRun();
            Clear(); BeginPage("Campaign"); Header("逆流之塔 / 已自动保存 · 临时牌组不属于永久收藏");
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
                Label(root, new Rect(104, 301, 1320, 50), "战利品 · 仅加入本次旅程牌组", 28, teal);
                for (int i = 0; i < Run.Rewards.Count; i++)
                {
                    string key = Run.Rewards[i]; var d = Database.Card(key);
                    Button(root, new Rect(104 + i * 457, 379, 419, 226), d.Name + "  /  " + d.Time + " 刻\n\n" + d.Text, () => ChooseJourneyReward(key), true, panel, 24);
                }
                Button(root, new Rect(1050, 659, 390, 61), "跳过奖励", () => ChooseJourneyReward(null));
            }
            else if (Run.Stage == RunStage.Rest)
            {
                Label(root, new Rect(104, 315, 1300, 50), "调律 · 让齿轮歇一歇", 30, teal);
                Button(root, new Rect(104, 410, 620, 130), "修复命烛 +6\n恢复后前往下一节点", () => RestJourney(), true, panel, 26);
                Button(root, new Rect(770, 410, 660, 130), "精简牌组\n选择移除一张牌，替代治疗", ShowRemoveCard, Run.Deck.Cards.Count > 10, panel, 26);
            }
            else
            {
                int earned = 0; foreach (var receipt in progression.Data.Settlements) if (receipt.RunId == progression.Data.ActiveRun.Id && receipt.Applied) earned += receipt.Total;
                Label(root, new Rect(104, 540, 1330, 50), "本次旅程永久铸片合计 +" + earned + "（已逐场入账，不再次发放）；临时牌组已清空。", 22, gold);
                Label(root, new Rect(104, 346, 1330, 160), Run.Stage == RunStage.Complete ? "第一幕短流程已完成。\n完整三幕、专属敌人、遗物与叙事分支，将在后续版本展开。" : "本次旅程结束。已经入账的收藏收益仍属于你。\n临时牌组已清空，可重新出发或返回收藏调整配方。", 28, ink);
                Button(root, new Rect(104, 612, 500, 80), "重新出发", RestartJourney, true, new Color32(30, 75, 79, 255), 27);
            }
            Button(root, new Rect(1230, 800, 280, 54), "返回主菜单", ShowMenu); FinishPage();
        }
        void ShowRemoveCard()
        {
            Clear(); BeginPage("RemoveCard"); Header("调律 / 选择移除一张牌");
            for (int i = 0; i < Run.Deck.Cards.Count; i++)
            {
                int index = i; var d = Database.Card(Run.Deck.Cards[i]);
                Button(root, new Rect(70 + i % 5 * 295, 140 + i / 5 * 185, 275, 165), d.Name + "\n" + d.Text, () => RestJourney(index), true, panel, 20);
            }
            Button(root, new Rect(1190, 810, 300, 50), "取消", ShowCampaign); FinishPage();
        }
    }
}
