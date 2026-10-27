using System;
using System.Collections.Generic;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        RectTransform root;
        Font font;
        Text timerLabel, previewLabel, cardDetailsLabel;
        readonly Color background = new Color32(13, 20, 30, 255), panel = new Color32(23, 34, 46, 255), gold = new Color32(225, 190, 116, 255), ink = new Color32(219, 225, 226, 255), muted = new Color32(137, 157, 170, 255), teal = new Color32(74, 181, 176, 255), red = new Color32(207, 115, 103, 255);
        UiFramework ui;
        bool uiCaptured => ui != null && ui.CaptureMode;
        void BuildCanvas()
        {
            if (root != null) return;
            font = UiFonts.Regular;
            if (!uiCaptured)
            {
                var prefab = Resources.Load<GameObject>("KairosUI/UIRoot");
                if (prefab == null) throw new InvalidOperationException("Missing UIRoot Prefab");
                ui = Instantiate(prefab, transform, false).GetComponent<UiFramework>(); root = ui.screen;
            }
            else root = ui.screen;
            if (FindObjectOfType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
        void BeginPage(string id)
        { BuildCanvas(); root = ui.ShowPage(id); }
        void FinishPage() => ui.EndBinding();
        RectTransform Element(Transform parent, string name, Rect rect)
        { return ui.Resolve(parent, "Element:" + name, name, rect, out _); }
        RectTransform Box(Transform parent, Rect rect, Color color)
        {
            var rt = ui.Resolve(parent, "Box", "Panel", rect, out bool created); var image = rt.GetComponent<Image>();
            if (image == null) image = rt.gameObject.AddComponent<Image>(); if (created || rt.GetComponent<UiElementSlot>().bindColor) image.color = color; return rt;
        }
        Text Label(Transform parent, Rect rect, string text, int size = 18, Color? color = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = ui.Resolve(parent, "Label", "Text", rect, out bool created); var t = rt.GetComponent<Text>(); if (t == null) t = rt.gameObject.AddComponent<Text>();
            if (created) { t.font = size >= 23 ? UiFonts.Bold : font; t.fontSize = size; t.color = color ?? ink; t.alignment = align; t.raycastTarget = false; t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; }
            else if (rt.GetComponent<UiElementSlot>().bindColor) t.color = color ?? ink;
            t.text = text; return t;
        }
        Button Button(Transform parent, Rect rect, string text, Action action, bool enabled = true, Color? color = null, int size = 18)
        {
            var rt = ui.Resolve(parent, "Button", "Button", rect, out bool created); var image = rt.GetComponent<Image>(); if (image == null) image = rt.gameObject.AddComponent<Image>(); if (created || rt.GetComponent<UiElementSlot>().bindColor) image.color = color ?? panel;
            var b = rt.GetComponent<Button>(); if (b == null) b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = image; b.interactable = enabled;
            if (created) { var colors = b.colors; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f); colors.pressedColor = new Color(.7f, .8f, .85f); colors.disabledColor = new Color(.45f, .45f, .45f, .8f); b.colors = colors; }
            Label(rt, new Rect(8, 4, rect.width - 16, rect.height - 8), text, size, null, TextAnchor.MiddleCenter);
            b.onClick.RemoveAllListeners(); b.onClick.AddListener(() => action()); return b;
        }
        void Clear()
        {
            timerLabel = previewLabel = cardDetailsLabel = timeSummary = windowTitle = null;
            dragging = false; targetRects.Clear(); sourceRect = futureRect = null;
            if (ui != null) ui.ClearViews();
        }
        void Header(string subtitle)
        {
            var header = ui.Subpanel(root, "Header", new Rect(0, 0, 1600, 73));
            Label(header, new Rect(32, 17, 430, 43), "K A I R O S   /   契机", 31, gold);
            Label(header, new Rect(500, 28, 820, 28), subtitle, 17, muted);
            Box(header, new Rect(32, 70, 1536, 1), gold * new Color(1, 1, 1, .35f));
        }
        void ShowMenu()
        {
            lesson = null; tutorialIntro = false; help = false; leaving = false; retrySave = null; aiCommand = null;
            State = null; PrivacyCurtain = false; Clear(); BeginPage("Home");
            if (progression == null) { InitFailure(new Exception("档案尚未加载")); return; }
            SyncRun(); DrawMetaHome(); FinishPage();
        }
        void RebuildBattle()
        {
            if (State == null) return; Clear(); BeginPage(PrivacyCurtain ? "PrivacyCurtain" : "Battle");
            Header(lesson != null ? "新手教学 / 固定残局 · 跟随金色高亮" : Mode == DemoMode.Hotseat ? "本地 PVP  /  双人热座" : Mode == DemoMode.Tower ? "逆流之塔  /  " + CampaignRun.EnemyNames[Run.Encounter] : trialMatch ? "全卡试用 / 无成长收益" : "收藏 AI 对战 / 永久成长收益");
            if (PrivacyCurtain)
            {
                int next = GameEngine.NextActor(State);
                Label(root, new Rect(350, 260, 900, 85), "请把屏幕交给 P" + (next + 1), 44, gold, TextAnchor.MiddleCenter);
                Label(root, new Rect(380, 365, 840, 80), "手牌与伏笔已隐藏。\n对方移开视线后，再揭开你的牌面。", 24, muted, TextAnchor.MiddleCenter);
                Button(root, new Rect(600, 505, 400, 76), "我是 P" + (next + 1) + "，开始操作", AcceptHandoff, true, new Color32(30, 75, 79, 255), 25);
                Button(root, new Rect(600, 615, 400, 58), "保存并返回菜单", ShowMenu, true, panel, 21); FinishPage(); return;
            }
            View = ViewRedactor.For(State, viewer);
            actionWindow = ActionAvailability.For(View); DrawActionWindow();
            DrawPlayer(1 - viewer, 141, false); DrawBoard(1 - viewer, 215);
            DrawTimeline(); DrawBoard(viewer, 487); DrawPlayer(viewer, 610, true); DrawHand();
            if (lesson != null) DrawTutorialSidebar(); else DrawSidebar();
            Button(root, new Rect(1310, 18, 130, 36), "规则手册", () => OpenRules());
            Button(root, new Rect(1454, 18, 110, 36), "离开", () => { leaving = true; RebuildBattle(); });
            DrawActionLinks();
            if (!View.MainPhase && CanAct) DrawMulligan();
            else if (View.Pending != null && View.Pending.Kind != PendingKind.Interject) DrawChoice();
            if (State.Outcome != Outcome.Ongoing) { if (lesson != null) DrawTutorialComplete(); else DrawResult(); }
            if (tutorialIntro) DrawTutorialIntro();
            if (help) DrawHelp();
            if (leaving) DrawLeave(); FinishPage();
        }
        void DrawPlayer(int p, int y, bool friendly)
        {
            var side = ui.Subpanel(root, "PlayerInfo" + p, new Rect(0, y, 1188, friendly ? 73 : 65));
            var player = View.Players[p]; var k = Database.Keeper(player.Keeper); Color accent = friendly ? teal : red;
            var target = new Target(TargetKind.Keeper, p);
            DrawKeeperPanel(k, p, 0, friendly, target, side);
            Label(side, new Rect(350, 2, 725, 31), "时标 " + player.Marker + "   |   视界 " + Math.Min(View.Config.HorizonMax, View.Config.HorizonBase + player.ChimesRung) + "   |   时砂 " + player.Sand + "   |   牌库 " + View.DeckCounts[p] + "   |   手牌 " + View.HandCounts[p], 21, accent);
            string sigils = "刻印："; foreach (var c in player.Sigils) sigils += Database.Card(c.Key).Name + "  ";
            if (player.Sigils.Count == 0) sigils += friendly && actionWindow.CanUseHand ? "—  /  无目标牌可拖到此处打出" : "—";
            if (friendly)
            {
                var playZone = Box(side, new Rect(345, 35, 843, 37), background); playZone.name = "PlayZone";
                BindHit(playZone, BattleHitKind.Deploy);
            }
            Label(side, new Rect(350, 39, 810, 32), sigils + (player.Prophecy != null ? "  /  预言 " + player.Prophecy : ""), 17, muted);
        }
        void DrawBoard(int p, int y)
        {
            var side = ui.Subpanel(root, "Board" + p, new Rect(32, y, 1156, p == viewer ? 110 : 76));
            var board = View.Players[p].Board;
            var layout = side.GetComponent<UiRepeaterLayout>();
            if (layout == null && uiCaptured) { layout = side.gameObject.AddComponent<UiRepeaterLayout>(); layout.cellSize = new Vector2(220, p == viewer ? 110 : 76); }
            if (p == viewer) { side.gameObject.name = "DeployZone"; BindHit(side, BattleHitKind.Deploy); }
            for (int i = 0; i < 5; i++)
            {
                var area = layout.Area(i);
                if (i >= board.Count) { var empty = Box(side, area, panel * new Color(1, 1, 1, 0.4f)); if (p == viewer) BindHit(empty, BattleHitKind.Deploy); Label(empty, new Rect(0, 10, 220, area.height - 15), p == viewer && i == board.Count ? actionWindow.Normal ? "拖到此处出牌\n侍从从最右侧入场" : "部署区\n" + (actionWindow.Response ? "仅可部署合法抢拍侍从" : "等待行动权") : "—", 16, muted, TextAnchor.MiddleCenter); continue; }
                var u = board[i]; var d = Database.Card(u.Card.Key); int id = u.Id;
                bool canAttack = p == viewer && BattleInput && HasAction(CommandKind.Attack, id);
                string state = u.Stasis ? "停滞" : u.Exhausted ? "疲惫" : p != viewer ? "就绪" : canAttack ? "可攻击" : "就绪 · 暂不能攻击";
                string tags = ((u.Keywords & Keyword.Guard) != 0 ? " 守卫" : "") + ((u.Keywords & Keyword.Growth) != 0 ? " 蓄力" : "") + (u.Shield ? " 护盾" : "");
                bool chosen = selectedUnit == id || selectedTarget.Equals(new Target(TargetKind.Unit, id));
                var visual = CardWidget(side, "BoardUnit", area);
                visual.Bind(d.Key, d.School, d.Rarity, d.Name, "", u.Atk + "攻 / " + u.Hp + "血 / " + u.Cadence + "刻", tags.Trim(), "", state, !u.Exhausted, chosen);
                var unitButton = visual.Interaction;
                unitButton.interactable = BattleInput && (p == viewer && selectedCard == 0 && !ability ? canAttack : TargetAvailable(new Target(TargetKind.Unit, id)));
                unitButton.onClick.AddListener(() => { if (p == viewer && selectedCard == 0 && !ability) { ClearSelection(); selectedUnit = id; RebuildBattle(); } else SelectTarget(new Target(TargetKind.Unit, id)); });
                BindTarget(unitButton, new Target(TargetKind.Unit, id));
                if (p == viewer) BindSource(unitButton.GetComponent<RectTransform>(), 0, id);
                if (canAttack && Draft() == null) ActionOutline(unitButton, teal);
                Highlight(unitButton, TargetAvailable(new Target(TargetKind.Unit, id)) || lesson != null && CanAct && TutorialUnitSelectable(p, id));
            }
        }
        void DrawHand()
        {
            var side = ui.Subpanel(root, "Hand", new Rect(32, 702, 1156, 188));
            var layout = side.GetComponent<UiRepeaterLayout>();
            if (layout == null && uiCaptured) { layout = side.gameObject.AddComponent<UiRepeaterLayout>(); layout.cellSize = new Vector2(136, 188); layout.spacing = new Vector2(9, 0); layout.columns = 8; }
            var hand = View.Players[viewer].Hand;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i]; var d = Database.Card(c.Key); int id = c.Id;
                var available = ActionAvailability.Card(View, id);
                bool selectable = CanAct && TutorialCardSelectable(id) && (actionWindow.Window == ActionWindow.Mulligan || actionWindow.CanUseHand);
                var visual = CardWidget(side, "HandCard", layout.Area(i)); var rt = visual.Rect; rt.name = "HandCard-" + id;
                visual.Bind(d.Key, d.School, d.Rarity, d.Name, d.Type == "OMEN" ? "伏笔2" : (d.Time + c.CostDelta) + "刻", d.Type == "UNIT" ? d.Atk + "/" + d.Hp + " 节" + d.Cadence : KindName(d.Type), d.Text, "", HandStatus(id, available), selectable && available.CanPlay, selectedCard == id || selection.Contains(id));
                visual.SetStatusColor(selectable && available.CanPlay ? actionWindow.Response ? gold : teal : muted);
                var button = visual.Interaction; button.interactable = selectable;
                var colors = button.colors; colors.disabledColor = new Color(.65f, .65f, .65f, 1); button.colors = colors;
                if (selectable && available.CanPlay) ActionOutline(button, actionWindow.Response ? gold : teal);
                else Highlight(button, lesson != null && button.interactable);
                button.onClick.AddListener(() =>
                {
                    if (!View.MainPhase) { if (!selection.Remove(id)) selection.Add(id); RebuildBattle(); return; }
                    SelectHand(id); RebuildBattle();
                });
                BindSource(rt, id, 0);
                var trigger = rt.GetComponent<EventTrigger>() ?? rt.gameObject.AddComponent<EventTrigger>(); trigger.triggers.Clear(); var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener(_ => HoverCard(id)); trigger.triggers.Add(entry);
                var exit = new EventTrigger.Entry { eventID = EventTriggerType.PointerExit };
                exit.callback.AddListener(_ => { if (!dragging && timeSummary != null) timeSummary.text = PreviewText(Draft()); }); trigger.triggers.Add(exit);
            }
        }
        void HoverCard(int id)
        {
            if (previewLabel == null || selectedCard != 0 || selectedUnit != 0 || ability || selectedAction.HasValue) return;
            if (cardDetailsLabel != null) cardDetailsLabel.text = Database.Card(GameEngine.FindCard(View.Players[viewer].Hand, id).Key).Text;
            var availability = ActionAvailability.Card(View, id);
            previewLabel.text = CardUnavailableReason(id, availability);
            if (!BattleInput || !availability.CanPlay) return;
            foreach (var c in View.Legal) if (c.CardId == id && c.Kind != CommandKind.Omen)
            {
                if (timeSummary != null) timeSummary.text = PreviewText(c);
                var p = MarkerPreview.For(View, c); previewLabel.text = "悬停预览：" + View.Players[viewer].Marker + " → " + p.FinalMarker + "\n对手窗口 " + p.OpponentWindow + " 刻；鸣钟 " + p.Chimes.Count + " 次" + (p.Kairos ? "；契机" : ""); return;
            }
            previewLabel.text = "当前不能正面打出。\n点击卡牌可查看原因或改为伏笔。";
        }
    }
}
