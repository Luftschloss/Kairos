using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    public sealed class KairosRng
    {
        public uint A, B, C, D;
        public KairosRng(string seed)
        {
            uint h = 2166136261; foreach (char c in seed ?? "kairos") h = unchecked((h ^ c) * 16777619);
            A = Mix(ref h); B = Mix(ref h); C = Mix(ref h); D = Mix(ref h);
        }
        static uint Mix(ref uint x) { x = unchecked(x + 0x9e3779b9); uint z = x; z = unchecked((z ^ (z >> 16)) * 0x85ebca6b); z = unchecked((z ^ (z >> 13)) * 0xc2b2ae35); return z ^ (z >> 16); }
        static uint Rot(uint x, int k) => (x << k) | (x >> (32 - k));
        public uint Next() { uint result = unchecked(Rot(B * 5, 7) * 9), t = B << 9; C ^= A; D ^= B; B ^= C; A ^= D; C ^= t; D = Rot(D, 11); return result; }
        public int Range(int max) { if (max <= 0) throw new ArgumentOutOfRangeException(nameof(max)); uint bound = (uint)max, threshold = unchecked(0u - bound) % bound; uint r; do r = Next(); while (r < threshold); return (int)(r % bound); }
        public void Shuffle<T>(List<T> list) { for (int i = list.Count - 1; i > 0; i--) { int j = Range(i + 1); T t = list[i]; list[i] = list[j]; list[j] = t; } }
        public KairosRng Clone() => (KairosRng)MemberwiseClone();
    }
    public sealed class CardInstance
    {
        public int Id, Owner, CostDelta;
        public string Key;
        public CardInstance Clone() => (CardInstance)MemberwiseClone();
    }
    public sealed class Buff
    {
        public int Atk, Hp;
        public string Duration;
        public Buff Clone() => (Buff)MemberwiseClone();
    }
    public sealed class Unit
    {
        public int Id, Atk, Hp, MaxHp, Cadence, EnteredAtChime, AttacksSinceReady;
        public CardInstance Card;
        public Keyword Keywords;
        public bool Exhausted, Stasis, Shield;
        public List<Buff> Buffs = new List<Buff>();
        public Unit Clone() { var u = (Unit)MemberwiseClone(); u.Card = Card.Clone(); u.Buffs = new List<Buff>(); foreach (var b in Buffs) u.Buffs.Add(b.Clone()); return u; }
    }
    public sealed class CostStatus
    {
        public int Delta, MinBaseTime;
        public string Duration;
        public CostStatus Clone() => (CostStatus)MemberwiseClone();
    }
    public sealed class PlayerState
    {
        public string Keeper, LastPlayedCardKey, Prophecy;
        public int Id, Life, Marker, LastMoveSeq, ChimesRung, Sand, AbilityReadyAt, FocusUsed, PushReceived, ProphecyOwner, ProphecyDraw, ChainLength;
        public bool Finished, MulliganDone, RefluxUsed, RefluxPending, TaxFreeUsed, ChainDiscountUsed, AnyUnitDied;
        public List<string> UsedTriggers = new List<string>();
        public List<CardInstance> Hand = new List<CardInstance>(), Deck = new List<CardInstance>(), Past = new List<CardInstance>(), Sigils = new List<CardInstance>();
        public List<Unit> Board = new List<Unit>();
        public List<CostStatus> Statuses = new List<CostStatus>();
        public PlayerState Clone()
        {
            var p = (PlayerState)MemberwiseClone(); p.Hand = Copy(Hand); p.Deck = Copy(Deck); p.Past = Copy(Past); p.Sigils = Copy(Sigils);
            p.Board = new List<Unit>(); foreach (var u in Board) p.Board.Add(u.Clone());
            p.Statuses = new List<CostStatus>(); foreach (var x in Statuses) p.Statuses.Add(x.Clone()); p.UsedTriggers = new List<string>(UsedTriggers); return p;
        }
        static List<CardInstance> Copy(List<CardInstance> source) { var result = new List<CardInstance>(); foreach (var c in source) result.Add(c.Clone()); return result; }
    }
    public sealed class TimelineObject
    {
        public int Id, Owner, Pos, PlacedSeq;
        public ObjectKind Kind;
        public CardInstance Card;
        public EffectContext Context;
        public TimelineObject Clone() { var o = (TimelineObject)MemberwiseClone(); o.Card = Card.Clone(); o.Context = Context.Clone(); return o; }
    }
    public sealed class EffectContext
    {
        public int Player, Self, Entered, Triggerer = -1, SacrificedTime, Scale = 1;
        public Target Target;
        public string Choice;
        public bool Kairos, Chain, Interjecting;
        public EffectContext Clone() => (EffectContext)MemberwiseClone();
    }
    public sealed class EffectTask
    {
        public List<Effect> Steps;
        public int Index;
        public EffectContext Context;
        public EffectTask Clone() => new EffectTask { Steps = new List<Effect>(Steps), Index = Index, Context = Context.Clone() };
    }
    public sealed class InFlightAction
    {
        public Command Command;
        public CardInstance Card;
        public int Cost, StartMarker, Stage;
        public EffectContext Context;
        public bool Countered, Redirected;
        public InFlightAction Clone() => new InFlightAction { Command = Command.Clone(), Card = Card?.Clone(), Cost = Cost, StartMarker = StartMarker, Stage = Stage, Context = Context.Clone(), Countered = Countered, Redirected = Redirected };
    }
    public sealed class PendingDecision
    {
        public PendingKind Kind;
        public int Player;
        public List<int> Options = new List<int>();
        public PendingDecision Clone() => new PendingDecision { Kind = Kind, Player = Player, Options = new List<int>(Options) };
    }
    public sealed class GameEvent
    {
        public string Kind, Text;
        public int PrivateTo = -1;
        public GameEvent Clone() => (GameEvent)MemberwiseClone();
    }
    public sealed class GameState
    {
        public RulesConfig Config;
        public CardDatabase Db; // Immutable after loading; shared by clones.
        public string Seed;
        public KairosRng Rng;
        public PlayerState[] Players = new PlayerState[2];
        public int NextId = 1, MoveSeq = 1, LastDeclaredActor = -1;
        public Outcome Outcome;
        public PendingDecision Pending;
        public List<TimelineObject> Timeline = new List<TimelineObject>();
        public List<InFlightAction> InFlight = new List<InFlightAction>();
        public List<List<EffectTask>> Frames = new List<List<EffectTask>> { new List<EffectTask>() };
        public List<GameEvent> Events = new List<GameEvent>();
        public List<Command> Commands = new List<Command>();
        public bool MainPhase => Players[0].MulliganDone && Players[1].MulliganDone;
        public GameState Clone()
        {
            var s = (GameState)MemberwiseClone(); s.Config = Config.Clone(); s.Rng = Rng.Clone(); s.Players = new[] { Players[0].Clone(), Players[1].Clone() };
            s.Pending = Pending?.Clone(); s.Timeline = new List<TimelineObject>(); foreach (var o in Timeline) s.Timeline.Add(o.Clone());
            s.InFlight = new List<InFlightAction>(); foreach (var f in InFlight) s.InFlight.Add(f.Clone());
            s.Frames = new List<List<EffectTask>>(); foreach (var frame in Frames) { var copy = new List<EffectTask>(); foreach (var t in frame) copy.Add(t.Clone()); s.Frames.Add(copy); }
            s.Events = new List<GameEvent>(); foreach (var e in Events) s.Events.Add(e.Clone()); s.Commands = new List<Command>(); foreach (var c in Commands) s.Commands.Add(c.Clone()); return s;
        }
    }
}
