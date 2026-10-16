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
        Text timerLabel, previewLabel;
        readonly Color background = new Color32(13, 20, 30, 255), panel = new Color32(23, 34, 46, 255), gold = new Color32(225, 190, 116, 255), ink = new Color32(219, 225, 226, 255), muted = new Color32(137, 157, 170, 255), teal = new Color32(74, 181, 176, 255), red = new Color32(207, 115, 103, 255);
        void BuildCanvas()
        {
            if (root != null) return;
            font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "PingFang SC", "Noto Sans CJK SC", "Arial" }, 20);
            var canvasObject = new GameObject("KAIROS Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var area = new GameObject("Screen", typeof(RectTransform), typeof(Image)); area.transform.SetParent(canvasObject.transform, false);
            root = area.GetComponent<RectTransform>(); root.anchorMin = root.anchorMax = new Vector2(0.5f, 0.5f); root.sizeDelta = new Vector2(1600, 900); area.GetComponent<Image>().color = background;
            if (FindObjectOfType<EventSystem>() == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }
        RectTransform Element(Transform parent, string name, Rect rect)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(rect.x, -rect.y); rt.sizeDelta = rect.size; return rt;
        }
        RectTransform Box(Transform parent, Rect rect, Color color)
        { var rt = Element(parent, "Panel", rect); rt.gameObject.AddComponent<Image>().color = color; return rt; }
        Text Label(Transform parent, Rect rect, string text, int size = 18, Color? color = null, TextAnchor align = TextAnchor.UpperLeft)
        {
            var rt = Element(parent, "Text", rect); var t = rt.gameObject.AddComponent<Text>(); t.font = font; t.fontSize = size; t.color = color ?? ink; t.text = text; t.alignment = align; t.raycastTarget = false; t.supportRichText = true; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate; return t;
        }
        Button Button(Transform parent, Rect rect, string text, Action action, bool enabled = true, Color? color = null, int size = 18)
        {
            var rt = Box(parent, rect, color ?? panel); var b = rt.gameObject.AddComponent<Button>(); b.targetGraphic = rt.GetComponent<Image>(); b.interactable = enabled;
            var colors = b.colors; colors.highlightedColor = new Color(1.25f, 1.25f, 1.25f); colors.pressedColor = new Color(0.7f, 0.8f, 0.85f); colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f); b.colors = colors;
            Label(rt, new Rect(8, 4, rect.width - 16, rect.height - 8), text, size, null, TextAnchor.MiddleCenter);
            b.onClick.AddListener(() => action()); return b;
        }
        void Clear()
        {
            timerLabel = previewLabel = null;
            for (int i = root.childCount - 1; i >= 0; i--) { var go = root.GetChild(i).gameObject; go.SetActive(false); Destroy(go); }
        }
        void Header(string subtitle)
        {
            Label(root, new Rect(32, 17, 430, 43), "K A I R O S   /   契机", 31, gold);
            Label(root, new Rect(500, 28, 820, 28), subtitle, 17, muted);
            Box(root, new Rect(32, 70, 1536, 1), gold * new Color(1, 1, 1, 0.35f));
        }
        void ShowMenu()
        {
            lesson = null; tutorialIntro = false; help = false; leaving = false;
            State = null; PrivacyCurtain = false; Clear(); Header("钟碎之日  ·  可玩规则原型  /  DEMO 01");
            Button(root, new Rect(1310, 18, 252, 38), "规则手册 / 随时查阅", () => OpenRules(), true, panel, 18);
            Label(root, new Rect(100, 155, 760, 95), "谁拥有你的时间？", 52, gold);
            Label(root, new Rect(104, 252, 850, 88), "没有回合，没有法力。\n落后者行动；每一张牌，都把未来让给对手。", 24, ink);
            var art = Box(root, new Rect(1060, 148, 380, 190), panel);
            Label(art, new Rect(24, 24, 336, 46), "00  ─  04  ─  08  ─  64", 28, gold, TextAnchor.MiddleCenter);
            Label(art, new Rect(35, 93, 310, 70), "鸣钟 · 预见 · 一呼一应\n黄铜钟盘上的下一种可能", 20, muted, TextAnchor.MiddleCenter);
            Label(root, new Rect(104, 376, 1350, 32), "对局准备", 23, gold);
            Button(root, new Rect(104, 427, 620, 70), "P1 / 你的预组：" + Database.Precons[FirstDeck].Name + "\n" + Database.Keeper(Database.Precons[FirstDeck].Keeper).Name, () => { FirstDeck = 1 - FirstDeck; ShowMenu(); });
            Button(root, new Rect(758, 427, 620, 70), "P2 / 对手预组：" + Database.Precons[SecondDeck].Name + "\n" + Database.Keeper(Database.Precons[SecondDeck].Keeper).Name, () => { SecondDeck = 1 - SecondDeck; ShowMenu(); });
            Label(root, new Rect(104, 526, 100, 32), "对局种子", 18, muted);
            var inputRect = Box(root, new Rect(210, 517, 516, 46), panel); var input = inputRect.gameObject.AddComponent<InputField>(); var inputText = Label(inputRect, new Rect(14, 8, 480, 30), Seed, 19); input.textComponent = inputText; input.text = Seed; input.onValueChanged.AddListener(value => Seed = string.IsNullOrWhiteSpace(value) ? "kairos" : value);
            Button(root, new Rect(758, 516, 620, 54), TutorialCompleted ? "新手教学 · 已完成，可重玩" : "新手教学 · 约 2 分钟，边玩边学（推荐）", StartTutorial, true, new Color32(30, 75, 79, 255), 21);
            Button(root, new Rect(104, 617, 402, 96), "本地 PVP\n双人热座 · 隐私交接", () => RequestMode(() => StartMatch(DemoMode.Hotseat)), true, new Color32(80, 68, 46, 255), 23);
            Button(root, new Rect(530, 617, 402, 96), "对战练习\n与本地 AI 切磋", () => RequestMode(() => StartMatch(DemoMode.Practice)), true, new Color32(30, 75, 79, 255), 23);
            Button(root, new Rect(956, 617, 422, 96), "逆流之塔\nPVE 基础短流程", () => RequestMode(StartTower), true, new Color32(64, 54, 80, 255), 23);
            Label(root, new Rect(104, 753, 1290, 82), "先读这一条：时标较小的一方行动，相同时后到者先行。每过 4 刻鸣钟：抽牌、重整、扩大视界。\n选手牌或侍从 → 点目标 → 确认。卡牌可盖成伏笔；抢拍仅一层。", 19, muted);
        }
        void RebuildBattle()
        {
            if (State == null) return; Clear();
            Header(lesson != null ? "新手教学 / 固定残局 · 跟随金色高亮" : Mode == DemoMode.Hotseat ? "本地 PVP  /  双人热座" : Mode == DemoMode.Tower ? "逆流之塔  /  " + CampaignRun.EnemyNames[Run.Encounter] : "对战练习  /  本地 AI");
            if (PrivacyCurtain)
            {
                int next = GameEngine.NextActor(State);
                Label(root, new Rect(350, 260, 900, 85), "请把屏幕交给 P" + (next + 1), 44, gold, TextAnchor.MiddleCenter);
                Label(root, new Rect(380, 365, 840, 80), "手牌与伏笔已隐藏。\n对方移开视线后，再揭开你的牌面。", 24, muted, TextAnchor.MiddleCenter);
                Button(root, new Rect(600, 505, 400, 76), "我是 P" + (next + 1) + "，开始操作", AcceptHandoff, true, new Color32(30, 75, 79, 255), 25); return;
            }
            View = ViewRedactor.For(State, viewer);
            DrawPlayer(1 - viewer, 90, false); DrawBoard(1 - viewer, 180);
            DrawTimeline(); DrawBoard(viewer, 487); DrawPlayer(viewer, 610, true); DrawHand();
            if (lesson != null) DrawTutorialSidebar(); else DrawSidebar();
            Button(root, new Rect(1310, 18, 130, 36), "规则手册", () => OpenRules());
            Button(root, new Rect(1454, 18, 110, 36), "离开", () => { leaving = true; RebuildBattle(); });
            if (!View.MainPhase && CanAct) DrawMulligan();
            else if (View.Pending != null && View.Pending.Kind != PendingKind.Interject) DrawChoice();
            if (State.Outcome != Outcome.Ongoing) { if (lesson != null) DrawTutorialComplete(); else DrawResult(); }
            if (tutorialIntro) DrawTutorialIntro();
            if (help) DrawHelp();
            if (leaving) DrawLeave();
        }
        void DrawPlayer(int p, int y, bool friendly)
        {
            var player = View.Players[p]; var k = Database.Keeper(player.Keeper); Color accent = friendly ? teal : red;
            var target = new Target(TargetKind.Keeper, p);
            var keeperButton = Button(root, new Rect(32, y, 298, 73), (friendly ? "P" + (p + 1) + "  " : EnemyName + "  ") + k.Name + "\n命烛 " + player.Life + " / " + View.Config.LifeMax, () => SelectTarget(target), lesson == null ? CanAct : TargetAvailable(target), selectedTarget.Equals(target) ? new Color32(92, 77, 43, 255) : panel, 20);
            Highlight(keeperButton, TargetAvailable(target));
            Label(root, new Rect(350, y + 2, 725, 31), "时标 " + player.Marker + "   |   视界 " + Math.Min(View.Config.HorizonMax, View.Config.HorizonBase + player.ChimesRung) + "   |   时砂 " + player.Sand + "   |   牌库 " + View.DeckCounts[p] + "   |   手牌 " + View.HandCounts[p], 21, accent);
            string sigils = "刻印："; foreach (var c in player.Sigils) sigils += Database.Card(c.Key).Name + "  ";
            if (player.Sigils.Count == 0) sigils += "—";
            Label(root, new Rect(350, y + 39, 810, 32), sigils + (player.Prophecy != null ? "  /  预言 " + player.Prophecy : ""), 17, muted);
        }
        void DrawBoard(int p, int y)
        {
            var board = View.Players[p].Board;
            for (int i = 0; i < 5; i++)
            {
                int x = 32 + i * 234;
                if (i >= board.Count) { var empty = Box(root, new Rect(x, y, 220, 110), panel * new Color(1, 1, 1, 0.4f)); Label(empty, new Rect(0, 40, 220, 30), "—", 22, muted, TextAnchor.MiddleCenter); continue; }
                var u = board[i]; var d = Database.Card(u.Card.Key); int id = u.Id;
                string state = u.Stasis ? "停滞" : u.Exhausted ? "疲惫" : "可攻击";
                string tags = ((u.Keywords & Keyword.Guard) != 0 ? " 守卫" : "") + ((u.Keywords & Keyword.Growth) != 0 ? " 蓄力" : "") + (u.Shield ? " 护盾" : "");
                bool chosen = selectedUnit == id || selectedTarget.Equals(new Target(TargetKind.Unit, id));
                var unitButton = Button(root, new Rect(x, y, 220, 110), d.Name + "\n" + u.Atk + " 攻 / " + u.Hp + " 血 / " + u.Cadence + " 刻\n" + state + tags,
                    () => { if (p == viewer && selectedCard == 0 && !ability && (selectedUnit == 0 || lesson != null)) { selectedUnit = id; selectedTarget = default; RebuildBattle(); } else SelectTarget(new Target(TargetKind.Unit, id)); }, CanAct && TutorialUnitSelectable(p, id), chosen ? new Color32(92, 77, 43, 255) : panel, 18);
                Highlight(unitButton, TargetAvailable(new Target(TargetKind.Unit, id)) || lesson != null && CanAct && TutorialUnitSelectable(p, id));
            }
        }
        void DrawTimeline()
        {
            var strip = Box(root, new Rect(32, 306, 1156, 162), panel);
            string active = State.Outcome != Outcome.Ongoing ? "对局结束" : !View.MainPhase ? "起手调度" : View.Pending != null ? "等待 P" + (View.Actor + 1) + " 选择" : "行动权：P" + (View.Actor + 1) + "  ·  落后者行动，相同则后到者先行";
            var intent = Mode == DemoMode.Tower ? AiIntent() : null;
            Label(strip, new Rect(16, 10, 1110, 25), intent ?? active, 18, gold);
            for (int i = 0; i <= 64; i++)
            {
                float x = 24 + i * 17.2f; bool bell = i % 4 == 0;
                Box(strip, new Rect(x, 63, 1, bell ? 28 : 11), i >= 48 ? red : muted);
                if (bell) Label(strip, new Rect(x - 12, 95, 36, 23), i.ToString(), 13, muted, TextAnchor.MiddleCenter);
            }
            for (int p = 0; p < 2; p++)
            {
                float x = 24 + View.Players[p].Marker * 17.2f;
                Label(strip, new Rect(x - 15, p == viewer ? 111 : 35, 56, 26), "P" + (p + 1), 18, p == viewer ? teal : red);
                Box(strip, new Rect(x - 2, 62, 5, 25), p == viewer ? teal : red);
            }
            var draft = LegalDraft();
            if (draft != null)
            {
                var previewView = View; previewView.Costs[PlayerView.CommandKey(draft)] = GameEngine.Cost(State, draft);
                var preview = MarkerPreview.For(previewView, draft); float x = 24 + preview.FinalMarker * 17.2f;
                Box(strip, new Rect(x - 3, 57, 7, 37), gold);
            }
            foreach (var o in View.Timeline)
            {
                float x = 24 + Mathf.Min(o.Pos, 64) * 17.2f;
                Label(strip, new Rect(x - 5, 139, 60, 22), o.Kind == ObjectKind.Omen ? "?" : "◆", 15, gold);
            }
        }
        void DrawHand()
        {
            var hand = View.Players[viewer].Hand;
            for (int i = 0; i < hand.Count; i++)
            {
                var c = hand[i]; var d = Database.Card(c.Key); int id = c.Id;
                Color color = selectedCard == id || selection.Contains(id) ? new Color32(93, 77, 44, 255) : panel;
                var rt = Box(root, new Rect(32 + i * 145, 702, 136, 171), color);
                var button = rt.gameObject.AddComponent<Button>(); button.targetGraphic = rt.GetComponent<Image>(); button.interactable = CanAct && TutorialCardSelectable(id);
                Highlight(button, lesson != null && button.interactable);
                button.onClick.AddListener(() =>
                {
                    if (!View.MainPhase) { if (!selection.Remove(id)) selection.Add(id); RebuildBattle(); return; }
                    selectedCard = id; selectedUnit = 0; ability = false; placingOmen = d.Type == "OMEN"; selectedTarget = default; sand = 0; RebuildBattle();
                });
                Label(rt, new Rect(10, 7, 116, 26), (d.Type == "OMEN" ? "伏笔 2" : (d.Time + c.CostDelta) + " 刻") + " · " + KindName(d.Type), 15, gold);
                Label(rt, new Rect(10, 38, 116, 45), d.Name, 20, ink);
                Label(rt, new Rect(10, 84, 116, 64), d.Text, 14, muted);
                if (d.Type == "UNIT") Label(rt, new Rect(10, 147, 116, 22), d.Atk + " / " + d.Hp + "    节奏 " + d.Cadence, 14, teal);
                var trigger = rt.gameObject.AddComponent<EventTrigger>(); var entry = new EventTrigger.Entry { eventID = EventTriggerType.PointerEnter };
                entry.callback.AddListener(_ => HoverCard(id)); trigger.triggers.Add(entry);
            }
        }
        void HoverCard(int id)
        {
            if (previewLabel == null || selectedCard != 0 || selectedUnit != 0 || ability) return;
            foreach (var c in View.Legal) if (c.CardId == id && c.Kind != CommandKind.Omen)
            {
                var p = MarkerPreview.For(View, c); previewLabel.text = "悬停预览：" + View.Players[viewer].Marker + " → " + p.FinalMarker + "\n对手窗口 " + p.OpponentWindow + " 刻；鸣钟 " + p.Chimes.Count + " 次" + (p.Kairos ? "；契机" : ""); return;
            }
            previewLabel.text = "当前不能正面打出。\n点击卡牌可查看原因或改为伏笔。";
        }
    }
}
