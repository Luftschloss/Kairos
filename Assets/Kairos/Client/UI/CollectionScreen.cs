using System;
using System.Collections.Generic;
using System.Linq;
using Kairos.Engine;
using Kairos.Progression;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        DeckList workshop;
        bool workshopDirty;
        string collectionCard, search = "", schoolFilter = "ALL", typeFilter = "ALL", rarityFilter = "ALL", keywordFilter = "ALL";
        int costFilter = -1, ownedFilter, cardPage, deckPage;
        bool compatibleOnly;
        void TextInput(Transform parent, Rect rect, string value, Action<string> changed, int limit = 80)
        {
            var rt = ui.Resolve(parent, "Input", "Input", rect, out bool created); var input = rt.GetComponent<InputField>();
            if (input == null) input = rt.gameObject.AddComponent<InputField>();
            if (created) { var image = rt.gameObject.AddComponent<Image>(); image.color = background; }
            input.textComponent = Label(rt, new Rect(10, 7, rect.width - 20, rect.height - 12), value, 18);
            input.textComponent.supportRichText = false; input.characterLimit = limit; input.SetTextWithoutNotify(value);
            input.onValueChanged.RemoveAllListeners(); input.onValueChanged.AddListener(v => changed(v));
        }
        static string SchoolName(string key) => key == "DAWN" ? "晨" : key == "NOON" ? "昼" : key == "DUSK" ? "暮" : key == "NIGHT" ? "夜" : key == "ETERNAL" ? "永" : key == "NEUTRAL" ? "中立" : "全部";
        static string RarityName(string key) => key == "COMMON" ? "普通" : key == "RARE" ? "稀有" : key == "EPIC" ? "史诗" : key == "LEGENDARY" ? "传说" : "全部";
        static string Cycle(string current, string[] values) => values[(Array.IndexOf(values, current) + 1) % values.Length];
        void OpenCollection()
        {
            State = null; aiCommand = null; retrySave = null;
            if (workshop == null) workshop = (chosenDeck ?? progression.Data.Profile.Decks.FirstOrDefault() ?? Database.Precons[0]).Clone();
            ShowCollection();
        }
        void LeaveWorkshop(Action action)
        {
            if (!workshopDirty) { action(); return; }
            var box = Modal("UnsavedWorkshop", "保留未保存的牌组修改？");
            Label(box, new Rect(40, 150, 1040, 150), "可保存为草稿，缺卡或不足 30 张也不会丢失。\n丢弃只影响当前配方修改，不影响收藏与已完成的合成。", 25, ink);
            Button(box, new Rect(40, 450, 330, 60), "保存后继续", () => MetaTry(() => { progression.SaveDeck(workshop); workshopDirty = false; action(); }));
            Button(box, new Rect(405, 450, 330, 60), "丢弃修改", () => { workshop = progression.Data.Profile.Decks.Find(d => d.Key == workshop.Key)?.Clone() ?? (chosenDeck ?? Database.Precons[0]).Clone(); workshopDirty = false; action(); });
            Button(box, new Rect(770, 450, 330, 60), "继续编辑", ShowCollection); FinishPage();
        }
        bool Compatible(CardDefinition d)
        {
            if (d.School == "NEUTRAL") return true;
            var schools = workshop.Cards.Select(k => Database.Cards.Find(x => x.Key == k)?.School).Where(x => x != null && x != "NEUTRAL").Distinct().ToList();
            if (!schools.Contains(Database.Keeper(workshop.Keeper).School)) schools.Add(Database.Keeper(workshop.Keeper).School);
            return schools.Contains(d.School) || schools.Count < 2;
        }
        List<CardDefinition> FilteredCards()
        {
            return Database.Cards.Where(d => (schoolFilter == "ALL" || d.School == schoolFilter) && (typeFilter == "ALL" || d.Type == typeFilter)
                && (rarityFilter == "ALL" || d.Rarity == rarityFilter) && (costFilter < 0 || d.Time == costFilter)
                && (ownedFilter == 0 || (ProgressionService.Owned(progression.Data.Profile, d.Key) > 0) == (ownedFilter == 1))
                && (!compatibleOnly || Compatible(d)) && (keywordFilter == "ALL" || d.Text.Contains(keywordFilter))
                && (string.IsNullOrEmpty(search) || (d.Name + " " + d.Text).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
        }
        void ShowCollection()
        {
            if (workshop == null) workshop = (chosenDeck ?? Database.Precons[0]).Clone();
            Clear(); BeginPage("Collection"); Header("我的收藏 / 工坊 · 铸片 " + progression.Data.Profile.CraftShards + " · " + (workshopDirty ? "配方未保存" : "配方已保存"));
            Button(root, new Rect(1340, 18, 210, 38), "返回菜单", () => LeaveWorkshop(ShowMenu), true, panel, 18);
            var filterPanel = ui.Subpanel(root, "CollectionFilters", new Rect(32, 91, 852, 92));
            var gridPanel = ui.Subpanel(root, "CollectionGrid", new Rect(32, 199, 852, 492));
            var footerPanel = ui.Subpanel(root, "CollectionFooter", new Rect(32, 715, 852, 145));
            TextInput(filterPanel, new Rect(0, 0, 218, 43), search, v => search = v);
            Button(filterPanel, new Rect(226, 0, 74, 43), "搜索", () => { cardPage = 0; ShowCollection(); }, true, panel, 17);
            var schools = new[] { "ALL", "NEUTRAL", "DAWN", "NOON", "DUSK", "NIGHT", "ETERNAL" };
            FilterDropdown(filterPanel, new Rect(312, 0, 172, 43), "SchoolFilter", "学派：", schools.Select(SchoolName).ToArray(), Array.IndexOf(schools, schoolFilter), i => schoolFilter = schools[i]);
            var types = new[] { "ALL", "UNIT", "SPELL", "SIGIL", "OMEN" };
            FilterDropdown(filterPanel, new Rect(496, 0, 172, 43), "TypeFilter", "类型：", types.Select(t => t == "ALL" ? "全部" : KindName(t)).ToArray(), Array.IndexOf(types, typeFilter), i => typeFilter = types[i]);
            var rarities = new[] { "ALL", "COMMON", "RARE", "EPIC", "LEGENDARY" };
            FilterDropdown(filterPanel, new Rect(680, 0, 172, 43), "RarityFilter", "稀有度：", rarities.Select(RarityName).ToArray(), Array.IndexOf(rarities, rarityFilter), i => rarityFilter = rarities[i]);
            FilterDropdown(filterPanel, new Rect(0, 54, 172, 38), "CostFilter", "时值：", new[] { "全部", "0刻", "1刻", "2刻", "3刻", "4刻", "5刻", "6刻", "7刻" }, costFilter + 1, i => costFilter = i - 1);
            FilterDropdown(filterPanel, new Rect(184, 54, 172, 38), "OwnedFilter", "拥有：", new[] { "全部", "已拥有", "未拥有" }, ownedFilter, i => ownedFilter = i);
            var keywords = new[] { "ALL", "抢拍", "咏唱", "契机", "连拍", "守卫", "迅捷", "预见", "时砂", "停滞" };
            FilterDropdown(filterPanel, new Rect(368, 54, 252, 38), "KeywordFilter", "关键词：", keywords.Select(k => k == "ALL" ? "全部" : k).ToArray(), Array.IndexOf(keywords, keywordFilter), i => keywordFilter = keywords[i]);
            FilterDropdown(filterPanel, new Rect(632, 54, 220, 38), "CompatibilityFilter", "学派兼容：", new[] { "不限", "仅当前可用" }, compatibleOnly ? 1 : 0, i => compatibleOnly = i == 1);
            var cards = FilteredCards(); cardPage = Math.Max(0, Math.Min(cardPage, Math.Max(0, (cards.Count - 1) / 9)));
            for (int i = cardPage * 9; i < Math.Min(cards.Count, cardPage * 9 + 9); i++)
            {
                var d = cards[i]; int slot = i % 9; int n = ProgressionService.Owned(progression.Data.Profile, d.Key);
                var visual = CardWidget(gridPanel, "CollectionTile", new Rect(slot % 3 * 288, slot / 3 * 168, 276, 156));
                BindCard(visual, d, "拥有 " + n + "/" + ProgressionService.Limit(d), n > 0, d.Key == collectionCard, SchoolName(d.School) + " · " + RarityName(d.Rarity));
                visual.Interaction.onClick.AddListener(() => { collectionCard = d.Key; ShowCollection(); });
            }
            Button(footerPanel, new Rect(0, 0, 170, 40), "上一页", () => { cardPage--; ShowCollection(); }, cardPage > 0);
            Label(footerPanel, new Rect(188, 7, 450, 32), "搜索名字 / 规则 · " + cards.Count + " 种 · 页 " + (cardPage + 1), 17, muted);
            Button(footerPanel, new Rect(680, 0, 172, 40), "下一页", () => { cardPage++; ShowCollection(); }, (cardPage + 1) * 9 < cards.Count);
            var detail = Database.Cards.Find(d => d.Key == collectionCard) ?? cards.FirstOrDefault();
            if (detail != null)
            {
                collectionCard = detail.Key;
                Button(footerPanel, new Rect(0, 53, 600, 92), detail.Name + (detail.Type == "UNIT" ? " · " + detail.Atk + "攻 / " + detail.Hp + "血 / 节奏" + detail.Cadence : "") + "\n查看完整效果、文案与使用牌组", () => ShowCardDetails(detail), true, panel, 21);
                Button(footerPanel, new Rect(619, 51, 233, 43), "加入配方 +1", () => { workshop.Cards.Add(detail.Key); workshopDirty = true; ShowCollection(); }, workshop.Cards.Count < 60 && workshop.Cards.Count(x => x == detail.Key) < ProgressionService.Limit(detail), panel, 17);
                Button(footerPanel, new Rect(619, 102, 233, 43), "合成 1 张 · " + ProgressionService.CraftCost(detail), () => ConfirmCraft(new Dictionary<string, int> { { detail.Key, 1 } }), ProgressionService.Owned(progression.Data.Profile, detail.Key) < ProgressionService.Limit(detail), panel, 17);
            }
            DrawWorkshop(); FinishPage();
        }
        void DrawWorkshop()
        {
            var side = ui.Subpanel(root, "Workshop", new Rect(910, 91, 658, 785));
            side.GetComponent<Image>().color = panel;
            TextInput(side, new Rect(16, 12, 400, 43), workshop.Name, v => { workshop.Name = v; workshopDirty = true; }, 32);
            Button(side, new Rect(430, 12, 210, 43), "切换 / 新建牌组", ShowWorkshopSlots, true, background, 17);
            Button(side, new Rect(16, 65, 624, 46), Database.Keeper(workshop.Keeper).Name + " · 点击更换钟主", ShowKeeperPicker, true, background, 20);
            Label(side, new Rect(16, 121, 624, 56), "配方 " + workshop.Cards.Count + "/30 张 · " + (progression.DeckReason(workshop) ?? "收藏牌组合法") + "\n缺卡草稿可保存；试用无需拥有，但仍需符合构筑规则。", 16, gold);
            var groups = workshop.Cards.GroupBy(x => x).ToList(); deckPage = Math.Max(0, Math.Min(deckPage, Math.Max(0, (groups.Count - 1) / 10)));
            for (int i = deckPage * 10; i < Math.Min(groups.Count, deckPage * 10 + 10); i++)
            {
                var group = groups[i]; var d = Database.Cards.Find(x => x.Key == group.Key); string name = d == null ? "未知卡 " + group.Key : d.Name;
                Button(side, new Rect(16, 188 + i % 10 * 35, 548, 31), group.Count() + " × " + name + "  / 拥有 " + ProgressionService.Owned(progression.Data.Profile, group.Key), () => { collectionCard = group.Key; ShowCollection(); }, true, background, 16);
                Button(side, new Rect(574, 188 + i % 10 * 35, 66, 31), "−", () => { workshop.Cards.Remove(group.Key); workshopDirty = true; ShowCollection(); }, true, background, 19);
            }
            Button(side, new Rect(16, 543, 170, 33), "前页", () => { deckPage--; ShowCollection(); }, deckPage > 0, background, 16);
            Button(side, new Rect(470, 543, 170, 33), "后页", () => { deckPage++; ShowCollection(); }, (deckPage + 1) * 10 < groups.Count, background, 16);
            var defs = workshop.Cards.Select(k => Database.Cards.Find(x => x.Key == k)).Where(x => x != null).ToList();
            string curve = string.Join("  ", Enumerable.Range(1, 7).Select(i => i + "刻:" + defs.Count(d => d.Time == i)));
            string stats = "侍从 " + defs.Count(d => d.Type == "UNIT") + " / 术式 " + defs.Count(d => d.Type == "SPELL") + " / 刻印 " + defs.Count(d => d.Type == "SIGIL") + " / 伏笔 " + defs.Count(d => d.Type == "OMEN") + " / 抢拍 " + defs.Count(d => d.Interject);
            Label(side, new Rect(16, 586, 624, 70), curve + "\n" + stats, 15, muted);
            Button(side, new Rect(16, 661, 300, 46), "保存草稿 / 配方", () => MetaTry(() => { progression.SaveDeck(workshop); chosenDeck = workshop.Clone(); workshopDirty = false; ShowCollection(); }), true, background, 20);
            Button(side, new Rect(332, 661, 308, 46), "一键补齐缺卡…", () => MetaTry(() => ConfirmCraft(progression.Missing(workshop))), true, background, 20);
            Button(side, new Rect(16, 720, 300, 46), "用于下次对局", () => LeaveWorkshop(() => { chosenDeck = workshop.Clone(); ShowMenu(); }), true, background, 20);
            Button(side, new Rect(332, 720, 308, 46), "试用此配方（无收益）", () => LeaveWorkshop(() => { chosenDeck = workshop?.Clone() ?? chosenDeck; ChooseMatch(MatchMode.Trial); }), progression.DeckReason(workshop, false) == null && progression.Data.ActiveMatch == null && progression.Data.ActiveRun == null, background, 18);
        }
        void ConfirmCraft(Dictionary<string, int> missing)
        {
            int price = progression.Quote(missing); string transaction = Guid.NewGuid().ToString("N");
            var box = Modal("Craft", "指定合成 · 永久收藏");
            var lines = missing.Select(x => Database.Card(x.Key).Name + " ×" + x.Value + "：" + ProgressionService.CraftCost(Database.Card(x.Key)) * x.Value + " 铸片").ToArray();
            ScrollText(box, new Rect(40, 115, 1040, 240), lines.Length == 0 ? "当前配方没有缺卡。" : string.Join("\n", lines));
            Label(box, new Rect(40, 369, 1040, 68), "共 " + price + " 铸片 · 余额 " + progression.Data.Profile.CraftShards + " → " + (progression.Data.Profile.CraftShards - price) + "\n全部成功才扣款；缺卡不等于强化等级。", 23, gold);
            Button(box, new Rect(40, 480, 500, 65), "取消", ShowCollection);
            Button(box, new Rect(600, 480, 500, 65), "确认合成", () => MetaTry(() => { progression.Craft(missing, transaction); ShowCollection(); }), missing.Count > 0 && progression.Data.Profile.CraftShards >= price); FinishPage();
        }
        void ScrollText(Transform parent, Rect rect, string text)
        {
            var area = Box(parent, rect, background); var scroll = area.GetComponent<ScrollRect>() ?? area.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false;
            var vp = Box(area, new Rect(0, 0, rect.width, rect.height), background); if (vp.GetComponent<RectMask2D>() == null) vp.gameObject.AddComponent<RectMask2D>();
            var label = Label(vp, new Rect(12, 8, rect.width - 24, rect.height), text, 22, ink);
            float height = label.cachedTextGeneratorForLayout.GetPreferredHeight(text, label.GetGenerationSettings(new Vector2(label.rectTransform.rect.width, 0))) / label.pixelsPerUnit;
            if (uiCaptured || label.rectTransform.GetComponent<UiElementSlot>().dynamicLayout) label.rectTransform.sizeDelta = new Vector2(label.rectTransform.rect.width, Mathf.Max(rect.height, height + 16));
            scroll.viewport = vp; scroll.content = label.rectTransform; scroll.movementType = ScrollRect.MovementType.Clamped;
        }
        void ShowCardDetails(CardDefinition d)
        {
            var box = Modal("CardDetails", d.Name + " · " + d.Time + "刻 / " + RarityName(d.Rarity));
            string uses = string.Join("、", progression.Data.Profile.Decks.Where(deck => deck.Cards.Contains(d.Key)).Select(deck => deck.Name));
            var face = CardWidget(box, "CardFace", new Rect(40, 100, 300, 400)); BindCard(face, d); face.Interaction.interactable = false;
            ScrollText(box, new Rect(375, 110, 720, 350), SchoolName(d.School) + " / " + KindName(d.Type) + (d.Type == "UNIT" ? " / " + d.Atk + "攻 " + d.Hp + "血 节奏" + d.Cadence : "") + "\n\n" + d.Text + "\n\n" + d.Flavor + "\n\n永久收藏：" + ProgressionService.Owned(progression.Data.Profile, d.Key) + "/" + ProgressionService.Limit(d) + "；指定合成：" + ProgressionService.CraftCost(d) + " 铸片/张\n使用此牌的配方：" + (uses.Length == 0 ? "无" : uses));
            Button(box, new Rect(40, 505, 1050, 62), "返回收藏", ShowCollection); FinishPage();
        }
        void ShowWorkshopSlots()
        {
            LeaveWorkshop(() =>
            {
                Clear(); BeginPage("WorkshopSlots"); Header("牌组配方 · " + progression.Data.Profile.Decks.Count + "/12 槽位");
                var decks = progression.Data.Profile.Decks;
                for (int i = 0; i < decks.Count; i++)
                {
                    var d = decks[i]; var area = Box(root, new Rect(40 + i % 3 * 515, 100 + i / 3 * 165, 492, 150), panel);
                    Button(area, new Rect(8, 8, 476, 76), d.Name + " · " + d.Cards.Count + " 张\n" + Database.Keeper(d.Keeper).Name, () => { workshop = d.Clone(); workshopDirty = false; deckPage = 0; ShowCollection(); }, true, background, 20);
                    Button(area, new Rect(8, 94, 230, 43), "复制配方", () => { workshop = d.Clone(); workshop.Key = Guid.NewGuid().ToString("N"); workshop.Name = "副本 " + d.Name; workshopDirty = true; ShowCollection(); }, decks.Count < 12, background, 18);
                    Button(area, new Rect(250, 94, 234, 43), "删除配方…", () => { var box = Modal("DeleteDeck", "删除配方，不删除卡牌"); Label(box, new Rect(40, 150, 1000, 150), "确定删除「" + d.Name + "」？永久收藏不受影响。", 25, ink); Button(box, new Rect(40, 450, 500, 65), "取消", ShowWorkshopSlots); Button(box, new Rect(600, 450, 500, 65), "确认删除", () => MetaTry(() => { progression.DeleteDeck(d.Key); if (workshop?.Key == d.Key) workshop = null; if (chosenDeck?.Key == d.Key) chosenDeck = null; ShowWorkshopSlots(); })); FinishPage(); }, true, background, 18);
                }
                Button(root, new Rect(40, 798, 720, 58), "新建空白牌组", () => { workshop = new DeckList { Key = Guid.NewGuid().ToString("N"), Name = "新牌组", Keeper = Database.Precons[0].Keeper }; workshopDirty = true; deckPage = 0; ShowCollection(); }, decks.Count < 12);
                Button(root, new Rect(800, 798, 740, 58), "返回收藏", ShowCollection); FinishPage();
            });
        }
        void ShowKeeperPicker()
        {
            Clear(); BeginPage("KeeperPicker"); Header("选择钟主 · 未解锁钟主仅限试用");
            for (int i = 0; i < Database.Keepers.Count; i++)
            {
                var k = Database.Keepers[i]; var visual = CardWidget(root, "KeeperCard", new Rect(40 + i % 5 * 310, 110 + i / 5 * 310, 295, 285));
                BindKeeper(visual, k, progression.Data.Profile.Keepers.Contains(k.Key) ? "已解锁" : "仅试用");
                visual.SetSelected(workshop != null && workshop.Keeper == k.Key);
                visual.Interaction.onClick.AddListener(() => { workshop.Keeper = k.Key; workshopDirty = true; ShowCollection(); });
            }
            Button(root, new Rect(40, 790, 1500, 62), "返回配方", ShowCollection); FinishPage();
        }
        void ShowProfile()
        {
            State = null; Clear(); BeginPage("Profile"); Header("钟主档案 / 背包 · 铸片 " + progression.Data.Profile.CraftShards);
            string[] titles = { "初识时序", "循声而行", "熟悉节律", "编排未来", "自定时刻" }; int[] levels = { 0, 30, 80, 160, 300 };
            for (int i = 0; i < Database.Keepers.Count; i++)
            {
                var k = Database.Keepers[i]; progression.Data.Profile.Mastery.TryGetValue(k.Key, out int xp); int level = 0; while (level < 4 && xp >= levels[level + 1]) level++;
                var visual = CardWidget(root, "KeeperCard", new Rect(40 + i % 5 * 310, 104 + i / 5 * 272, 295, 254));
                BindKeeper(visual, k, progression.Data.Profile.Keepers.Contains(k.Key) ? titles[level] + " · 熟练度 " + xp : "未解锁 · 仅试用", k.Text + (level < 4 ? "\n下一阶熟练度：" + levels[level + 1] : "\n已达本期上限")); visual.Interaction.interactable = false;
            }
            Label(root, new Rect(40, 672, 1490, 85), "背包：本期暂无定向凭证来源；铸片直接用于收藏合成，不需要点击使用。\n熟练度只解锁文本称号，不增加战斗属性。其余钟主的机制试炼与正式外观留到后续版本。", 21, muted);
            Button(root, new Rect(40, 790, 720, 62), "查看永久账单", ShowReceipts);
            Button(root, new Rect(800, 790, 740, 62), "返回菜单", ShowMenu); FinishPage();
        }
        void ShowReceipts()
        {
            Clear(); BeginPage("Receipts"); Header("最近结算 · 已入账收益不会因旅程失败收回");
            var receipts = progression.Data.Settlements.AsEnumerable().Reverse().Take(9).ToList();
            for (int i = 0; i < receipts.Count; i++) { var r = receipts[i]; Label(root, new Rect(48, 105 + i * 72, 1480, 65), r.Reason + " · " + Database.Keeper(r.Keeper).Name + " · " + r.Outcome + "\n永久铸片 +" + r.Total + " / 熟练度 +" + r.Mastery + " · " + (r.Applied ? "已入账" : "待恢复"), 20, ink); }
            if (receipts.Count == 0) Label(root, new Rect(48, 140, 1400, 80), "尚无对局账单。初始赠送、合成及牌组里程碑不计入对局收益。", 23, muted);
            Button(root, new Rect(48, 794, 1490, 58), "返回档案", ShowProfile); FinishPage();
        }
    }
}
