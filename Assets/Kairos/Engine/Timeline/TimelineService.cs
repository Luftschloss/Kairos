using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    public static partial class GameEngine
    {
        static void Place(GameState s, ObjectKind kind, CardInstance card, int position, EffectContext context)
        {
            var o = new TimelineObject { Id = s.NextId++, Owner = card.Owner, Kind = kind, Pos = position, Card = card, PlacedSeq = s.NextId, Context = context.Clone() };
            s.Timeline.Add(o); Emit(s, "ObjectPlaced", (kind == ObjectKind.Omen ? "伏笔" : s.Db.Card(card.Key).Name) + " 放置于第 " + position + " 刻");
        }
        // R-6-1/2: nested frames drain completely, before returning to the action frame.
        static void MoveForward(GameState s, int p, int delta)
        {
            var player = s.Players[p]; if (player.Finished || delta <= 0 || s.Outcome != Outcome.Ongoing) return;
            int from = player.Marker, target = Math.Min(s.Config.TimelineEnd, player.Marker + delta);
            while (player.Marker < target && s.Outcome == Outcome.Ongoing)
            {
                player.Marker++;
                ProcessObjects(s, p, true);
                if (s.Outcome != Outcome.Ongoing) break;
                ProcessObjects(s, p, false);
                if (s.Outcome != Outcome.Ongoing) break;
                int line = player.Marker;
                if (line < s.Config.TimelineEnd && line % s.Config.BarLength == 0 && line / s.Config.BarLength > player.ChimesRung) RunFrame(s, () => Chime(s, p, line));
            }
            if (from != player.Marker) { player.LastMoveSeq = ++s.MoveSeq; Emit(s, "Move", "P" + (p + 1) + " 时标 " + from + " → " + player.Marker); }
            if (player.Marker >= s.Config.TimelineEnd)
            {
                player.Finished = true;
                for (int i = s.Timeline.Count - 1; i >= 0; i--) if (s.Timeline[i].Owner == p && s.Timeline[i].Kind != ObjectKind.Omen) { if (s.Timeline[i].Kind == ObjectKind.Channel) player.Past.Add(s.Timeline[i].Card); s.Timeline.RemoveAt(i); }
            }
        }
        static void ProcessObjects(GameState s, int mover, bool omens)
        {
            var taken = new List<TimelineObject>();
            foreach (var o in s.Timeline) if (o.Pos <= s.Players[mover].Marker && (omens ? o.Kind == ObjectKind.Omen : o.Owner == mover && o.Kind != ObjectKind.Omen)) taken.Add(o);
            taken.Sort((a, b) => a.PlacedSeq.CompareTo(b.PlacedSeq));
            foreach (var o in taken) s.Timeline.Remove(o);
            foreach (var o in taken)
            {
                if (s.Outcome != Outcome.Ongoing) return;
                RunFrame(s, () =>
                {
                    var d = s.Db.Card(o.Card.Key); var ctx = o.Context.Clone(); ctx.Triggerer = mover; ctx.Self = o.Id;
                    if (o.Kind == ObjectKind.Echo) ctx.Scale = 2;
                    if (omens)
                    {
                        Emit(s, "OmenReveal", "伏笔揭示：" + d.Name + (d.Type == "OMEN" ? "" : "（虚张声势）"));
                        if (d.Type == "OMEN") EnqueueAbilities(s, d.Abilities, "ON_REVEAL", ctx, "object:" + o.Id);
                        s.Players[o.Owner].Past.Add(o.Card);
                        Collect(s, "ON_OWN_OMEN_REVEALED", o.Owner, ctx);
                    }
                    else
                    {
                        Emit(s, "Channel", d.Name + " 咏唱结算");
                        // Revalidate the locked target; never retarget a channel.
                        if (d.Targets.Count == 0 || Matches(s, o.Owner, ctx.Target, d.Targets[0])) EnqueueAbilities(s, d.Abilities, "ON_PLAY", ctx, "object:" + o.Id);
                        if (o.Kind == ObjectKind.Channel) s.Players[o.Owner].Past.Add(o.Card);
                    }
                });
            }
        }
        static void Chime(GameState s, int p, int line)
        {
            var player = s.Players[p]; player.ChimesRung = line / s.Config.BarLength;
            Emit(s, "Chime", "P" + (p + 1) + " 鸣钟 · 第 " + line + " 刻");
            Draw(s, p, 1);
            foreach (var u in player.Board)
            {
                if (u.Stasis) u.Stasis = false; else { u.Exhausted = false; u.AttacksSinceReady = 0; }
                foreach (var b in u.Buffs) { u.Atk -= b.Atk; u.MaxHp -= b.Hp; u.Hp = Math.Min(u.Hp, u.MaxHp); }
                u.Buffs.Clear();
            }
            player.Statuses.RemoveAll(x => x.Duration == "UNTIL_CONTROLLER_NEXT_CHIME");
            player.FocusUsed = 0; player.PushReceived = 0; player.TaxFreeUsed = false; player.ChainDiscountUsed = false; player.AnyUnitDied = false; player.UsedTriggers.Clear();
            Collect(s, "ON_YOUR_CHIME", p, new EffectContext { Player = p });
            int index = Array.IndexOf(s.Config.DuskBellLines, line);
            if (index >= 0) { LoseLife(s, p, s.Config.DuskBellDamage[index]); Emit(s, "Dusk", "暮钟伤害 " + s.Config.DuskBellDamage[index]); }
            StateBased(s);
        }
        static void Push(GameState s, int p, int amount)
        {
            var player = s.Players[p]; if (player.Finished) return;
            int allowed = Math.Min(amount, Math.Min(s.Config.PushSingleMax, s.Config.PushPerBarMax - player.PushReceived));
            if (allowed <= 0) return; player.PushReceived += allowed; MoveForward(s, p, allowed);
        }
        static void Rewind(GameState s, int p, int amount)
        {
            var player = s.Players[p]; if (player.Finished) return;
            int next = Math.Max(Math.Max(0, s.Players[1 - p].Marker - s.Config.RewindMaxGap), player.Marker - amount);
            if (next >= player.Marker) return; player.Marker = next; player.LastMoveSeq = ++s.MoveSeq;
        }
    }
}
