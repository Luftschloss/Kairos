using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    public static partial class GameEngine
    {
        // R-5: all external commands are validated before any mutation.
        public static IReadOnlyList<GameEvent> Apply(GameState s, Command command)
        {
            string error = IllegalReason(s, command); Rules.Require(error == null, "R-5-1", error);
            var c = command.Clone(); s.Events.Clear(); s.Commands.Add(c.Clone());
            if (c.Kind == CommandKind.Concede) { End(s, c.Player == 0 ? Outcome.P1Win : Outcome.P0Win); return s.Events; }
            if (c.Kind == CommandKind.Mulligan)
            {
                var p = s.Players[c.Player]; foreach (int id in c.Selection) { var card = FindCard(p.Hand, id); p.Hand.Remove(card); p.Deck.Add(card); }
                s.Rng.Shuffle(p.Deck); Draw(s, c.Player, c.Selection.Length); p.MulliganDone = true;
                Emit(s, "Mulligan", "P" + (c.Player + 1) + " 已完成调度"); return s.Events;
            }
            if (c.Kind == CommandKind.Choose) { ResolveChoice(s, c); Advance(s); return s.Events; }
            if (c.Kind == CommandKind.Pass) { s.Pending = null; s.InFlight[s.InFlight.Count - 1].Stage = 0; Advance(s); return s.Events; }
            if (c.Kind == CommandKind.Interject) { s.Pending = null; s.InFlight[s.InFlight.Count - 1].Stage = 0; }
            Declare(s, c); return s.Events;
        }
        static void Declare(GameState s, Command c)
        {
            var p = s.Players[c.Player]; int cost = Cost(s, c, true); int landing = p.Marker + cost;
            var ctx = new EffectContext { Player = c.Player, Target = c.Target, Choice = c.Choice, Kairos = landing >= s.Config.BarLength && landing <= s.Config.KairosLineMax && landing % s.Config.BarLength == 0, Chain = s.LastDeclaredActor == c.Player, Interjecting = c.Kind == CommandKind.Interject };
            if (p.Prophecy != null) { if (p.Prophecy == Category(s, c)) s.Players[p.ProphecyOwner].ProphecyDraw += 2; p.Prophecy = null; }
            p.ChainLength = ctx.Chain ? p.ChainLength + 1 : 1; s.Players[1 - c.Player].ChainLength = 0; s.LastDeclaredActor = c.Player;
            var f = new InFlightAction { Command = c, Cost = cost, StartMarker = p.Marker, Context = ctx };
            if (IsCard(c) || c.Kind == CommandKind.Omen) { f.Card = FindCard(p.Hand, c.CardId); p.Hand.Remove(f.Card); }
            if (c.Kind == CommandKind.Attack) { var u = FindUnit(s, c.UnitId); u.Exhausted = true; u.AttacksSinceReady++; }
            if (c.Kind == CommandKind.Ability) p.AbilityReadyAt = p.ChimesRung + s.Db.Keeper(p.Keeper).Ability.CooldownBars;
            if (c.Kind == CommandKind.Focus) p.FocusUsed++;
            s.InFlight.Add(f);
            string action = f.Card != null ? c.Kind == CommandKind.Omen ? "埋下伏笔" : "打出 " + s.Db.Card(f.Card.Key).Name : c.Kind == CommandKind.Attack ? "宣言攻击" : c.Kind == CommandKind.Ability ? s.Db.Keeper(p.Keeper).Ability.Name : c.Kind == CommandKind.Focus ? "蓄势" : "静候";
            Emit(s, "Declared", "P" + (c.Player + 1) + " " + action + " · " + cost + " 刻" + (ctx.Kairos ? " / 契机" : "") + (ctx.Chain ? " / 连拍" : ""));
            RunFrame(s, () => Collect(s, "ON_CHAIN_LENGTH", c.Player, ctx));
            if (s.Outcome != Outcome.Ongoing) return;
            if (c.Kind == CommandKind.Play || c.Kind == CommandKind.Attack || c.Kind == CommandKind.Ability)
            {
                s.Pending = new PendingDecision { Kind = PendingKind.Interject, Player = 1 - c.Player };
                var legal = new List<Command>(); LegalCommands(s, 1 - c.Player, legal);
                if (legal.Count > 1) { f.Stage = -1; Emit(s, "Interject", "请选择抢拍或放弃", 1 - c.Player); return; }
                s.Pending = null;
            }
            Advance(s);
        }
        static void Advance(GameState s)
        {
            while (s.InFlight.Count > 0 && s.Outcome == Outcome.Ongoing)
            {
                var f = s.InFlight[s.InFlight.Count - 1];
                if (f.Stage < 0 || s.Pending != null) return;
                if (f.Stage == 0) { f.Stage = 1; ResolveAction(s, f); }
                if (!Drain(s)) return;
                if (s.Outcome != Outcome.Ongoing) break;
                MoveForward(s, f.Command.Player, f.Cost);
                Checkpoint(s);
                s.InFlight.RemoveAt(s.InFlight.Count - 1);
            }
            if (s.Outcome != Outcome.Ongoing) { s.Pending = null; s.InFlight.Clear(); foreach (var frame in s.Frames) frame.Clear(); }
        }
        static void ResolveAction(GameState s, InFlightAction f)
        {
            var c = f.Command; var p = s.Players[c.Player]; var ctx = f.Context;
            if (f.Countered) { Emit(s, "Fizzle", "原行动被反制，仍支付时间"); return; }
            if (c.Kind == CommandKind.Attack)
            {
                var u = FindUnit(s, c.UnitId); var target = c.Target;
                if (u == null || target.Kind == TargetKind.Unit && FindUnit(s, target.Id) == null || !f.Redirected && !CanAttackTarget(s, c.Player, target)) { Emit(s, "Fizzle", "攻击失效，仍支付时间"); return; }
                int counter = target.Kind == TargetKind.Unit ? Math.Max(0, FindUnit(s, target.Id).Atk) : 0;
                Damage(s, target, Math.Max(0, u.Atk)); Damage(s, new Target(TargetKind.Unit, u.Id), counter); StateBased(s); return;
            }
            if (c.Kind == CommandKind.Focus) { Draw(s, c.Player, 1); StateBased(s); return; }
            if (c.Kind == CommandKind.Wait) { Collect(s, "ON_YOUR_WAIT", c.Player, ctx); return; }
            if (c.Kind == CommandKind.Ability)
            {
                var abilityDef = s.Db.Keeper(p.Keeper).Ability;
                if (!TargetsLegal(s, c, abilityDef.Targets)) { Emit(s, "Fizzle", "时器目标失效，仍支付时间"); return; }
                Enqueue(s, abilityDef.Effects, ctx); return;
            }
            var d = s.Db.Card(f.Card.Key);
            if (c.Kind == CommandKind.Omen) { Place(s, ObjectKind.Omen, f.Card, f.StartMarker + f.Cost + c.Offset, ctx); return; }
            p.LastPlayedCardKey = d.Key;
            if (!TargetsLegal(s, c, d.Targets)) { p.Past.Add(f.Card); Emit(s, "Fizzle", "目标已不合法，原行动失效"); return; }
            if (d.Type == "UNIT" && p.Board.Count >= s.Config.BoardMax || d.Type == "SIGIL" && p.Sigils.Count >= s.Config.SigilMax)
            { p.Past.Add(f.Card); Emit(s, "Fizzle", "场地已满，原行动失效"); return; }
            if (d.Channel.HasValue) { Place(s, ObjectKind.Channel, f.Card, f.StartMarker + f.Cost + d.Channel.Value, ctx); return; }
            if (d.Type == "UNIT") EnterUnit(s, f.Card, ctx, false);
            else
            {
                if (d.Type == "SIGIL") p.Sigils.Add(f.Card); else p.Past.Add(f.Card);
                ctx.Self = f.Card.Id; EnqueueAbilities(s, d.Abilities, "ON_PLAY", ctx, "card:" + f.Card.Id);
            }
            if (d.Echo.HasValue) Place(s, ObjectKind.Echo, f.Card.Clone(), p.Marker + d.Echo.Value, ctx);
        }
        static void Enqueue(GameState s, List<Effect> effects, EffectContext ctx)
        { if (effects.Count > 0) s.Frames[s.Frames.Count - 1].Add(new EffectTask { Steps = new List<Effect>(effects), Context = ctx.Clone() }); }
        static void EnqueueAbilities(GameState s, List<Ability> abilities, string trigger, EffectContext ctx, string source)
        {
            foreach (var a in abilities)
            {
                if (a.Trigger != trigger || !ConditionMet(s, a.Condition, ctx)) continue;
                var p = s.Players[ctx.Player]; if (a.Length > 0 && p.ChainLength != a.Length) continue;
                string key = source + ":" + trigger;
                if (a.Limit == "ONCE_PER_YOUR_BAR") { if (p.UsedTriggers.Contains(key)) continue; p.UsedTriggers.Add(key); }
                Enqueue(s, a.Effects, ctx);
            }
        }
        static void Collect(GameState s, string trigger, int owner, EffectContext original)
        {
            var p = s.Players[owner];
            foreach (var u in p.Board) { var ctx = original.Clone(); ctx.Player = owner; ctx.Self = u.Id; EnqueueAbilities(s, s.Db.Card(u.Card.Key).Abilities, trigger, ctx, "unit:" + u.Id); }
            foreach (var c in p.Sigils) { var ctx = original.Clone(); ctx.Player = owner; ctx.Self = c.Id; EnqueueAbilities(s, s.Db.Card(c.Key).Abilities, trigger, ctx, "sigil:" + c.Id); }
            var kc = original.Clone(); kc.Player = owner; kc.Self = 0; EnqueueAbilities(s, s.Db.Keeper(p.Keeper).Abilities, trigger, kc, "keeper");
        }
        static bool Drain(GameState s)
        {
            var queue = s.Frames[s.Frames.Count - 1];
            while (queue.Count > 0 && s.Outcome == Outcome.Ongoing)
            {
                var task = queue[0];
                while (task.Index < task.Steps.Count)
                {
                    var e = task.Steps[task.Index++];
                    if (e.Op == Op.IF) { task.Steps.InsertRange(task.Index, ConditionMet(s, e.Condition, task.Context) ? e.Then : e.Else); continue; }
                    ExecuteEffect(s, e, task.Context);
                    if (s.Pending != null) { Rules.Require(s.Frames.Count == 1, "R-9-4", "被动效果不能暂停"); return false; }
                }
                queue.RemoveAt(0); StateBased(s);
            }
            return true;
        }
        static void RunFrame(GameState s, Action body)
        { s.Frames.Add(new List<EffectTask>()); body(); Drain(s); s.Frames.RemoveAt(s.Frames.Count - 1); }
        static bool ConditionMet(GameState s, Condition condition, EffectContext ctx)
        {
            if (condition == null) return true;
            switch (condition.Kind)
            {
                case "KAIROS": return ctx.Kairos;
                case "CHAIN_BEAT": return ctx.Chain;
                case "INTERJECTING": return ctx.Interjecting;
                case "TRIGGERER_IS_OPPONENT": return ctx.Triggerer == 1 - ctx.Player;
                case "TRIGGERER_IS_SELF": return ctx.Triggerer == ctx.Player;
                case "ANY_UNIT_DIED_THIS_BAR": return s.Players[ctx.Player].AnyUnitDied;
                case "YOUR_LIFE_LT_OPPONENT": return s.Players[ctx.Player].Life < s.Players[1 - ctx.Player].Life;
                case "TARGET_DIED": var u = FindUnit(s, ctx.Target.Id); return ctx.Target.Kind == TargetKind.Unit && (u == null || u.Hp <= 0);
                default: throw new RulesViolationException("R-15", "未知条件：" + condition.Kind);
            }
        }
        static void ResolveChoice(GameState s, Command c)
        {
            var pending = s.Pending; var p = s.Players[c.Player]; s.Pending = null;
            if (pending.Kind == PendingKind.Summon)
            {
                var card = FindCard(p.Past, c.Selection[0]); p.Past.Remove(card);
                EnterUnit(s, card, new EffectContext { Player = c.Player }, true);
            }
            else
            {
                var cards = new List<CardInstance>(); foreach (int id in c.Selection) { var card = FindCard(p.Deck, id); p.Deck.Remove(card); cards.Add(card); }
                if (pending.Kind == PendingKind.Pick) { AddToHand(s, c.Player, cards[0]); cards.RemoveAt(0); p.Deck.AddRange(cards); }
                else { for (int i = c.TopCount - 1; i >= 0; i--) p.Deck.Insert(0, cards[i]); for (int i = c.TopCount; i < cards.Count; i++) p.Deck.Add(cards[i]); }
            }
            Emit(s, "Choice", "P" + (c.Player + 1) + " 完成选择"); StateBased(s);
        }
    }
}
