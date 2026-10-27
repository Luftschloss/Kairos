using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    // Layout lives in Prefabs; this component only binds content and visual state.
    public sealed class CardVisual : MonoBehaviour
    {
        [SerializeField] RawImage illustration;
        [SerializeField] Image schoolFrame;
        [SerializeField] Image rarityMark;
        [SerializeField] Image surface;
        [SerializeField] Text title;
        [SerializeField] Text cost;
        [SerializeField] Text stats;
        [SerializeField] Text rulesText;
        [SerializeField] Text metadata;
        [SerializeField] Text status;
        [SerializeField] Button interaction;
        [SerializeField] bool showRuleSummary;
        [SerializeField] CardFrameHighlight selectionHighlight;
        [SerializeField] CardFrameHighlight actionHighlight;
        [SerializeField] Color selectionColor = new Color32(255, 216, 126, 255);
        public bool Selected { get; private set; }
        static readonly Dictionary<string, Texture2D> portraits = new Dictionary<string, Texture2D>();
        static readonly Dictionary<string, Sprite> frames = new Dictionary<string, Sprite>();
        [System.Serializable] sealed class PortraitFramingEntry { public string key; public float focusY = .47f; }
        [System.Serializable] sealed class PortraitFraming { public PortraitFramingEntry[] portraits; }
        static readonly Dictionary<string, float> portraitFocus = new Dictionary<string, float>();
        static bool framingLoaded;
        static float GetPortraitFocus(string key)
        {
            if (!framingLoaded)
            {
                var asset = Resources.Load<TextAsset>("KairosArt/portrait-framing");
                var data = asset == null ? null : JsonUtility.FromJson<PortraitFraming>(asset.text);
                framingLoaded = asset != null;
                if (data != null && data.portraits != null)
                    foreach (var entry in data.portraits)
                        if (entry != null && !string.IsNullOrEmpty(entry.key)) portraitFocus[entry.key] = Mathf.Clamp01(entry.focusY);
            }
            return portraitFocus.TryGetValue(key, out var focus) ? focus : .47f;
        }
        public Button Interaction => interaction;
        public RectTransform Rect => (RectTransform)transform;
        public void Bind(string key, string school, string rarity, string cardTitle, string costText, string statText, string rules, string meta, string state, bool available, bool selected)
        {
            if (!portraits.TryGetValue(key, out var portrait)) { portrait = Resources.Load<Texture2D>("KairosArt/Portraits/" + key); portraits[key] = portrait; }
            if (!frames.TryGetValue(school, out var frame)) { frame = Resources.Load<Sprite>("KairosArt/Frames/" + school); frames[school] = frame; }
            if (illustration != null)
            {
                illustration.texture = portrait; illustration.enabled = portrait != null; illustration.color = available ? Color.white : new Color(.58f, .58f, .58f, 1);
                if (portrait != null)
                {
                    var area = illustration.rectTransform.rect; float aspect = area.height > 0 ? area.width / area.height : .75f; float imageAspect = (float)portrait.width / portrait.height;
                    float uvHeight = Mathf.Min(1, imageAspect / aspect), uvWidth = Mathf.Min(1, aspect / imageAspect);
                    // Art-authored focus is measured from the top; Unity UVs start at the bottom.
                    float focusFromBottom = 1 - GetPortraitFocus(key);
                    illustration.uvRect = new Rect((1 - uvWidth) * .5f, Mathf.Clamp(focusFromBottom - uvHeight * .5f, 0, 1 - uvHeight), uvWidth, uvHeight);
                }
            }
            // Author fonts in the Prefab; only replace missing/built-in Latin fallback references.
            if (title != null && UiFonts.IsFallback(title.font)) title.font = UiFonts.Bold;
            foreach (var label in new[] { cost, stats, rulesText, metadata, status }) if (label != null && UiFonts.IsFallback(label.font)) label.font = UiFonts.Regular;
            if (schoolFrame != null) { schoolFrame.sprite = frame; schoolFrame.enabled = frame != null; }
            if (rarityMark != null) rarityMark.color = rarity == "LEGENDARY" ? new Color32(244, 194, 91, 255) : rarity == "EPIC" ? new Color32(174, 139, 219, 255) : rarity == "RARE" ? new Color32(115, 179, 216, 255) : new Color32(169, 164, 145, 255);
            if (surface != null) surface.color = new Color32(25, 32, 39, 255);
            SetSelected(selected);
            if (title != null) title.text = cardTitle;
            if (cost != null) cost.text = costText;
            if (stats != null) stats.text = statText;
            if (rulesText != null) rulesText.text = showRuleSummary ? statText : rules;
            if (metadata != null) metadata.text = meta;
            if (status != null) status.text = state;
        }
        public void SetSelected(bool selected)
        {
            Selected = selected;
            if (selectionHighlight != null) { selectionHighlight.color = selectionColor; selectionHighlight.enabled = selected; selectionHighlight.raycastTarget = false; }
            if (actionHighlight != null) actionHighlight.enabled = actionHint && !selected;
        }
        bool actionHint;
        public void SetActionHint(bool visible, Color color)
        {
            actionHint = visible;
            if (actionHighlight != null) { actionHighlight.color = color; actionHighlight.enabled = visible && !Selected; actionHighlight.raycastTarget = false; }
        }
        public void SetStatusColor(Color color) { if (status != null) status.color = color; }
    }
}
