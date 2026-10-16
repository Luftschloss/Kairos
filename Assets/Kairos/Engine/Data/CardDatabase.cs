using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kairos.Engine
{
    [Flags] public enum Keyword { None = 0, Guard = 1, Swift = 2, Growth = 4 }
    public enum CommandKind { Play, Attack, Ability, Focus, Omen, Wait, Interject, Pass, Choose, Mulligan, Concede }
    public enum TargetKind { None, Unit, Keeper, Object }
    public enum Outcome { Ongoing, P0Win, P1Win, Draw }
    public enum PendingKind { Interject, Scry, Pick, Summon }
    public enum ObjectKind { Omen, Channel, Echo }
    public enum Op { DAMAGE, DAMAGE_ALL, HEAL, DRAW, SCRY, PICK_FROM_TOP, SUMMON_FROM_PAST, DESTROY, DESTROY_ALL, BUFF, BUFF_ALL, SHIELD, STASIS, STASIS_ALL, REFRESH, SACRIFICE, GAIN_SAND, PAY_LIFE, PUSH_MARKER, PUSH_OBJECT, PUSH_ALL_OBJECTS, NEXT_CARD_COST, PROPHECY, COUNTER_DECLARED, REDIRECT_ATTACK, COPY_LAST_PLAYED, GROW_ALL, IF }

    [Serializable] public sealed class RulesConfig
    {
        public int TimelineEnd = 64, BarLength = 4, LifeStart = 20, LifeMax = 20;
        public int DeckSize = 30, CopyLimit = 2, LegendaryLimit = 1, MaxSchools = 2;
        public int HandStart = 5, SecondPlayerBonusCards = 1, HandMax = 8, BoardMax = 5, SigilMax = 3;
        public int ChannelMax = 2, OmenMax = 3, OmenSetCost = 2, OmenOffsetMin = 1, OmenOffsetMax = 6;
        public int FocusCost = 2, FocusPerBar = 1, HorizonBase = 2, HorizonMax = 7, InterjectTax = 1, MinActionCost = 1;
        public int RewindMaxGap = 4, PushSingleMax = 3, PushPerBarMax = 4, SandMax = 10;
        public int RefluxThreshold = 10, RefluxRewind = 2, EmptyDeckDamage = 2, KairosLineMax = 60;
        public int[] DuskBellLines = { 48, 52, 56, 60 }, DuskBellDamage = { 1, 2, 3, 4 };
        public int InterjectTimeoutMs = 8000;
        public RulesConfig Clone() { var c = (RulesConfig)MemberwiseClone(); c.DuskBellLines = (int[])DuskBellLines.Clone(); c.DuskBellDamage = (int[])DuskBellDamage.Clone(); return c; }
        public void Validate()
        {
            Rules.Require(TimelineEnd > 0 && BarLength > 0 && LifeMax >= LifeStart && LifeStart > 0 && DeckSize > 0 && HandMax > 0 && BoardMax > 0 && MinActionCost >= 1 && HorizonBase >= 1 && HorizonMax >= HorizonBase && DuskBellLines.Length == DuskBellDamage.Length, "R-1", "无效规则配置");
        }
    }
    public sealed class RulesViolationException : Exception
    {
        public readonly string Rule;
        public RulesViolationException(string rule, string message) : base(rule + "：" + message) { Rule = rule; }
    }
    public static class Rules
    {
        public static void Require(bool value, string rule, string message) { if (!value) throw new RulesViolationException(rule, message); }
    }
    public struct Target : IEquatable<Target>
    {
        public TargetKind Kind;
        public int Id;
        public Target(TargetKind kind, int id) { Kind = kind; Id = id; }
        public bool Equals(Target other) => Kind == other.Kind && Id == other.Id;
        public override bool Equals(object obj) => obj is Target t && Equals(t);
        public override int GetHashCode() => ((int)Kind * 397) ^ Id;
    }
    public sealed class Command
    {
        public CommandKind Kind;
        public int Player, CardId, UnitId, Sand, Offset = 1, TopCount;
        public Target Target;
        public string Choice;
        public int[] Selection = Array.Empty<int>();
        public Command Clone() { var c = (Command)MemberwiseClone(); c.Selection = (int[])Selection.Clone(); return c; }
    }
    public sealed class Filter
    {
        public int? MinAtk, MaxBaseTime;
        public bool EnteredThisBar, NotAttacked, NotStasis, Exhausted, HasChannel;
        public string Keyword, Type;
    }
    public sealed class TargetSpec
    {
        public string Id, Selector;
        public bool Optional;
        public Filter Filter;
    }
    public sealed class Condition
    {
        public string Kind, Ref;
        public static Condition Parse(JToken token)
        {
            if (token == null) return null;
            if (token.Type == JTokenType.String) return new Condition { Kind = (string)token };
            var p = ((JObject)token).First as JProperty;
            return new Condition { Kind = p.Name, Ref = (string)p.Value };
        }
    }
    // Typed once on load; no JSON interpretation or reflection during Apply.
    public sealed class Effect
    {
        public Op Op;
        public string Target, Player, Selector, Duration, Predicted, Destination, To, From;
        public int Amount, Count, Pick, Atk, Hp, Delta, MinBaseTime, Max, Times, CostDelta;
        public bool FromSacrificed;
        public Filter Filter;
        public Condition Condition;
        public List<Effect> Then = new List<Effect>(), Else = new List<Effect>();
    }
    public sealed class Ability
    {
        public string Trigger, Limit;
        public int Length;
        public Condition Condition;
        public List<Effect> Effects = new List<Effect>();
    }
    public sealed class Aura
    {
        public string Kind, AppliesTo, Cond, Limit, Selector;
        public Filter Filter;
        public int Delta, Atk, Hp, Per;
    }
    public sealed class CardDefinition
    {
        public string Key, Name, School, Type, Rarity, Text, Flavor;
        public int Time, Atk, Hp, Cadence = 1;
        public Keyword Keywords;
        public bool Interject, InterjectOnly;
        public string InterjectCondition;
        public int? Channel, Echo;
        public List<TargetSpec> Targets = new List<TargetSpec>();
        public string[] Choice;
        public List<Ability> Abilities = new List<Ability>();
        public List<Aura> Static = new List<Aura>();
    }
    public sealed class KeeperAbility
    {
        public string Name;
        public int Cost, CooldownBars;
        public List<TargetSpec> Targets = new List<TargetSpec>();
        public List<Effect> Effects = new List<Effect>();
    }
    public sealed class KeeperDefinition
    {
        public string Key, Name, School, Text;
        public List<Ability> Abilities = new List<Ability>();
        public List<Aura> Static = new List<Aura>();
        public KeeperAbility Ability;
    }
    public sealed class DeckList
    {
        public string Key, Name, Keeper;
        public List<string> Cards = new List<string>();
        public DeckList Clone() { var d = (DeckList)MemberwiseClone(); d.Cards = new List<string>(Cards); return d; }
    }
    public sealed class CardDatabase
    {
        public string Version;
        public readonly List<CardDefinition> Cards = new List<CardDefinition>();
        public readonly List<KeeperDefinition> Keepers = new List<KeeperDefinition>();
        public readonly List<DeckList> Precons = new List<DeckList>();
        readonly Dictionary<string, CardDefinition> cards = new Dictionary<string, CardDefinition>();
        readonly Dictionary<string, KeeperDefinition> keepers = new Dictionary<string, KeeperDefinition>();
        public CardDefinition Card(string key) => cards[key];
        public KeeperDefinition Keeper(string key) => keepers[key];
        public bool HasCard(string key) => key != null && cards.ContainsKey(key);
        public static CardDatabase Load(string json)
        {
            var root = JObject.Parse(json);
            var db = new CardDatabase { Version = (string)root["version"] };
            foreach (JObject j in root["cards"])
            {
                var c = new CardDefinition {
                    Key = (string)j["key"], Name = (string)j["name"], School = (string)j["school"], Type = (string)j["type"], Rarity = (string)j["rarity"], Text = (string)j["text"], Flavor = (string)j["flavor"],
                    Time = (int?)j["time"] ?? 0, Atk = (int?)j["atk"] ?? 0, Hp = (int?)j["hp"] ?? 0, Cadence = (int?)j["cadence"] ?? 1,
                    Interject = (bool?)j["interject"] ?? false, InterjectOnly = (bool?)j["interjectOnly"] ?? false, InterjectCondition = (string)j["interjectCondition"], Channel = (int?)j["channel"], Echo = (int?)j["echo"],
                    Targets = j["targets"]?.ToObject<List<TargetSpec>>() ?? new List<TargetSpec>(), Choice = j["choice"]?.ToObject<string[]>(), Static = j["static"]?.ToObject<List<Aura>>() ?? new List<Aura>()
                };
                foreach (var k in j["keywords"] ?? new JArray()) c.Keywords |= (Keyword)Enum.Parse(typeof(Keyword), (string)k, true);
                c.Abilities = ParseAbilities(j["abilities"], c.Channel.HasValue || c.Echo.HasValue);
                if ((c.Keywords & Keyword.Growth) != 0) c.Abilities.Add(new Ability { Trigger = "ON_YOUR_CHIME", Effects = new List<Effect> { new Effect { Op = Op.BUFF, Target = "$self", Atk = 1, Hp = 1, Duration = "PERMANENT" } } });
                Rules.Require(!db.cards.ContainsKey(c.Key) && c.Time >= 0 && (c.Type != "UNIT" || c.Hp > 0 && c.Cadence > 0), "R-15", "卡牌数据无效：" + c.Key);
                db.Cards.Add(c); db.cards.Add(c.Key, c);
            }
            foreach (JObject j in root["keepers"])
            {
                var a = (JObject)j["ability"];
                var k = new KeeperDefinition { Key = (string)j["key"], Name = (string)j["name"], School = (string)j["school"], Text = (string)j["text"],
                    Static = j["passive"]?["static"]?.ToObject<List<Aura>>() ?? new List<Aura>(), Abilities = ParseAbilities(j["passive"]?["abilities"], true),
                    Ability = new KeeperAbility { Name = (string)a["name"], Cost = (int)a["cost"], CooldownBars = (int)a["cooldownBars"], Targets = a["targets"]?.ToObject<List<TargetSpec>>() ?? new List<TargetSpec>(), Effects = ParseEffects(a["effects"], false) } };
                db.Keepers.Add(k); db.keepers.Add(k.Key, k);
            }
            foreach (JObject j in root["precons"])
            {
                var d = new DeckList { Key = (string)j["key"], Name = (string)j["name"], Keeper = (string)j["keeper"] };
                // Sort keys: serialized object order is not a gameplay dependency.
                var keys = new List<string>(); foreach (var p in ((JObject)j["cards"]).Properties()) keys.Add(p.Name); keys.Sort(StringComparer.Ordinal);
                foreach (string key in keys) for (int i = 0; i < (int)j["cards"][key]; i++) d.Cards.Add(key);
                db.ValidateDeck(d, new RulesConfig()); db.Precons.Add(d);
            }
            return db;
        }
        static List<Ability> ParseAbilities(JToken data, bool passive)
        {
            var list = new List<Ability>();
            foreach (JObject j in data ?? new JArray()) list.Add(new Ability { Trigger = (string)j["trigger"], Limit = (string)j["limit"], Length = (int?)j["length"] ?? 0, Condition = Condition.Parse(j["cond"]), Effects = ParseEffects(j["effects"], passive || (string)j["trigger"] != "ON_PLAY") });
            return list;
        }
        static List<Effect> ParseEffects(JToken data, bool passive)
        {
            var list = new List<Effect>();
            foreach (JObject j in data ?? new JArray())
            {
                Rules.Require(Enum.TryParse((string)j["op"], out Op op), "R-15", "未知操作：" + j["op"]);
                Rules.Require(!passive || op != Op.SCRY && op != Op.PICK_FROM_TOP && op != Op.SUMMON_FROM_PAST, "R-9-4", "被动/咏唱不能等待玩家选择");
                var e = j.ToObject<Effect>(); e.Op = op; e.Condition = Condition.Parse(j["cond"]);
                e.Then = ParseEffects(j["then"], passive); e.Else = ParseEffects(j["else"], passive);
                list.Add(e);
            }
            return list;
        }
        public void ValidateDeck(DeckList d, RulesConfig c, bool campaign = false)
        {
            Rules.Require(keepers.ContainsKey(d.Keeper), "R-3-1", "未知钟主");
            Rules.Require(campaign ? d.Cards.Count >= 10 : d.Cards.Count == c.DeckSize, "R-3-1", "牌组张数错误");
            var schools = new List<string>(); var counts = new Dictionary<string, int>();
            foreach (var key in d.Cards)
            {
                Rules.Require(cards.ContainsKey(key), "R-3-1", "未知卡牌 " + key);
                var card = Card(key); if (card.School != "NEUTRAL" && !schools.Contains(card.School)) schools.Add(card.School);
                counts.TryGetValue(key, out int n); counts[key] = ++n;
                Rules.Require(campaign || n <= (card.Rarity == "LEGENDARY" ? c.LegendaryLimit : c.CopyLimit), "R-3-1", "同名牌超限");
            }
            Rules.Require(campaign || schools.Count <= c.MaxSchools && schools.Contains(Keeper(d.Keeper).School), "R-3-1", "学派不合法");
        }
    }
}
