using System;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        ActionAvailability actionWindow;
        Text windowTitle;
        Color WindowColor => actionWindow.Response ? gold : actionWindow.Normal ? teal : muted;
        bool HasAction(CommandKind kind, int unit = 0)
        { foreach (var c in View.Legal) if (c.Kind == kind && (unit == 0 || c.UnitId == unit) && LessonAllows(c)) return true; return false; }
        void DrawActionWindow()
        {
            var strip = Box(root, new Rect(32, 78, 1156, 56), actionWindow.Normal ? new Color32(24, 65, 67, 255) : actionWindow.Response ? new Color32(76, 59, 28, 255) : new Color32(33, 40, 51, 255));
            strip.name = "ActionWindow";
            Box(strip, new Rect(0, 0, 5, 56), WindowColor).GetComponent<Image>().raycastTarget = false;
            string title = actionWindow.Title;
            if (actionWindow.Normal)
            {
                int cards = 0; foreach (var c in View.Players[viewer].Hand) if (ActionAvailability.Card(View, c.Id).CanPlay && TutorialCardSelectable(c.Id)) cards++;
                title += cards > 0 ? " · 可出牌 " + cards + " 张" : " · 暂无可出手牌";
            }
            windowTitle = Label(strip, new Rect(16, 6, 550, 44), title, 24, WindowColor, TextAnchor.MiddleLeft);
            windowTitle.name = "ActionWindowTitle";
            Label(strip, new Rect(580, 7, 560, 46), actionWindow.Explanation, 17, ink, TextAnchor.MiddleLeft).name = "ActionWindowReason";
        }
        void ActionOutline(Button b, Color color)
        { var outline = b.gameObject.AddComponent<Outline>(); outline.effectColor = color; outline.effectDistance = new Vector2(2, -2); }
        string CardUnavailableReason(int id, HandAvailability available)
        {
            if (!actionWindow.CanUseHand || !TutorialCardSelectable(id)) return !TutorialCardSelectable(id) && lesson != null ? "教学中请按右侧目标操作；此牌暂未开放。" : available.Reason;
            if (available.CanPlay) return available.Reason;
            var instance = GameEngine.FindCard(View.Players[viewer].Hand, id); var d = Database.Card(instance.Key);
            string reason;
            if (actionWindow.Response && !d.Interject) reason = "这张牌不能抢拍，只能等你获得普通行动权。";
            else if (actionWindow.Normal && d.InterjectOnly) reason = "仅限符合条件的抢拍窗口，不能主动打出。";
            else if (d.Type == "OMEN") reason = "伏笔牌必须盖放到合法的未来刻度。";
            else if (d.Type == "UNIT" && View.Players[viewer].Board.Count >= View.Config.BoardMax) reason = "侍从区已满，不能部署。";
            else if (d.Type == "SIGIL" && View.Players[viewer].Sigils.Count >= View.Config.SigilMax) reason = "刻印区已满，不能打出。";
            else
            {
                var probe = new Command { Player = viewer, Kind = actionWindow.Response ? CommandKind.Interject : CommandKind.Play, CardId = id, Sand = View.Players[viewer].Sand };
                if (GameEngine.Cost(State, probe) > GameEngine.Horizon(State, viewer)) reason = "即使用尽时砂仍超过视界；需等己方鸣钟提升上限。";
                else reason = actionWindow.Response ? "不满足本次抢拍条件，或没有合法目标 / 场位。" : "没有合法目标或咏唱位；当前不能正面打出。";
            }
            return reason + (available.CanCover ? " 可改为盖放伏笔。" : "");
        }
        string HandStatus(int id, HandAvailability available)
        {
            if (lesson != null && !TutorialCardSelectable(id)) return "教学暂锁定";
            if (actionWindow.Window == ActionWindow.Mulligan) return selection.Contains(id) ? "已标记换牌" : "点击标记换牌";
            return available.Label;
        }
        void UpdateResponseClock()
        {
            if (replyAt <= 0 || actionWindow == null || !actionWindow.Response) return;
            int seconds = Mathf.Max(0, Mathf.CeilToInt(replyAt - Time.unscaledTime));
            if (timerLabel != null) timerLabel.text = "仅可抢拍 · 剩余 " + seconds + " 秒";
            if (windowTitle != null) windowTitle.text = "你可抢拍 · 剩余 " + seconds + " 秒";
        }
    }
}
