using System;
using Kairos.Engine;

namespace Kairos.Ai
{
    // Deliberately bounded L1 policy. Reads a redacted view, never the complete state.
    public static class GreedyAi
    {
        public static Command Choose(PlayerView view, CardDatabase db)
        {
            Command best = null; int score = int.MinValue;
            foreach (var c in view.Legal) { int n = Score(view, db, c); if (n > score) { score = n; best = c; } }
            return best?.Clone();
        }
        public static int Score(PlayerView v, CardDatabase db, Command c)
        {
            if (c.Kind == CommandKind.Mulligan) return 0;
            if (c.Kind == CommandKind.Choose)
            {
                if (v.Pending.Kind == PendingKind.Summon) { var card = GameEngine.FindCard(v.Players[c.Player].Past, c.Selection[0]); var d = db.Card(card.Key); return d.Atk * 3 + d.Hp; }
                int value = 0; for (int i = 0; i < c.Selection.Length; i++) { var card = GameEngine.FindCard(v.Players[c.Player].Deck, c.Selection[i]); var d = db.Card(card.Key); value += (c.Selection.Length - i) * (d.Type == "UNIT" ? 8 : 4); }
                return value + c.TopCount;
            }
            int cost = v.Cost(c); var p = v.Players[c.Player];
            if (c.Kind == CommandKind.Pass) return 0;
            if (c.Kind == CommandKind.Wait) return -20;
            if (c.Kind == CommandKind.Focus) return p.Hand.Count < 3 ? 30 : 3;
            Unit target = null; foreach (var side in v.Players) foreach (var u in side.Board) if (c.Target.Kind == TargetKind.Unit && u.Id == c.Target.Id) target = u;
            if (c.Kind == CommandKind.Attack)
            {
                Unit attacker = null; foreach (var u in p.Board) if (u.Id == c.UnitId) attacker = u;
                if (attacker == null) return -100;
                if (c.Target.Kind == TargetKind.Keeper) return attacker.Atk >= v.Players[1 - c.Player].Life ? 10000 : 35 + attacker.Atk * 4 - cost;
                return 20 + (target.Hp <= attacker.Atk ? target.Atk * 4 + 8 : 0) - (attacker.Hp <= target.Atk ? attacker.Atk * 3 : 0) - cost;
            }
            if (c.Kind == CommandKind.Omen) { var card = GameEngine.FindCard(p.Hand, c.CardId); return db.Card(card.Key).Type == "OMEN" ? 9 - c.Offset : -30; }
            if (c.Kind == CommandKind.Ability) return 12 - cost + (target != null && target.Card.Owner == c.Player ? 5 : 0);
            var instance = GameEngine.FindCard(p.Hand, c.CardId); if (instance == null) return -100;
            var def = db.Card(instance.Key); int score = 14 - cost * 2 - c.Sand;
            if (def.Type == "UNIT") score += def.Atk * 3 + def.Hp * 2 + ((def.Keywords & Keyword.Swift) != 0 ? 8 : 0);
            foreach (var ability in def.Abilities) foreach (var e in ability.Effects)
            {
                bool enemy = c.Target.Kind == TargetKind.Keeper ? c.Target.Id != c.Player : target != null && target.Card.Owner != c.Player;
                if (e.Op == Op.DAMAGE) score += !enemy ? -100 : c.Target.Kind == TargetKind.Keeper && e.Amount >= v.Players[1 - c.Player].Life && !def.Channel.HasValue ? 10000 : 18 + (target != null && target.Hp <= e.Amount ? 12 : 0);
                if (e.Op == Op.STASIS || e.Op == Op.DESTROY || e.Op == Op.BUFF && e.Hp < 0) score += enemy ? 20 : -100;
                if (e.Op == Op.SHIELD) score += target != null && !target.Shield ? 10 : -40;
                if (e.Op == Op.PUSH_OBJECT) { foreach (var o in v.Timeline) if (o.Id == c.Target.Id) score += o.Owner == c.Player ? -40 : 6; }
                if (e.Op == Op.DRAW) score += p.Hand.Count < 5 ? 8 : -8;
            }
            return score;
        }
    }
}
