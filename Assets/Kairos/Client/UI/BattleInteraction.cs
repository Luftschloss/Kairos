using System.Collections.Generic;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        BattleDragRouter dragRouter;
        bool dragging;
        int dragSerial;
        RectTransform dragLine, dragTip;
        Text dragText;
        readonly List<RaycastResult> dragHits = new List<RaycastResult>();
        bool BattleInput => CanAct && !help && !leaving && View.MainPhase && (View.Pending == null || View.Pending.Kind == PendingKind.Interject);

        void BindHit(Transform rect, BattleHitKind kind, int value = 0)
        { var h = rect.gameObject.GetComponent<BattleHit>() ?? rect.gameObject.AddComponent<BattleHit>(); h.Kind = kind; h.Value = value; }
        void BindTarget(Button button, Target target)
        {
            var h = button.gameObject.GetComponent<BattleHit>() ?? button.gameObject.AddComponent<BattleHit>(); h.Kind = BattleHitKind.Target; h.Target = target;
            if (!targetRects.ContainsKey(target)) targetRects[target] = button.GetComponent<RectTransform>();
        }
        void BindSource(RectTransform rect, int card, int unit)
        {
            var source = rect.gameObject.GetComponent<BattleDragSource>() ?? rect.gameObject.AddComponent<BattleDragSource>(); source.Owner = this; source.CardId = card; source.UnitId = unit;
            if (selectedCard != 0 && selectedCard == card || selectedUnit != 0 && selectedUnit == unit) sourceRect = rect;
        }
        void SelectHand(int id)
        {
            var d = Database.Card(GameEngine.FindCard(View.Players[viewer].Hand, id).Key);
            ClearSelection(); selectedCard = id; placingOmen = d.Type == "OMEN"; offset = View.Config.OmenOffsetMin;
            if (d.Choice != null && d.Choice.Length > 0) prophecy = d.Choice[0];
        }
        public void BeginBattleDrag(PointerEventData e, int card, int unit)
        {
            if (!BattleInput || card != 0 && !TutorialCardSelectable(card) || unit != 0 && !TutorialUnitSelectable(viewer, unit)) return;
            if (unit != 0 && !HasAction(CommandKind.Attack, unit)) return;
            if (card != 0) { var allowed = ActionAvailability.Card(View, card); if (!allowed.CanPlay && !allowed.CanCover) return; }
            // Keep a selected card's explicit sand / prophecy / cover settings.
            if (card != 0) { if (selectedCard != card) SelectHand(card); }
            else { ClearSelection(); selectedUnit = unit; }
            selectedTarget = default; RebuildBattle();
            if (dragRouter == null) { dragRouter = gameObject.AddComponent<BattleDragRouter>(); dragRouter.Owner = this; }
            e.pointerDrag = dragRouter.gameObject; e.eligibleForClick = false; e.pointerPress = null;
            dragging = true; dragSerial = decisionSerial;
            dragLine = EffectWidget("ActionLine", new Rect(0, 0, 1, 4)); dragLine.name = "DragArrow";
            dragTip = EffectWidget("DragTip", new Rect(0, 0, 330, 62));
            dragText = dragTip.GetComponentInChildren<Text>();
            MoveBattleDrag(e);
        }
        BattleHit HitAt(PointerEventData e)
        {
            dragHits.Clear(); EventSystem.current.RaycastAll(e, dragHits);
            foreach (var hit in dragHits)
            {
                var h = hit.gameObject.GetComponentInParent<BattleHit>();
                // The topmost blocking UI is authoritative; never drop through a modal.
                return h;
            }
            return null;
        }
        Command DropCommand(BattleHit hit)
        {
            var c = Draft(); if (hit == null || c == null) return null;
            if (c.Kind == CommandKind.Omen)
            {
                if (hit.Kind != BattleHitKind.Tick) return null;
                c.Offset = hit.Value - View.Players[viewer].Marker - View.Config.OmenSetCost;
            }
            else if (hit.Kind == BattleHitKind.Target) c.Target = hit.Target;
            else if (hit.Kind == BattleHitKind.Deploy && c.CardId != 0) c.Target = default;
            else return null;
            return GameEngine.IllegalReason(State, c) == null && LessonAllows(c) ? c : null;
        }
        Vector2 RootPoint(Vector2 screen)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, null, out var point);
            return new Vector2(point.x - root.rect.xMin, root.rect.yMax - point.y);
        }
        Vector2 Center(RectTransform rect) => RootPoint(RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center)));
        void SetLine(RectTransform line, Vector2 from, Vector2 to)
        {
            line.anchoredPosition = new Vector2(from.x, -from.y); line.pivot = new Vector2(0, .5f);
            Vector2 delta = new Vector2(to.x - from.x, from.y - to.y);
            line.sizeDelta = new Vector2(delta.magnitude, 3); line.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }
        public void MoveBattleDrag(PointerEventData e)
        {
            if (!dragging || dragSerial != decisionSerial || !BattleInput) return;
            var c = DropCommand(HitAt(e)); Vector2 point = RootPoint(e.position);
            if (sourceRect != null) SetLine(dragLine, Center(sourceRect), point);
            dragLine.GetComponent<Image>().color = c == null ? red : gold;
            dragTip.anchoredPosition = new Vector2(Mathf.Clamp(point.x + 20, 0, 1270), -Mathf.Clamp(point.y - 70, 75, 815));
            dragText.text = c == null ? "未在合法落区 · 松手取消\n出牌拖到部署区 / 目标；伏笔拖到刻度" : "松手提交 · " + (c.Target.Kind == TargetKind.None ? c.Kind == CommandKind.Omen ? "盖放 @" + Preview(c).FuturePosition : "部署 / 打出" : TargetName(c.Target)) + "\n" + Preview(c).From + " → " + Preview(c).Landing + "刻";
            if (timeSummary != null) timeSummary.text = PreviewText(c ?? Draft());
        }
        public void EndBattleDrag(PointerEventData e)
        {
            if (!dragging) return;
            var c = dragSerial == decisionSerial && BattleInput ? DropCommand(HitAt(e)) : null;
            dragging = false;
            if (c != null) Submit(c); else CancelDraft();
        }
        void DrawActionLinks()
        {
            if (sourceRect != null && targetRects.TryGetValue(selectedTarget, out var target)) DrawLink(sourceRect, target);
            var draft = Draft(); var preview = Preview(draft);
            if (preview != null && preview.FuturePosition >= 0 && futureRect != null && targetRects.TryGetValue(selectedTarget, out var locked)) DrawLink(futureRect, locked);
            if (selectedTarget.Kind == TargetKind.Object)
            {
                var o = View.Timeline.Find(x => x.Id == selectedTarget.Id);
                if (o != null && o.Kind != ObjectKind.Omen && o.Context != null && targetRects.TryGetValue(selectedTarget, out var from) && targetRects.TryGetValue(o.Context.Target, out var to)) DrawLink(from, to);
            }
        }
        void DrawLink(RectTransform from, RectTransform to)
        {
            var line = EffectWidget("ActionLine", new Rect(0, 0, 1, 3)); line.GetComponent<Image>().color = new Color(gold.r, gold.g, gold.b, .6f);
            SetLine(line, Center(from), Center(to));
            var end = Center(to); EffectWidget("TargetDot", new Rect(end.x - 10, end.y - 12, 24, 26));
        }
    }
}
