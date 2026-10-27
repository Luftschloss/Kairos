using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kairos.Engine;
using Kairos.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        ProgressionService progression;
        string profileNotice;
        Action retrySave;
        DeckList chosenDeck, opponentDeck;
        bool trialMatch;
        string SavePath => Path.Combine(Application.persistentDataPath, "Profiles", "local-v1.json");
        void LoadProgression()
        {
            var store = new FileSaveStore(SavePath);
            progression = new ProgressionService(Database, store, PlayerPrefs.GetInt(TutorialPreference, 0) == 1);
            profileNotice = store.Notice;
        }
        void MetaTry(Action action)
        {
            try { action(); }
            catch (Exception ex) { ShowMetaError(ex.Message, action); }
        }
        void ShowMetaError(string reason, Action retry)
        {
            retrySave = retry;
            var box = Modal("SaveError", "操作未完成 · 数据不会部分扣除");
            Label(box, new Rect(40, 120, 1050, 235), reason + "\n\n已确认的数据保留。写入成功前不会显示已入账。", 23, gold);
            Button(box, new Rect(40, 430, 495, 65), "重试", () => { retrySave = null; MetaTry(retry); }, true, panel, 24);
            Button(box, new Rect(590, 430, 510, 65), "返回菜单 / 保留存档", () => { retrySave = null; ShowMenu(); }, true, panel, 24); FinishPage();
        }
        void InitFailure(Exception ex)
        {
            BuildCanvas(); Clear(); BeginPage("LoadFailure"); Header("档案加载失败");
            Label(root, new Rect(160, 220, 1280, 260), ex.Message + "\n\n原文件没有被重置。存档位置：\n" + SavePath, 24, gold);
            Button(root, new Rect(510, 570, 580, 80), "重新读取档案", () => { try { LoadProgression(); ShowMenu(); } catch (Exception e) { InitFailure(e); } }); FinishPage();
        }
        void SyncRun() { Run = progression?.Data.ActiveRun?.Progress; }
        void EnterSavedMatch(bool resume)
        {
            var saved = progression.Data.ActiveMatch;
            State = progression.RestoreMatch(); SyncRun(); lesson = null; tutorialIntro = false;
            Mode = saved.Mode == MatchMode.Hotseat ? DemoMode.Hotseat : saved.Mode == MatchMode.Tower ? DemoMode.Tower : DemoMode.Practice;
            trialMatch = saved.Mode == MatchMode.Trial; viewer = Mode == DemoMode.Hotseat ? saved.Viewer : 0;
            matchDecks = saved.Replay.Decks; matchSeed = saved.Replay.Seed; matchConfig = saved.Replay.Config;
            matchStartLife = saved.Replay.InitialLife == null ? matchConfig.LifeStart : saved.Replay.InitialLife[0];
            enemyStartLife = saved.Replay.InitialLife == null ? matchConfig.LifeStart : saved.Replay.InitialLife[1];
            BeginMatch();
            if (resume && Mode == DemoMode.Hotseat && State.Outcome == Outcome.Ongoing) { PrivacyCurtain = true; RebuildBattle(); }
        }
        void ResumeProgress() => MetaTry(() =>
        {
            progression.RecoverSettlement();
            if (progression.Data.ActiveMatch != null) EnterSavedMatch(true);
            else { SyncRun(); State = null; Mode = DemoMode.Tower; ShowCampaign(); }
        });
        void ChooseMatch(MatchMode mode)
        {
            if (mode == MatchMode.Collection || mode == MatchMode.Trial) trialMatch = mode == MatchMode.Trial;
            StartMatch(mode == MatchMode.Hotseat ? DemoMode.Hotseat : DemoMode.Practice);
        }
        void ConfirmAbandon()
        {
            var box = Modal("Abandon", "确认放弃保存的进度？");
            Label(box, new Rect(40, 140, 1060, 185), "未完成的本场不产生收益。\n本次旅程牌组将结束；已经入账的收藏与铸片不会丢失。\n只是想暂时离开？返回菜单即可，下次可以继续。", 25, ink);
            Button(box, new Rect(40, 440, 500, 65), "保留并返回", ShowMenu);
            Button(box, new Rect(600, 440, 500, 65), "确认放弃", () => MetaTry(() => { progression.AbandonActive(); State = null; Run = null; ShowMenu(); })); FinishPage();
        }
        void CloseResultTo(Action next) => MetaTry(() =>
        { progression.CloseResult(); State = null; SyncRun(); next(); });
        void ReplayMatch()
        {
            var saved = progression.Data.ActiveMatch;
            chosenDeck = saved.Replay.Decks[0].Clone(); opponentDeck = saved.Replay.Decks[1].Clone(); trialMatch = saved.Mode == MatchMode.Trial;
            var mode = Mode; CloseResultTo(() => StartMatch(mode));
        }
        void ChooseJourneyReward(string key)
        {
            string runId = progression.Data.ActiveRun.Id; int encounter = Run.Encounter;
            MetaTry(() => { progression.ChooseRunReward(key, runId, encounter); SyncRun(); ShowCampaign(); });
        }
        void RestJourney(int remove = -1)
        {
            string runId = progression.Data.ActiveRun.Id; int encounter = Run.Encounter;
            MetaTry(() => { progression.RestRun(remove, runId, encounter); SyncRun(); ShowCampaign(); });
        }
        void RestartJourney() => MetaTry(() => { progression.FinishRun(); StartTower(); });
        string ReceiptText()
        {
            var r = progression.CurrentReceipt;
            if (r == null) return "结算待保存";
            string bonuses = string.Join("、", r.Bonuses.Select(x => x == "collector-first-result-v1" ? "首次完成对局" : "短流程首通"));
            return r.Reason + "\n永久铸片 +" + r.BaseShards + (r.BonusShards > 0 ? "；里程碑 +" + r.BonusShards + "（" + bonuses + "）" : "")
                + "\n熟练度 +" + r.Mastery + " · " + (r.Applied ? "已入账" : "尚未入账") + " · 当前铸片 " + progression.Data.Profile.CraftShards;
        }
        void DrawMetaHome()
        {
            var p = progression.Data.Profile; chosenDeck = chosenDeck ?? p.Decks.FirstOrDefault()?.Clone() ?? Database.Precons[0].Clone();
            opponentDeck = opponentDeck ?? Database.Precons[SecondDeck].Clone();
            Header("收藏构筑 / 本地档案 · 铸片 " + p.CraftShards);
            Label(root, new Rect(60, 96, 950, 70), "下一种可能，由你来构筑。", 38, gold);
            Label(root, new Rect(64, 172, 1440, 54), "永久收藏 " + p.CardCounts.Count(x => x.Value > 0) + " / " + Database.Cards.Count + " 种  ·  钟主 " + p.Keepers.Count + " / " + Database.Keepers.Count + "\n" + (profileNotice ?? "没有卡牌等级、体力和断签惩罚。正式美术可后续替换。"), 19, muted);
            Button(root, new Rect(64, 247, 470, 66), "我的收藏 / 牌组工坊", () => OpenCollection(), true, panel, 24);
            Button(root, new Rect(554, 247, 470, 66), "钟主档案 / 背包", ShowProfile, true, panel, 24);
            Button(root, new Rect(1044, 247, 470, 66), "规则手册 / 新手教学", () => OpenRules(), true, panel, 24);
            Button(root, new Rect(64, 345, 704, 75), "P1：" + chosenDeck.Name + "\n" + Database.Keeper(chosenDeck.Keeper).Name + " · " + chosenDeck.Cards.Count + " 张", () => ShowDeckPicker(false), true, panel, 21);
            Button(root, new Rect(790, 345, 724, 75), "P2 / AI：" + opponentDeck.Name + "\n" + Database.Keeper(opponentDeck.Keeper).Name, () => ShowDeckPicker(true), true, panel, 21);
            Label(root, new Rect(64, 440, 1350, 32), progression.DeckReason(chosenDeck) ?? "自有收藏牌组合法 · 可获得成长收益", 18, progression.DeckReason(chosenDeck) == null ? teal : gold);
            Label(root, new Rect(64, 489, 140, 32), "对局种子", 20, muted);
            TextInput(root, new Rect(205, 481, 550, 48), Seed, value => Seed = string.IsNullOrWhiteSpace(value) ? "kairos" : value);
            Button(root, new Rect(790, 481, 350, 48), "教学（首次 +40 铸片）", StartTutorial, true, background, 19);
            Button(root, new Rect(1154, 481, 360, 48), "塔起始：" + Database.Precons[FirstDeck].Name, () => { FirstDeck = 1 - FirstDeck; ShowMenu(); }, true, background, 17);
            bool free = progression.Data.ActiveMatch == null && progression.Data.ActiveRun == null;
            Button(root, new Rect(64, 552, 346, 86), "收藏 AI 对战\n使用拥有的卡 · 有收益", () => RequestMode(() => ChooseMatch(MatchMode.Collection)), free && progression.DeckReason(chosenDeck) == null, panel, 22);
            Button(root, new Rect(432, 552, 346, 86), "全卡试用\n46 牌 / 10 钟主 · 无收益", () => RequestMode(() => ChooseMatch(MatchMode.Trial)), free && progression.DeckReason(chosenDeck, false) == null, panel, 22);
            Button(root, new Rect(800, 552, 346, 86), "本地热座 PVP\n双方试用 · 无收益", () => RequestMode(() => ChooseMatch(MatchMode.Hotseat)), free && progression.DeckReason(chosenDeck, false) == null && progression.DeckReason(opponentDeck, false) == null, panel, 22);
            Button(root, new Rect(1168, 552, 346, 86), "逆流之塔\n单旅程牌组 · 永久铸片", () => RequestMode(StartTower), free, panel, 22);
            if (!free)
            {
                Button(root, new Rect(64, 668, 1090, 66), progression.Data.ActiveMatch != null ? "继续保存的对局 / 结算" : "继续保存的旅程", ResumeProgress, true, new Color32(30, 88, 85, 255), 25);
                Button(root, new Rect(1174, 668, 340, 66), "放弃保存进度…", ConfirmAbandon, true, background, 21);
            }
            else Label(root, new Rect(64, 678, 1440, 70), "开局与每次行动后自动保存。返回菜单、关闭应用，不丢已确认的进度。\n下一步：收藏选卡 → 指定合成 → 保存配方。PvE 战利品仅本次旅程有效。", 22, muted);
            Label(root, new Rect(64, 780, 1440, 65), "本地档案，不与线上经济互通。存档：" + SavePath, 15, muted);
        }
        void ShowDeckPicker(bool opponent)
        {
            Clear(); BeginPage("DeckPicker"); Header(opponent ? "选择对手牌组 / 可试用" : "选择你的牌组");
            var decks = progression.Data.Profile.Decks;
            for (int i = 0; i < decks.Count; i++)
            {
                var d = decks[i]; Button(root, new Rect(60 + i % 3 * 496, 105 + i / 3 * 160, 474, 140), d.Name + "\n" + Database.Keeper(d.Keeper).Name + " · " + d.Cards.Count + " 张\n" + (progression.DeckReason(d, !opponent) ?? "可用"), () => { if (opponent) opponentDeck = d.Clone(); else chosenDeck = d.Clone(); ShowMenu(); }, true, panel, 19);
            }
            Button(root, new Rect(60, 790, 480, 60), "返回菜单", ShowMenu);
            Button(root, new Rect(1060, 790, 480, 60), "进入工坊新建 / 编辑", () => OpenCollection()); FinishPage();
        }
    }
}
