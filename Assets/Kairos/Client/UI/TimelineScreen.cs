using System;
using System.Collections.Generic;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        CommandKind? selectedAction;
        int timelineStart = -1;
        Text timeSummary;
        readonly Dictionary<Target, RectTransform> targetRects = new Dictionary<Target, RectTransform>();
        RectTransform sourceRect, futureRect;

        ActionPreview Preview(Command c) => c == null ? null : ActionPreview.For(View, c, Database, GameEngine.Cost(State, c));
        string PreviewText(Command c)
        {
            if (c == null) return actionWindow.Title + " · 你 " + View.Players[viewer].Marker + " / 对手 " + View.Players[1 - viewer].Marker + " 刻"
                + "\n" + actionWindow.Explanation;
            var p = Preview(c);
            string actor = p.NextActor == -2 ? "抢拍后仍须结算原行动" : p.NextActor == -1 ? "双方时尽" : p.NextActor == viewer ? "预计仍由你行动" : "预计交给对手";
            return p.From + " → " + p.Landing + " · 支付 " + p.Cost + " 刻  |  鸣钟 " + p.Chimes.Count + " 次" + (p.Kairos ? " · 契机" : "") + (p.Chain ? " · 连拍" : "") + "  |  " + actor
                + "\n" + (p.FuturePosition >= 0 ? (c.Kind == CommandKind.Omen ? "伏笔" : "咏唱锁定") + " @" + p.FuturePosition + "  ·  " : "")
                + (p.Chimes.Count > 0 ? "新钟点抽牌 / 重整 / 视界提升；" : "") + "经过物件 " + p.CrossingObjects.Count + " 个。仅支付推演，不含效果、抢拍、伏笔与胜负变化。";
        }
        void SelectAction(CommandKind kind) { if (!BattleInput || !HasAction(kind)) return; ClearSelection(); selectedAction = kind; RebuildBattle(); }
        string ActionLanding(CommandKind kind, string title)
        {
            var c = new Command { Player = viewer, Kind = kind };
            return title + " → " + Preview(c).Landing + "刻";
        }
        void ConfirmDraft() { var c = LegalDraft(); if (CanAct && c != null) Submit(c); }
        void CancelDraft() { ClearSelection(); RebuildBattle(); }
        void SelectOmenPosition(int pos)
        {
            var c = Draft(); if (c == null || c.Kind != CommandKind.Omen) return;
            c.Offset = pos - View.Players[viewer].Marker - View.Config.OmenSetCost;
            if (GameEngine.IllegalReason(State, c) != null || !LessonAllows(c)) return;
            offset = c.Offset; RebuildBattle();
        }
        bool OmenPositionAvailable(int pos)
        {
            var c = Draft(); if (c == null || c.Kind != CommandKind.Omen || !CanAct) return false;
            c.Offset = pos - View.Players[viewer].Marker - View.Config.OmenSetCost;
            return GameEngine.IllegalReason(State, c) == null && LessonAllows(c);
        }
        void DrawTimeline()
        {
            var strip = Box(root, new Rect(32, 296, 1156, 182), panel);
            strip.name = "Timeline";
            int end = View.Config.TimelineEnd, span = Math.Min(16, end);
            var draft = Draft(); var preview = Preview(draft);
            int start = timelineStart < 0 ? Math.Max(0, Math.Min(end - span, View.Players[viewer].Marker - 2)) : Math.Min(end - span, timelineStart);
            Label(strip, new Rect(10, 2, 220, 22), "全局 0—" + end + " / 局部 " + start + "—" + (start + span), 13, muted);
            // Global overview remains visible even when the two players are far apart.
            for (int i = 0; i <= end; i += View.Config.BarLength)
                Box(strip, new Rect(240 + i * 600f / end, 13, 1, 9), muted).GetComponent<Image>().raycastTarget = false;
            for (int p = 0; p < 2; p++) Label(strip, new Rect(230 + View.Players[p].Marker * 600f / end, p == viewer ? 11 : 0, 45, 16), "P" + (p + 1), 12, p == viewer ? teal : red);
            if (preview != null) Label(strip, new Rect(230 + preview.Landing * 600f / end, 23, 50, 15), "↓" + preview.Landing, 12, gold);
            Button(strip, new Rect(884, 1, 65, 29), "‹", () => { timelineStart = Math.Max(0, start - 8); RebuildBattle(); }, start > 0);
            Button(strip, new Rect(954, 1, 110, 29), "跟随时标", () => { timelineStart = -1; RebuildBattle(); }, true, background, 14);
            Button(strip, new Rect(1069, 1, 65, 29), "›", () => { timelineStart = Math.Min(end - span, start + 8); RebuildBattle(); }, start + span < end);
            float width = 1128f / (span + 1);
            for (int i = 0; i <= span; i++)
            {
                int pos = start + i; bool legal = OmenPositionAvailable(pos);
                string markers = "";
                for (int p = 0; p < 2; p++) if (View.Players[p].Marker == pos) markers += "<color=" + (p == viewer ? "#4AB5B0" : "#CF7367") + ">P" + (p + 1) + "</color> ";
                bool landing = preview != null && preview.Landing == pos;
                var cell = Button(strip, new Rect(14 + i * width, 38, width - 3, 48), (landing ? "↓ " : "") + pos + (pos > 0 && pos < end && pos % View.Config.BarLength == 0 ? " 钟" : "") + "\n" + markers,
                    () => SelectOmenPosition(pos), legal, legal || landing ? new Color32(91, 75, 44, 255) : background, 15);
                // Disabled buttons must not darken the overview text.
                var colors = cell.colors; colors.disabledColor = Color.white; cell.colors = colors;
                BindHit(cell.transform, BattleHitKind.Tick, pos);
                if (preview != null && preview.FuturePosition == pos)
                { futureRect = cell.GetComponent<RectTransform>(); Highlight(cell, true); }
                var objects = View.Timeline.FindAll(o => o.Pos == pos);
                if (objects.Count > 0)
                {
                    var chosen = objects.Find(o => selectedTarget.Equals(new Target(TargetKind.Object, o.Id))) ?? objects[0];
                    var target = new Target(TargetKind.Object, chosen.Id);
                    string title = (chosen.Kind == ObjectKind.Omen ? "?" : "◆") + " P" + (chosen.Owner + 1) + (objects.Count > 1 ? " ×" + objects.Count : "");
                    var b = Button(strip, new Rect(14 + i * width, 89, width - 3, 25), title, () => {
                        int index = objects.FindIndex(o => selectedTarget.Equals(new Target(TargetKind.Object, o.Id)));
                        selectedTarget = new Target(TargetKind.Object, objects[(index + 1) % objects.Count].Id); RebuildBattle();
                    }, true, selectedTarget.Equals(target) ? new Color32(91, 75, 44, 255) : background, 12);
                    BindTarget(b, target); Highlight(b, TargetAvailable(target) || preview != null && objects.Exists(o => preview.CrossingObjects.Contains(o.Id)));
                }
            }
            timeSummary = Label(strip, new Rect(14, 121, 878, 57), PreviewText(draft), 15, gold);
            Button(strip, new Rect(904, 125, 157, 45), View.Pending?.Kind == PendingKind.Interject ? "确认抢拍" : "确认行动", ConfirmDraft, CanAct && LegalDraft() != null, new Color32(30, 88, 85, 255), 19);
            Button(strip, new Rect(1067, 125, 74, 45), "取消", CancelDraft, CanAct, background, 16);
        }
        string DeclarationText()
        {
            var f = View.Declaration; if (f == null) return ActionHint();
            string name = f.Card != null ? Database.Card(f.Card.Key).Name : f.Command.Kind == CommandKind.Attack ? "侍从攻击" : "时器技能";
            return "对方宣言：" + name + (f.Command.Target.Kind == TargetKind.None ? "" : " → " + TargetName(f.Command.Target))
                + "\n锁定 " + f.Cost + " 刻；你的抢拍先结算，原行动随后继续。";
        }
    }
}
