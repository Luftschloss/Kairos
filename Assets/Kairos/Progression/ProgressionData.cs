using System;
using System.Collections.Generic;
using Kairos.Engine;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace Kairos.Progression
{
    public enum MatchMode { Collection, Trial, Hotseat, Tower }
    public sealed class PlayerProfile
    {
        public string Id = Guid.NewGuid().ToString("N");
        public int CraftShards;
        public Dictionary<string, int> CardCounts = new Dictionary<string, int>();
        public List<string> Keepers = new List<string>();
        public List<DeckList> Decks = new List<DeckList>();
        public Dictionary<string, int> Mastery = new Dictionary<string, int>();
        public Dictionary<string, int> Items = new Dictionary<string, int>();
        public HashSet<string> Entitlements = new HashSet<string>();
        public HashSet<string> Transactions = new HashSet<string>();
    }
    public sealed class SavedMatch
    {
        public string Id, RunId;
        public MatchMode Mode;
        public int Encounter, Viewer;
        public ReplayFile Replay;
        public string EndReason;
    }
    public sealed class SavedRun
    {
        public string Id = Guid.NewGuid().ToString("N");
        public CampaignRun Progress;
    }
    public sealed class Settlement
    {
        public string Id, MatchId, RunId, Keeper, Reason, Policy = "rewards-v1";
        public int Encounter, BaseShards, BonusShards, Mastery, BalanceAfter;
        public Outcome Outcome;
        public List<string> Bonuses = new List<string>();
        public long CreatedRevision;
        public bool Applied;
        public string Choice;
        public List<string> Candidates = new List<string>();
        public int Total => BaseShards + BonusShards;
    }
    public sealed class SaveEnvelope
    {
        public int SchemaVersion = 1;
        public long Revision;
        public string ContentVersion;
        public PlayerProfile Profile = new PlayerProfile();
        public SavedMatch ActiveMatch;
        public SavedRun ActiveRun;
        public List<Settlement> Settlements = new List<Settlement>();
        public SaveEnvelope Clone() => SaveJson.Read(SaveJson.Write(this));
    }
    public static class SaveJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None, MaxDepth = 128,
            ObjectCreationHandling = ObjectCreationHandling.Replace
        };
        public static string Write(SaveEnvelope data) => JsonConvert.SerializeObject(data, Formatting.Indented, Settings);
        public static SaveEnvelope Read(string json)
        {
            var token = JObject.Parse(json);
            foreach (string field in new[] { "SchemaVersion", "Revision", "ContentVersion", "Profile", "Settlements" })
                if (token[field] == null || token[field].Type == JTokenType.Null) throw new InvalidDataException("存档缺少字段：" + field);
            var profile = token["Profile"] as JObject;
            if (profile == null) throw new InvalidDataException("档案格式错误");
            foreach (string field in new[] { "Id", "CraftShards", "CardCounts", "Keepers", "Decks", "Mastery", "Items", "Entitlements", "Transactions" })
                if (profile[field] == null || profile[field].Type == JTokenType.Null) throw new InvalidDataException("档案缺少字段：" + field);
            return JsonConvert.DeserializeObject<SaveEnvelope>(json, Settings);
        }
    }
}
