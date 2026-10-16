using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    public static partial class GameEngine
    {
        static int PlayerRef(EffectContext ctx, string reference) => reference == "$opponent" || reference == "$opponentKeeper" ? 1 - ctx.Player : reference == "$triggerer" ? ctx.Triggerer : ctx.Player;
        static List<Target> Select(GameState s, EffectContext ctx, string reference)
        {
            var list = new List<Target>();
            if (reference == "$t1") { if (ctx.Target.Kind != TargetKind.None) list.Add(ctx.Target); return list; }
            if (reference == "$self" || reference == "$entered") { int id = reference == "$self" ? ctx.Self : ctx.Entered; if (FindUnit(s, id) != null) list.Add(new Target(TargetKind.Unit, id)); return list; }
            if (reference == "$youKeeper" || reference == "$opponentKeeper") { list.Add(new Target(TargetKind.Keeper, PlayerRef(ctx, reference))); return list; }
            if (reference == "ENEMY_OBJECTS") { foreach (var o in s.Timeline) if (o.Owner != ctx.Player) list.Add(new Target(TargetKind.Object, o.Id)); return list; }
            Unit best = null;
            for (int p = 0; p < 2; p++)
            {
                bool friendly = p == ctx.Player;
                if (reference != "ALL_UNITS" && (reference == "ENEMY_UNITS" || reference == "ENEMY_UNIT_HIGHEST_ATK" ? friendly : !friendly)) continue;
                foreach (var u in s.Players[p].Board)
                {
                    if (reference == "FRIENDLY_GROWTH_UNITS" && (u.Keywords & Keyword.Growth) == 0) continue;
                    if (reference == "ENEMY_UNIT_HIGHEST_ATK") { if (best == null || u.Atk > best.Atk) best = u; }
                    else if (reference == "FRIENDLY_UNIT_LOWEST_HP") { if (best == null || u.Hp < best.Hp) best = u; }
                    else list.Add(new Target(TargetKind.Unit, u.Id));
                }
            }
            if (best != null) list.Add(new Target(TargetKind.Unit, best.Id)); return list;
        }
        static void ExecuteEffect(GameState s, Effect e, EffectContext ctx)
        {
            int amount = e.Amount / ctx.Scale;
            int p = PlayerRef(ctx, e.Player); var player = s.Players[p];
            switch (e.Op)
            {
                case Op.DRAW: Draw(s, p, e.Count); return;
                case Op.PAY_LIFE: LoseLife(s, p, amount); return;
                case Op.GAIN_SAND: player.Sand = Math.Min(s.Config.SandMax, player.Sand + (e.FromSacrificed ? Math.Min(e.Max, ctx.SacrificedTime) : amount)); return;
                case Op.PUSH_MARKER: Push(s, p, amount); return;
                case Op.NEXT_CARD_COST: player.Statuses.Add(new CostStatus { Delta = e.Delta, MinBaseTime = e.MinBaseTime, Duration = e.Duration }); return;
                case Op.PROPHECY:
                    int victim = PlayerRef(ctx, e.Target); s.Players[victim].Prophecy = ctx.Choice; s.Players[victim].ProphecyOwner = ctx.Player;
                    Emit(s, "Prophecy", "P" + (victim + 1) + " 下一次行动被预言为 " + ctx.Choice); return;
                case Op.COUNTER_DECLARED:
                    if (s.InFlight.Count > 1) { var f = s.InFlight[s.InFlight.Count - 2]; if (f.Card != null) { f.Countered = true; s.Players[f.Command.Player].Deck.Add(f.Card); } } return;
                case Op.REDIRECT_ATTACK:
                    if (s.InFlight.Count > 1) { var f = s.InFlight[s.InFlight.Count - 2]; f.Command.Target = new Target(TargetKind.Unit, ctx.Self); f.Redirected = true; } return;
                case Op.COPY_LAST_PLAYED:
                    int source = PlayerRef(ctx, e.From), destination = PlayerRef(ctx, e.To); string key = s.Players[source].LastPlayedCardKey;
                    if (key != null) AddToHand(s, destination, new CardInstance { Id = s.NextId++, Owner = destination, Key = key, CostDelta = e.CostDelta }); return;
                case Op.SCRY: case Op.PICK_FROM_TOP: case Op.SUMMON_FROM_PAST:
                    var pending = new PendingDecision { Player = p, Kind = e.Op == Op.SCRY ? PendingKind.Scry : e.Op == Op.PICK_FROM_TOP ? PendingKind.Pick : PendingKind.Summon };
                    if (e.Op == Op.SUMMON_FROM_PAST)
                    {
                        if (player.Board.Count >= s.Config.BoardMax) return;
                        foreach (var card in player.Past) { var d = s.Db.Card(card.Key); if (d.Type == "UNIT" && (e.Filter?.MaxBaseTime == null || d.Time <= e.Filter.MaxBaseTime)) pending.Options.Add(card.Id); }
                    }
                    else for (int i = 0; i < Math.Min(e.Count, player.Deck.Count); i++) pending.Options.Add(player.Deck[i].Id);
                    if (pending.Options.Count > 0) s.Pending = pending; return;
            }
            foreach (var target in Select(s, ctx, e.Target ?? e.Selector))
            {
                var u = target.Kind == TargetKind.Unit ? FindUnit(s, target.Id) : null;
                switch (e.Op)
                {
                    case Op.DAMAGE: case Op.DAMAGE_ALL: Damage(s, target, amount); break;
                    case Op.HEAL:
                        if (target.Kind == TargetKind.Keeper) s.Players[target.Id].Life = Math.Min(s.Config.LifeMax, s.Players[target.Id].Life + amount);
                        else if (u != null) u.Hp = Math.Min(u.MaxHp, u.Hp + amount); break;
                    case Op.SACRIFICE: if (u != null) ctx.SacrificedTime = s.Db.Card(u.Card.Key).Time; goto case Op.DESTROY;
                    case Op.DESTROY: case Op.DESTROY_ALL: if (u != null) u.Hp = 0; break;
                    case Op.BUFF: case Op.BUFF_ALL: if (u != null) ApplyBuff(u, e.Atk, e.Hp, e.Duration); break;
                    case Op.SHIELD: if (u != null) u.Shield = true; break;
                    case Op.STASIS: case Op.STASIS_ALL: if (u != null) { u.Stasis = true; u.Exhausted = true; } break;
                    case Op.REFRESH: if (u != null && !u.Stasis) { u.Exhausted = false; u.AttacksSinceReady = 0; } break;
                    case Op.PUSH_OBJECT: case Op.PUSH_ALL_OBJECTS:
                        var o = target.Kind == TargetKind.Object ? FindObject(s, target.Id) : null;
                        if (o != null) { o.Pos += amount; Emit(s, "PushObject", "时轴物件推迟至 " + o.Pos + " 刻"); } break;
                    case Op.GROW_ALL:
                        if (u != null && (u.Keywords & Keyword.Growth) != 0) for (int i = 0; i < e.Times; i++) ApplyBuff(u, 1, 1, "PERMANENT"); break;
                    default: throw new RulesViolationException("R-15", "未实现操作：" + e.Op);
                }
            }
        }
        static void ApplyBuff(Unit u, int atk, int hp, string duration)
        {
            u.Atk += atk; u.Hp += hp; u.MaxHp += hp;
            if (duration == "UNTIL_CONTROLLER_NEXT_CHIME") u.Buffs.Add(new Buff { Atk = atk, Hp = hp, Duration = duration });
        }
        static void EnterUnit(GameState s, CardInstance card, EffectContext original, bool recalled)
        {
            var d = s.Db.Card(card.Key); var p = s.Players[card.Owner]; if (p.Board.Count >= s.Config.BoardMax) { p.Past.Add(card); return; }
            var u = new Unit { Id = s.NextId++, Card = card, Atk = d.Atk, Hp = d.Hp, MaxHp = d.Hp, Cadence = d.Cadence, Keywords = d.Keywords, EnteredAtChime = p.ChimesRung, Exhausted = recalled || (d.Keywords & Keyword.Swift) == 0 };
            foreach (var a in Auras(s, card.Owner))
            {
                if (a.Kind == "STAT_AURA" && (a.Filter?.Keyword == null || (u.Keywords & (Keyword)Enum.Parse(typeof(Keyword), a.Filter.Keyword, true)) != 0)) ApplyBuff(u, a.Atk, a.Hp, "PERMANENT");
                if (a.Kind == "ENTER_ATK_PER_PAST_UNITS") { int n = 0; foreach (var c in p.Past) if (s.Db.Card(c.Key).Type == "UNIT") n++; u.Atk += n / Math.Max(1, a.Per) * a.Atk; }
            }
            p.Board.Add(u); var ctx = original.Clone(); ctx.Self = u.Id; ctx.Entered = u.Id;
            Emit(s, "UnitEntered", d.Name + " 入场" + (recalled ? "（召回，不触发部署）" : ""));
            if (!recalled) EnqueueAbilities(s, d.Abilities, "ON_PLAY", ctx, "unit:" + u.Id);
            Collect(s, "ON_FRIENDLY_UNIT_ENTER", card.Owner, ctx);
        }
        static void Damage(GameState s, Target target, int amount)
        {
            if (amount <= 0) return;
            if (target.Kind == TargetKind.Keeper) { LoseLife(s, target.Id, amount); return; }
            var u = FindUnit(s, target.Id); if (u == null) return;
            if (u.Shield) { u.Shield = false; Emit(s, "Shield", s.Db.Card(u.Card.Key).Name + " 的护盾破裂"); Collect(s, "ON_FRIENDLY_SHIELD_BROKEN", u.Card.Owner, new EffectContext { Player = u.Card.Owner, Self = u.Id }); }
            else { u.Hp -= amount; Emit(s, "Damage", s.Db.Card(u.Card.Key).Name + " 受到 " + amount + " 点伤害"); }
        }
        static void LoseLife(GameState s, int p, int amount)
        {
            var player = s.Players[p]; player.Life -= amount;
            if (player.Life > 0 && player.Life <= s.Config.RefluxThreshold && !player.RefluxUsed) player.RefluxPending = true;
            Emit(s, "Damage", "P" + (p + 1) + " 命烛 -" + amount);
        }
        static void AddToHand(GameState s, int p, CardInstance card)
        {
            if (s.Players[p].Hand.Count >= s.Config.HandMax) { s.Players[p].Past.Add(card); Emit(s, "Burn", "手牌已满，焚毁 " + s.Db.Card(card.Key).Name); }
            else { s.Players[p].Hand.Add(card); Emit(s, "Draw", "抽到 " + s.Db.Card(card.Key).Name, p); }
        }
        static void Draw(GameState s, int p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var deck = s.Players[p].Deck;
                if (deck.Count == 0) LoseLife(s, p, s.Config.EmptyDeckDamage);
                else { var card = deck[0]; deck.RemoveAt(0); AddToHand(s, p, card); }
            }
        }
        static void StateBased(GameState s)
        {
            if (s.Outcome != Outcome.Ongoing) return;
            bool dead0 = s.Players[0].Life <= 0, dead1 = s.Players[1].Life <= 0;
            if (dead0 || dead1) { End(s, dead0 && dead1 ? Outcome.Draw : dead0 ? Outcome.P1Win : Outcome.P0Win); return; }
            int first = s.InFlight.Count > 0 ? s.InFlight[s.InFlight.Count - 1].Command.Player : Math.Max(0, s.LastDeclaredActor);
            var dead = new List<Unit>();
            for (int index = 0; index < 2; index++)
            {
                var board = s.Players[index == 0 ? first : 1 - first].Board;
                for (int i = 0; i < board.Count;) if (board[i].Hp <= 0) { var u = board[i]; dead.Add(u); board.RemoveAt(i); s.Players[u.Card.Owner].Past.Add(u.Card); } else i++;
            }
            foreach (var u in dead)
            {
                s.Players[0].AnyUnitDied = s.Players[1].AnyUnitDied = true;
                Emit(s, "Death", s.Db.Card(u.Card.Key).Name + " 进入往昔");
                var ctx = new EffectContext { Player = u.Card.Owner, Self = u.Id };
                for (int i = 0; i < 2; i++)
                {
                    int p = i == 0 ? first : 1 - first;
                    if (p == u.Card.Owner) EnqueueAbilities(s, s.Db.Card(u.Card.Key).Abilities, "ON_DEATH", ctx, "unit:" + u.Id);
                    Collect(s, "ON_ANY_UNIT_DEATH", p, ctx);
                    if (p == u.Card.Owner) Collect(s, "ON_FRIENDLY_UNIT_DEATH", p, ctx);
                }
            }
        }
        static void End(GameState s, Outcome outcome) { s.Outcome = outcome; s.Pending = null; Emit(s, "GameOver", outcome == Outcome.Draw ? "时尽 · 平局" : (outcome == Outcome.P0Win ? "P1" : "P2") + " 获胜"); }
        static void Checkpoint(GameState s)
        {
            if (s.Outcome != Outcome.Ongoing) return;
            int first = s.InFlight.Count > 0 ? s.InFlight[s.InFlight.Count - 1].Command.Player : 0;
            for (int i = 0; i < 2; i++) { int p = i == 0 ? first : 1 - first; int count = s.Players[p].ProphecyDraw; s.Players[p].ProphecyDraw = 0; Draw(s, p, count); }
            StateBased(s); if (s.Outcome != Outcome.Ongoing) return;
            for (int i = 0; i < 2; i++)
            {
                int p = i == 0 ? first : 1 - first; var player = s.Players[p];
                if (player.RefluxPending) { Rewind(s, p, s.Config.RefluxRewind); player.RefluxPending = false; player.RefluxUsed = true; Emit(s, "Reflux", "P" + (p + 1) + " 触发逆流"); }
            }
            if (s.Players[0].Finished && s.Players[1].Finished)
            {
                int compare = s.Players[0].Life.CompareTo(s.Players[1].Life);
                if (compare == 0) { int[] atk = { 0, 0 }; for (int p = 0; p < 2; p++) foreach (var u in s.Players[p].Board) atk[p] += Math.Max(0, u.Atk); compare = atk[0].CompareTo(atk[1]); }
                if (compare == 0) compare = s.Players[0].Hand.Count.CompareTo(s.Players[1].Hand.Count);
                End(s, compare == 0 ? Outcome.Draw : compare > 0 ? Outcome.P0Win : Outcome.P1Win);
            }
        }
    }
}
