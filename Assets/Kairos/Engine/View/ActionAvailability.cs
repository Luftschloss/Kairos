using System;
using Kairos.Engine;

namespace Kairos.Engine
{
    public enum ActionWindow { Act, Respond, Choose, Mulligan, Wait, WaitResponse, WaitChoice, Finished, Ended }

    // Presentation-only interpretation of the redacted legal-command list; never changes rules.
    public sealed class ActionAvailability
    {
        public ActionWindow Window;
        public string Title, Explanation;
        public bool Normal => Window == ActionWindow.Act;
        public bool Response => Window == ActionWindow.Respond;
        public bool CanUseHand => Normal || Response;

        public static ActionAvailability For(PlayerView v)
        {
            var r = new ActionAvailability(); var own = v.Players[v.Viewer]; var other = v.Players[1 - v.Viewer];
            if (v.Outcome != Outcome.Ongoing) { r.Window = ActionWindow.Ended; r.Title = "对局已结束"; r.Explanation = "本局不再接受行动，请查看结算。"; }
            else if (!v.MainPhase)
            {
                bool choosing = v.Actor == v.Viewer;
                r.Window = choosing ? ActionWindow.Mulligan : ActionWindow.Wait;
                r.Title = choosing ? "起手调度 · 还不能出牌" : "等待对手调度";
                r.Explanation = choosing ? "点击手牌标记换牌，再确认调度；双方完成后开始行动。" : own.MulliganDone ? "你的调度已确认；此时只能查看手牌，不能打出。" : "等待对手先确认调度，随后再选择你的起手牌。";
            }
            else if (v.PublicDecisionKind.HasValue)
            {
                bool response = v.PublicDecisionKind == PendingKind.Interject, mine = v.Actor == v.Viewer;
                r.Window = mine ? response ? ActionWindow.Respond : ActionWindow.Choose : response ? ActionWindow.WaitResponse : ActionWindow.WaitChoice;
                r.Title = mine ? response ? "你可抢拍 · 仅限响应牌" : "请先完成效果选择" : response ? "等待对手响应 · 原行动待结算" : "等待对手完成效果选择";
                r.Explanation = mine ? response ? "只能打出可抢拍的牌或放弃；不能普通出牌、攻击或使用时器。" : "在弹窗中确认选择，之后才能继续行动。" : response ? "你已宣言；对手响应结束后，原行动才会继续结算。" : "对手正在编排牌库或召回；不是重新获得行动权。";
            }
            else if (own.Finished) { r.Window = ActionWindow.Finished; r.Title = "你已到达时轴终点"; r.Explanation = "你不再行动；等待对手结束或触发胜负判定。"; }
            else if (v.Actor == v.Viewer)
            {
                r.Window = ActionWindow.Act; r.Title = "你可行动";
                r.Explanation = other.Finished ? "对手已到终点，你可以继续行动。" : own.Marker == other.Marker ? "双方同刻，你后到，所以仍由你行动；无需等待新回合。" : "你的时标更小，由你行动；每次结算后重新判断行动权。";
            }
            else
            {
                r.Window = ActionWindow.Wait; r.Title = "对手行动 · 暂不能出牌";
                r.Explanation = own.Marker == other.Marker ? "双方同刻，对手后到。等待其行动；若出现抢拍窗口会明确提示。" : "对手时标更小。你可查看手牌，暂不能出牌或攻击。";
            }
            return r;
        }
        public static HandAvailability Card(PlayerView v, int id)
        {
            var result = new HandAvailability();
            var window = For(v);
            if (!window.CanUseHand) { result.Label = window.Window == ActionWindow.Mulligan ? "点击标记换牌" : "暂不可打出"; result.Reason = window.Explanation; return result; }
            foreach (var c in v.Legal)
            {
                if (c.CardId != id) continue;
                if (c.Kind == CommandKind.Omen) result.CanCover = true;
                if (c.Kind == CommandKind.Play || c.Kind == CommandKind.Interject)
                { result.CanPlay = true; result.MinSand = Math.Min(result.MinSand, c.Sand); }
            }
            if (result.CanPlay)
            {
                result.Label = result.MinSand > 0 ? "需时砂 " + result.MinSand : window.Response ? "可抢拍" : "可打出";
                result.Reason = result.MinSand > 0 ? "至少投入 " + result.MinSand + " 时砂后才有合法出法；选牌后用右侧 + 调整。" : window.Response ? "可响应当前宣言；选择合法目标后确认抢拍。" : "可正面打出；选择目标或部署区后确认。";
            }
            else { result.Label = result.CanCover ? "仅可盖放" : "不可打出"; result.Reason = result.CanCover ? "目前不能正面打出，但可以作为伏笔盖放；普通牌盖放是虚张声势。" : "当前没有合法出法。"; }
            return result;
        }
    }
    public sealed class HandAvailability
    {
        public bool CanPlay, CanCover;
        public int MinSand = int.MaxValue;
        public string Label, Reason;
    }
}
