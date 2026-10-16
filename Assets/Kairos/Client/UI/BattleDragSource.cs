using System;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Kairos.Client
{
    public enum BattleHitKind { Target, Deploy, Tick }
    public sealed class BattleHit : MonoBehaviour
    {
        public BattleHitKind Kind;
        public int Value;
        public Target Target;
    }
    // Sources may be rebuilt after selection. Pointer ownership transfers to the
    // persistent router before that happens, so release still arrives exactly once.
    public sealed class BattleDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public GameController Owner;
        public int CardId, UnitId;
        public void OnBeginDrag(PointerEventData e) { if (e.button == PointerEventData.InputButton.Left) Owner.BeginBattleDrag(e, CardId, UnitId); }
        public void OnDrag(PointerEventData e) { }
        public void OnEndDrag(PointerEventData e) { }
    }
    public sealed class BattleDragRouter : MonoBehaviour, IDragHandler, IEndDragHandler
    {
        public GameController Owner;
        public void OnDrag(PointerEventData e) => Owner.MoveBattleDrag(e);
        public void OnEndDrag(PointerEventData e) => Owner.EndBattleDrag(e);
    }
}
