using System;
using System.IO;
using System.Linq;
using System.Text;

namespace Kairos.Progression
{
    public interface ISaveStore
    {
        SaveEnvelope Load();
        void Write(SaveEnvelope data);
    }
    public sealed class FileSaveStore : ISaveStore
    {
        readonly string path;
        bool recovered;
        public string Notice { get; private set; }
        public FileSaveStore(string path) { this.path = path; }
        public SaveEnvelope Load()
        {
            if (!File.Exists(path) && !File.Exists(path + ".bak")) return null;
            try { return ReadFile(path); }
            catch (Exception first)
            {
                try { var value = ReadFile(path + ".bak"); recovered = true; Notice = "主存档不可读，已恢复最近有效备份。"; return value; }
                catch (Exception) { throw new IOException("存档及备份无法读取；未覆盖文件。请保留文件并重试。", first); }
            }
        }
        static SaveEnvelope ReadFile(string file)
        { var data = SaveJson.Read(File.ReadAllText(file, Encoding.UTF8)); Validate(data); return data; }
        public void Write(SaveEnvelope data)
        {
            Validate(data);
            string directory = Path.GetDirectoryName(path); if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            byte[] bytes = Encoding.UTF8.GetBytes(SaveJson.Write(data));
            using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
            { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
            ReadFile(temp);
            if (File.Exists(path)) File.Replace(temp, path, recovered ? null : path + ".bak");
            else File.Move(temp, path);
            recovered = false;
        }
        public static void Validate(SaveEnvelope s)
        {
            if (s == null || s.SchemaVersion != 1 || s.Revision < 0 || s.Profile == null || string.IsNullOrEmpty(s.Profile.Id)) throw new InvalidDataException("不支持的存档版本或档案");
            var p = s.Profile;
            if (p.CardCounts == null || p.Keepers == null || p.Decks == null || p.Mastery == null || p.Items == null || p.Entitlements == null || p.Transactions == null || s.Settlements == null) throw new InvalidDataException("存档字段缺失");
            if (p.CraftShards < 0 || p.CardCounts.Any(x => string.IsNullOrEmpty(x.Key) || x.Value < 0) || p.Mastery.Values.Any(x => x < 0) || p.Items.Values.Any(x => x < 0) || p.Decks.Count > 12) throw new InvalidDataException("存档数量不合法");
            if (p.Decks.Any(d => d == null || string.IsNullOrEmpty(d.Key) || d.Cards == null) || p.Decks.Select(d => d.Key).Distinct().Count() != p.Decks.Count) throw new InvalidDataException("牌组记录不合法");
            if (s.Settlements.Any(r => r == null || string.IsNullOrEmpty(r.Id) || r.BaseShards < 0 || r.BonusShards < 0 || r.Mastery < 0 || r.Candidates == null || r.Bonuses == null) || s.Settlements.Select(r => r.Id).Distinct().Count() != s.Settlements.Count) throw new InvalidDataException("结算记录不合法");
            if (s.ActiveMatch != null && (string.IsNullOrEmpty(s.ActiveMatch.Id) || s.ActiveMatch.Replay == null || s.ActiveMatch.Replay.Commands == null || s.ActiveMatch.Replay.Decks == null || s.ActiveMatch.Replay.Decks.Length != 2)) throw new InvalidDataException("对局存档不完整");
            if (s.ActiveRun != null && (string.IsNullOrEmpty(s.ActiveRun.Id) || s.ActiveRun.Progress == null || s.ActiveRun.Progress.Deck == null || s.ActiveRun.Progress.Encounter < 0 || s.ActiveRun.Progress.Encounter > 2)) throw new InvalidDataException("旅程存档不完整");
        }
    }
}
