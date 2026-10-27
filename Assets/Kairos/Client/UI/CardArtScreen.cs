using System.Collections.Generic;
using Kairos.Engine;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        readonly Dictionary<string, CardVisual> visualPrefabs = new Dictionary<string, CardVisual>();
        CardVisual CardWidget(Transform parent, string prefab, Rect rect)
        {
            if (!visualPrefabs.TryGetValue(prefab, out var template))
            { var asset = Resources.Load<GameObject>("KairosUI/" + prefab); template = asset == null ? null : asset.GetComponent<CardVisual>(); visualPrefabs[prefab] = template; }
            if (template == null) throw new System.InvalidOperationException("Missing card UI Prefab: " + prefab);
            var slot = ui.Existing(parent, "Card:" + prefab, rect, out var key);
            if (slot != null) { if (slot.dynamicLayout) UiFramework.Position((RectTransform)slot.transform, rect); return slot.GetComponent<CardVisual>(); }
            if (!uiCaptured) throw new System.InvalidOperationException("Missing card binding slot: " + key);
            var visual = Instantiate(template, parent, false); var rt = visual.Rect;
            rt.anchoredPosition = new Vector2(rect.x, -rect.y); rt.sizeDelta = rect.size;
            ui.RegisterWidget(parent, rt, key, rect); return visual;
        }
        void BindCard(CardVisual visual, CardDefinition d, string state = "", bool available = true, bool selected = false, string meta = null)
        {
            visual.Bind(d.Key, d.School, d.Rarity, d.Name, d.Type == "OMEN" ? "伏笔 2刻" : d.Time + "刻", d.Type == "UNIT" ? d.Atk + "攻 / " + d.Hp + "血 / 节奏" + d.Cadence : KindName(d.Type), d.Text, meta ?? SchoolName(d.School) + " · " + RarityName(d.Rarity), state, available, selected);
        }
        void BindKeeper(CardVisual visual, KeeperDefinition k, string state, string body = null)
        { visual.Bind(k.Key, k.School, "LEGENDARY", k.Name, "时器 " + k.Ability.Cost + "刻", "冷却 " + k.Ability.CooldownBars + " 次鸣钟", body ?? k.Text, SchoolName(k.School) + " · 钟主", state, true, false); }
        void DrawKeeperPanel(KeeperDefinition k, int p, int y, bool friendly, Target target, Transform parent)
        {
            var visual = CardWidget(parent, "KeeperBadge", new Rect(32, y, 298, friendly ? 73 : 65));
            BindKeeper(visual, k, "命烛 " + View.Players[p].Life + " / " + View.Config.LifeMax, "");
            visual.SetSelected(selectedTarget.Equals(target));
            visual.Interaction.interactable = lesson == null ? CanAct : TargetAvailable(target);
            visual.Interaction.onClick.AddListener(() => SelectTarget(target));
            BindTarget(visual.Interaction, target); Highlight(visual.Interaction, TargetAvailable(target));
        }
    }
}
