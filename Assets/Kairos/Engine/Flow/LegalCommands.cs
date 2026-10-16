using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    public static partial class GameEngine
    {
        public static GameState Create(RulesConfig config, CardDatabase db, DeckList d0, DeckList d1, string seed, bool campaign = false)
        {
            config.Validate(); db.ValidateDeck(d0, config, campaign); db.ValidateDeck(d1, config, campaign);
            var s = new GameState { Config = config.Clone(), Db = db, Seed = seed, Rng = new KairosRng(seed) };
            int first = s.Rng.Range(2);
            var decks = new[] { d0, d1 };
            for (int p = 0; p < 2; p++)
            {
                var player = new PlayerState { Id = p, Keeper = decks[p].Keeper, Life = config.LifeStart, LastMoveSeq = p == first ? 1 : 0 };
                s.Players[p] = player;
                foreach (string key in decks[p].Cards) player.Deck.Add(new CardInstance { Id = s.NextId++, Key = key, Owner = p });
                s.Rng.Shuffle(player.Deck);
            }
            for (int p = 0; p < 2; p++) Draw(s, p, config.HandStart + (p == first ? 0 : config.SecondPlayerBonusCards));
            Emit(s, "Setup", "钟碎之日 · 双方调度起手，P" + (first + 1) + " 先行"); return s;
        }
        public static int NextActor(GameState s)
        {
            if (s.Outcome != Outcome.Ongoing) return -1;
            if (s.Pending != null) return s.Pending.Player;
            if (!s.MainPhase) return !s.Players[0].MulliganDone ? 0 : 1;
            var a = s.Players[0]; var b = s.Players[1];
            if (a.Finished && b.Finished) return -1;
            if (a.Finished) return 1; if (b.Finished) return 0;
            return a.Marker != b.Marker ? (a.Marker < b.Marker ? 0 : 1) : (a.LastMoveSeq > b.LastMoveSeq ? 0 : 1);
        }
        public static int Horizon(GameState s, int p) => Math.Min(s.Config.HorizonMax, s.Config.HorizonBase + s.Players[p].ChimesRung);
        public static Unit FindUnit(GameState s, int id) { foreach (var p in s.Players) foreach (var u in p.Board) if (u.Id == id) return u; return null; }
        public static TimelineObject FindObject(GameState s, int id) { foreach (var o in s.Timeline) if (o.Id == id) return o; return null; }
        public static CardInstance FindCard(List<CardInstance> zone, int id) { foreach (var c in zone) if (c.Id == id) return c; return null; }
        static bool IsCard(Command c) => c.Kind == CommandKind.Play || c.Kind == CommandKind.Interject;
        public static string Category(GameState s, Command c)
        {
            if (c.Kind == CommandKind.Attack) return "ATTACK";
            if (!IsCard(c)) return "OTHER";
            var card = FindCard(s.Players[c.Player].Hand, c.CardId);
            return card != null && s.Db.Card(card.Key).Type == "UNIT" ? "UNIT" : "SPELL";
        }
        static List<Aura> Auras(GameState s, int p)
        {
            var list = new List<Aura>();
            foreach (var u in s.Players[p].Board) list.AddRange(s.Db.Card(u.Card.Key).Static);
            foreach (var c in s.Players[p].Sigils) list.AddRange(s.Db.Card(c.Key).Static);
            list.AddRange(s.Db.Keeper(s.Players[p].Keeper).Static); return list;
        }
        public static int Cost(GameState s, Command c, bool consume = false)
        {
            var p = s.Players[c.Player]; var C = s.Config;
            if (c.Kind == CommandKind.Wait) return Math.Max(1, Math.Min(C.TimelineEnd, s.Players[1 - c.Player].Marker + 1) - p.Marker);
            if (c.Kind == CommandKind.Focus) return C.FocusCost;
            if (c.Kind == CommandKind.Omen) return C.OmenSetCost;
            CardInstance instance = IsCard(c) ? FindCard(p.Hand, c.CardId) : null;
            CardDefinition def = instance == null ? null : s.Db.Card(instance.Key);
            int cost = def != null ? def.Time + instance.CostDelta : c.Kind == CommandKind.Attack ? FindUnit(s, c.UnitId)?.Cadence ?? 999 : s.Db.Keeper(p.Keeper).Ability.Cost;
            foreach (var a in Auras(s, 1 - c.Player))
                if (a.Kind == "COST_MOD" && (a.AppliesTo == "OPPONENT_ATTACKS" && c.Kind == CommandKind.Attack || a.AppliesTo == "OPPONENT_CARDS" && def != null && (!a.Filter.HasChannel || def.Channel.HasValue))) cost += a.Delta;
            if (def != null)
            {
                for (int i = p.Statuses.Count - 1; i >= 0; i--) if (def.Time >= p.Statuses[i].MinBaseTime) { cost += p.Statuses[i].Delta; if (consume) p.Statuses.RemoveAt(i); }
            }
            if (p.Prophecy != null && p.Prophecy == Category(s, c)) cost++;
            if (c.Kind == CommandKind.Interject)
            {
                bool free = false; foreach (var a in Auras(s, c.Player)) if (a.Kind == "INTERJECT_TAX_FREE" && !p.TaxFreeUsed) free = true;
                cost += free ? 0 : C.InterjectTax; if (consume && free) p.TaxFreeUsed = true;
            }
            if (s.LastDeclaredActor == c.Player && !p.ChainDiscountUsed)
            {
                foreach (var a in Auras(s, c.Player)) if (a.Kind == "COST_MOD" && a.AppliesTo == "YOUR_ACTIONS" && a.Cond == "CHAIN_BEAT") { cost += a.Delta; if (consume) p.ChainDiscountUsed = true; break; }
            }
            if (def != null) { cost -= c.Sand; if (consume) p.Sand -= c.Sand; }
            return Math.Max(C.MinActionCost, cost);
        }
        public static bool Matches(GameState s, int p, Target t, TargetSpec spec)
        {
            if (t.Kind == TargetKind.None) return spec.Optional;
            if (spec.Selector == "TIMELINE_OBJECT") return t.Kind == TargetKind.Object && FindObject(s, t.Id) != null;
            if (t.Kind == TargetKind.Keeper) return spec.Selector == "ANY_CHARACTER" && t.Id >= 0 && t.Id < 2;
            if (t.Kind != TargetKind.Unit) return false;
            var u = FindUnit(s, t.Id); if (u == null) return false;
            if (spec.Selector == "FRIENDLY_UNIT" && u.Card.Owner != p || spec.Selector == "ENEMY_UNIT" && u.Card.Owner == p) return false;
            var f = spec.Filter; if (f == null) return true;
            var d = s.Db.Card(u.Card.Key);
            return (!f.MinAtk.HasValue || u.Atk >= f.MinAtk) && (!f.MaxBaseTime.HasValue || d.Time <= f.MaxBaseTime) &&
                (!f.EnteredThisBar || u.EnteredAtChime == s.Players[p].ChimesRung) && (!f.NotAttacked || u.AttacksSinceReady == 0) && (!f.Exhausted || u.Exhausted) && (!f.NotStasis || !u.Stasis) &&
                (f.Keyword == null || (u.Keywords & (Keyword)Enum.Parse(typeof(Keyword), f.Keyword, true)) != 0);
        }
        static bool TargetsLegal(GameState s, Command c, List<TargetSpec> specs) => specs.Count == 0 ? c.Target.Kind == TargetKind.None : Matches(s, c.Player, c.Target, specs[0]);
        static bool CanAttackTarget(GameState s, int p, Target t)
        {
            bool guard = false; foreach (var u in s.Players[1 - p].Board) if ((u.Keywords & Keyword.Guard) != 0) guard = true;
            if (t.Kind == TargetKind.Keeper) return t.Id == 1 - p && !guard;
            var unit = t.Kind == TargetKind.Unit ? FindUnit(s, t.Id) : null;
            return unit != null && unit.Card.Owner != p && (!guard || (unit.Keywords & Keyword.Guard) != 0);
        }
        static int ObjectCount(GameState s, int p, ObjectKind kind) { int count = 0; foreach (var o in s.Timeline) if (o.Owner == p && o.Kind == kind) count++; return count; }
        static int OmenCap(GameState s, int p) { int cap = s.Config.OmenMax; foreach (var a in Auras(s, p)) if (a.Kind == "OMEN_CAP_MOD") cap += a.Delta; return cap; }
        static bool ResponseCondition(GameState s, CardDefinition d)
        {
            if (s.InFlight.Count == 0) return false;
            var f = s.InFlight[s.InFlight.Count - 1];
            if (d.InterjectCondition == "DECLARED_ATTACK") return f.Command.Kind == CommandKind.Attack;
            if (d.InterjectCondition == "DECLARED_SPELL") return f.Card != null && s.Db.Card(f.Card.Key).Type != "UNIT";
            if (d.InterjectCondition == "DECLARED_UNIT") return f.Card != null && s.Db.Card(f.Card.Key).Type == "UNIT";
            return true;
        }
        public static string IllegalReason(GameState s, Command c)
        {
            if (c == null || c.Player < 0 || c.Player > 1) return "无效玩家";
            if (s.Outcome != Outcome.Ongoing) return "对局已结束";
            if (c.Kind == CommandKind.Concede) return null;
            var p = s.Players[c.Player];
            if (!s.MainPhase)
            {
                if (c.Kind != CommandKind.Mulligan || p.MulliganDone) return "请先完成调度";
                var ids = new List<int>(); foreach (int id in c.Selection) { if (ids.Contains(id) || FindCard(p.Hand, id) == null) return "调度牌不合法"; ids.Add(id); } return null;
            }
            if (s.Pending != null)
            {
                if (c.Player != s.Pending.Player) return "等待对方选择";
                if (s.Pending.Kind != PendingKind.Interject)
                {
                    if (c.Kind != CommandKind.Choose) return "请完成效果选择";
                    if (s.Pending.Kind == PendingKind.Summon) return c.Selection.Length == 1 && s.Pending.Options.Contains(c.Selection[0]) ? null : "请选择往昔中的侍从";
                    if (c.Selection.Length != s.Pending.Options.Count) return "请选择所有牌的顺序";
                    var seen = new List<int>(); foreach (int id in c.Selection) { if (seen.Contains(id) || !s.Pending.Options.Contains(id)) return "选择不合法"; seen.Add(id); }
                    if (s.Pending.Kind == PendingKind.Scry && (c.TopCount < 0 || c.TopCount > c.Selection.Length)) return "置顶数量不合法";
                    return null;
                }
                if (c.Kind == CommandKind.Pass) return null;
                if (c.Kind != CommandKind.Interject) return "只能抢拍或放弃";
            }
            else if (c.Player != NextActor(s) || c.Kind == CommandKind.Interject) return "当前没有行动权";
            if (p.Finished) return "已到达时轴终点";
            if (c.Sand < 0 || c.Sand > p.Sand || !IsCard(c) && c.Sand != 0) return "时砂数量不合法";
            switch (c.Kind)
            {
                case CommandKind.Play: case CommandKind.Interject:
                    var card = FindCard(p.Hand, c.CardId); if (card == null) return "手牌不存在";
                    var d = s.Db.Card(card.Key);
                    if (d.Type == "OMEN") return "伏笔只能盖放";
                    if (c.Kind == CommandKind.Play && d.InterjectOnly) return "仅限抢拍使用";
                    if (c.Kind == CommandKind.Interject && (!d.Interject || !ResponseCondition(s, d))) return "不满足抢拍条件";
                    if (d.Type == "UNIT" && p.Board.Count >= s.Config.BoardMax) return "侍从区已满";
                    if (d.Type == "SIGIL" && p.Sigils.Count >= s.Config.SigilMax) return "刻印区已满";
                    if (d.Channel.HasValue && ObjectCount(s, c.Player, ObjectKind.Channel) >= s.Config.ChannelMax) return "咏唱位已满";
                    if (!TargetsLegal(s, c, d.Targets)) return "请选择合法目标";
                    if (d.Choice != null && Array.IndexOf(d.Choice, c.Choice) < 0) return "请选择预言类别";
                    break;
                case CommandKind.Attack:
                    var u = FindUnit(s, c.UnitId);
                    if (u == null || u.Card.Owner != c.Player || u.Exhausted || u.Stasis) return "侍从不能攻击";
                    if (!CanAttackTarget(s, c.Player, c.Target)) return "必须优先攻击守卫";
                    break;
                case CommandKind.Ability:
                    if (p.ChimesRung < p.AbilityReadyAt) return "时器尚在冷却";
                    if (!TargetsLegal(s, c, s.Db.Keeper(p.Keeper).Ability.Targets)) return "请选择合法目标";
                    break;
                case CommandKind.Focus: if (p.FocusUsed >= s.Config.FocusPerBar) return "本小节已蓄势"; break;
                case CommandKind.Omen:
                    if (FindCard(p.Hand, c.CardId) == null) return "手牌不存在";
                    int pos = p.Marker + s.Config.OmenSetCost + c.Offset;
                    if (c.Offset < s.Config.OmenOffsetMin || c.Offset > s.Config.OmenOffsetMax || pos <= s.Players[1 - c.Player].Marker || pos >= s.Config.TimelineEnd || ObjectCount(s, c.Player, ObjectKind.Omen) >= OmenCap(s, c.Player)) return "伏笔位置或数量不合法";
                    foreach (var o in s.Timeline) if (o.Kind == ObjectKind.Omen && o.Owner == c.Player && o.Pos == pos) return "该位置已有己方伏笔";
                    break;
                case CommandKind.Wait: break;
                default: return "无效行动";
            }
            if (c.Kind != CommandKind.Wait && Cost(s, c) > Horizon(s, c.Player)) return "最终时值超过视界";
            return null;
        }
        public static void LegalCommands(GameState s, int player, List<Command> buffer)
        {
            buffer.Clear(); if (s.Outcome != Outcome.Ongoing || player < 0 || player > 1) return;
            if (!s.MainPhase) { if (!s.Players[player].MulliganDone) buffer.Add(new Command { Player = player, Kind = CommandKind.Mulligan }); return; }
            if (NextActor(s) != player) return;
            if (s.Pending != null && s.Pending.Kind != PendingKind.Interject)
            {
                if (s.Pending.Kind == PendingKind.Summon) foreach (int id in s.Pending.Options) buffer.Add(new Command { Player = player, Kind = CommandKind.Choose, Selection = new[] { id } });
                else PermuteChoices(s.Pending.Options.ToArray(), 0, s.Pending.Kind, player, buffer);
                return;
            }
            bool responding = s.Pending != null;
            if (responding) buffer.Add(new Command { Player = player, Kind = CommandKind.Pass });
            else
            {
                AddIfLegal(s, new Command { Player = player, Kind = CommandKind.Wait }, buffer);
                AddIfLegal(s, new Command { Player = player, Kind = CommandKind.Focus }, buffer);
                AddTargets(s, new Command { Player = player, Kind = CommandKind.Ability }, s.Db.Keeper(s.Players[player].Keeper).Ability.Targets, null, buffer);
                foreach (var u in s.Players[player].Board)
                {
                    AddIfLegal(s, new Command { Player = player, Kind = CommandKind.Attack, UnitId = u.Id, Target = new Target(TargetKind.Keeper, 1 - player) }, buffer);
                    foreach (var enemy in s.Players[1 - player].Board) AddIfLegal(s, new Command { Player = player, Kind = CommandKind.Attack, UnitId = u.Id, Target = new Target(TargetKind.Unit, enemy.Id) }, buffer);
                }
            }
            foreach (var card in s.Players[player].Hand)
            {
                var d = s.Db.Card(card.Key);
                for (int sand = 0; sand <= s.Players[player].Sand; sand++) AddTargets(s, new Command { Player = player, CardId = card.Id, Sand = sand, Kind = responding ? CommandKind.Interject : CommandKind.Play }, d.Targets, d.Choice, buffer);
                if (!responding) for (int offset = s.Config.OmenOffsetMin; offset <= s.Config.OmenOffsetMax; offset++) AddIfLegal(s, new Command { Player = player, CardId = card.Id, Offset = offset, Kind = CommandKind.Omen }, buffer);
            }
        }
        static void PermuteChoices(int[] ids, int index, PendingKind kind, int player, List<Command> result)
        {
            if (index == ids.Length) { for (int top = kind == PendingKind.Scry ? 0 : 1; top <= (kind == PendingKind.Scry ? ids.Length : 1); top++) result.Add(new Command { Kind = CommandKind.Choose, Player = player, Selection = (int[])ids.Clone(), TopCount = top }); return; }
            for (int i = index; i < ids.Length; i++) { int t = ids[index]; ids[index] = ids[i]; ids[i] = t; PermuteChoices(ids, index + 1, kind, player, result); t = ids[index]; ids[index] = ids[i]; ids[i] = t; }
        }
        static void AddTargets(GameState s, Command template, List<TargetSpec> specs, string[] choices, List<Command> result)
        {
            var targets = new List<Target>();
            if (specs.Count == 0 || specs[0].Optional) targets.Add(default);
            if (specs.Count > 0)
            {
                for (int p = 0; p < 2; p++) { targets.Add(new Target(TargetKind.Keeper, p)); foreach (var u in s.Players[p].Board) targets.Add(new Target(TargetKind.Unit, u.Id)); }
                foreach (var o in s.Timeline) targets.Add(new Target(TargetKind.Object, o.Id));
            }
            foreach (var t in targets) for (int i = 0; i < (choices?.Length ?? 1); i++) { var c = template.Clone(); c.Target = t; c.Choice = choices == null ? null : choices[i]; AddIfLegal(s, c, result); }
        }
        static void AddIfLegal(GameState s, Command c, List<Command> result) { if (IllegalReason(s, c) == null) result.Add(c); }
        static void Emit(GameState s, string kind, string text, int privateTo = -1) => s.Events.Add(new GameEvent { Kind = kind, Text = text, PrivateTo = privateTo });
    }
}
