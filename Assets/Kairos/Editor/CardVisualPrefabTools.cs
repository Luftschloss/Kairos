using System;
using System.IO;
using Kairos.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Editor
{
    public static class CardVisualPrefabTools
    {
        const string Folder = "Assets/Kairos/UI/Resources/KairosUI";
        static Font Font => UiFonts.Regular;
        [MenuItem("KAIROS/Art/Create Missing Card Prefabs")]
        public static void CreateMissing()
        {
            Directory.CreateDirectory(Folder);
            Build("CardFace", 300, 400, new Rect(10, 10, 280, 330), new Rect(18, 343, 264, 29), new Rect(17, 321, 266, 68), 22);
            Build("HandCard", 136, 188, new Rect(7, 7, 122, 129), new Rect(8, 136, 120, 28), new Rect(8, 116, 120, 29), 17);
            Build("BoardUnit", 220, 110, new Rect(5, 5, 65, 100), new Rect(78, 8, 136, 23), new Rect(78, 57, 136, 26), 17);
            Build("CollectionTile", 276, 156, new Rect(6, 6, 98, 144), new Rect(114, 10, 156, 45), new Rect(114, 92, 155, 43), 18);
            Build("KeeperCard", 295, 285, new Rect(7, 7, 116, 145), new Rect(134, 12, 150, 60), new Rect(15, 163, 265, 89), 19);
            Build("KeeperBadge", 298, 73, new Rect(4, 4, 49, 65), new Rect(60, 8, 230, 27), new Rect(60, 38, 229, 24), 17);
            AssetDatabase.SaveAssets(); Debug.Log("Created missing KAIROS card Prefabs. Existing Prefabs preserved.");
        }
        static RectTransform Part(Transform parent, string name, Rect area, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(area.x / width, 1 - area.yMax / height); rt.anchorMax = new Vector2(area.xMax / width, 1 - area.y / height);
            rt.offsetMin = rt.offsetMax = Vector2.zero; return rt;
        }
        static Text TextPart(Transform parent, string name, Rect area, float w, float h, int size, Color color)
        {
            var rt = Part(parent, name, area, w, h); var t = rt.gameObject.AddComponent<Text>(); t.font = Font; t.fontSize = size; t.color = color;
            t.raycastTarget = false; t.supportRichText = false; t.horizontalOverflow = HorizontalWrapMode.Wrap; t.verticalOverflow = VerticalWrapMode.Truncate;
            t.text = name; return t;
        }
        static void Ref(SerializedObject so, string name, UnityEngine.Object value) => so.FindProperty(name).objectReferenceValue = value;
        static void Build(string name, float w, float h, Rect artRect, Rect titleRect, Rect rulesRect, int titleSize)
        {
            string path = Folder + "/" + name + ".prefab";
            if (File.Exists(path)) return;
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button), typeof(CardVisual));
            try
            {
                var rt = go.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(w, h);
                var surface = go.GetComponent<Image>(); surface.color = new Color32(25, 32, 39, 255);
                var button = go.GetComponent<Button>(); button.targetGraphic = surface;
                var colors = button.colors; colors.normalColor = colors.highlightedColor = colors.selectedColor = colors.disabledColor = Color.white; colors.pressedColor = new Color(.9f, .9f, .9f, 1); button.colors = colors;
                var art = Part(rt, "Illustration", artRect, w, h).gameObject.AddComponent<RawImage>(); art.raycastTarget = false;
                art.texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Kairos/Art/Resources/KairosArt/Portraits/DAWN_SCOUT.png");
                var overlay = Part(rt, "SchoolFrame", new Rect(0, 0, w, h), w, h).gameObject.AddComponent<Image>(); overlay.raycastTarget = false;
                overlay.sprite = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Kairos/Art/Resources/KairosArt/Frames/DAWN.png");
                overlay.type = Image.Type.Sliced; overlay.fillCenter = false; overlay.pixelsPerUnitMultiplier = 4;
                var rarity = Part(rt, "RarityMark", new Rect(w - 17, 5, 9, 9), w, h).gameObject.AddComponent<Image>(); rarity.raycastTarget = false;
                var title = TextPart(rt, "Title", titleRect, w, h, titleSize, new Color32(239, 226, 194, 255)); title.font = UiFonts.Bold;
                var rules = TextPart(rt, "Rules", rulesRect, w, h, name == "HandCard" ? 12 : name == "CardFace" ? 17 : 15, new Color32(185, 195, 194, 255));
                Rect costRect, statsRect, metaRect, statusRect;
                if (name == "HandCard") { costRect = new Rect(10, 8, 42, 19); statsRect = new Rect(53, 8, 75, 19); metaRect = new Rect(0, 0, 0, 0); statusRect = new Rect(6, 166, 124, 20); }
                else if (name == "BoardUnit") { costRect = new Rect(0, 0, 0, 0); statsRect = new Rect(78, 34, 136, 22); metaRect = new Rect(0, 0, 0, 0); statusRect = new Rect(78, 83, 136, 23); }
                else if (name == "CollectionTile") { costRect = new Rect(114, 58, 61, 23); statsRect = new Rect(0, 0, 0, 0); metaRect = new Rect(114, 81, 156, 22); statusRect = new Rect(113, 125, 156, 25); }
                else if (name == "KeeperCard") { costRect = new Rect(134, 82, 150, 25); statsRect = new Rect(134, 109, 150, 24); metaRect = new Rect(134, 134, 148, 22); statusRect = new Rect(15, 255, 265, 24); }
                else if (name == "KeeperBadge") { costRect = statsRect = metaRect = new Rect(0, 0, 0, 0); statusRect = new Rect(60, 39, 229, 24); }
                else { costRect = new Rect(20, 18, 84, 27); statsRect = new Rect(18, 373, 264, 24); metaRect = new Rect(19, 420, 264, 20); statusRect = new Rect(19, 419, 264, 20); }
                var cost = TextPart(rt, "Cost", costRect, w, h, name == "HandCard" ? 13 : 17, new Color32(240, 211, 151, 255));
                var stats = TextPart(rt, "Stats", statsRect, w, h, name == "HandCard" ? 12 : 15, new Color32(167, 204, 190, 255));
                var meta = TextPart(rt, "Metadata", metaRect, w, h, 14, new Color32(153, 167, 173, 255));
                var status = TextPart(rt, "Status", statusRect, w, h, name == "HandCard" ? 13 : 15, new Color32(102, 205, 192, 255));
                if (name == "HandCard")
                {
                    var band = Part(rt, "StatusBand", statusRect, w, h).gameObject.AddComponent<Image>(); band.color = new Color32(14, 26, 31, 255); band.raycastTarget = false;
                    status.transform.SetAsLastSibling(); status.alignment = TextAnchor.MiddleCenter;
                    var header = Part(rt, "HeaderBand", new Rect(6, 6, 124, 22), w, h).gameObject.AddComponent<Image>(); header.color = new Color32(14, 26, 31, 220); header.raycastTarget = false;
                    cost.transform.SetAsLastSibling(); stats.transform.SetAsLastSibling();
                }
                if (costRect.width == 0) cost.gameObject.SetActive(false);
                if (statsRect.width == 0) stats.gameObject.SetActive(false);
                if (metaRect.width == 0) meta.gameObject.SetActive(false);
                var so = new SerializedObject(go.GetComponent<CardVisual>());
                Ref(so, "illustration", art); Ref(so, "schoolFrame", overlay); Ref(so, "rarityMark", rarity); Ref(so, "surface", surface);
                Ref(so, "title", title); Ref(so, "cost", cost); Ref(so, "stats", stats); Ref(so, "rulesText", rules); Ref(so, "metadata", meta); Ref(so, "status", status); Ref(so, "interaction", button);
                so.FindProperty("showRuleSummary").boolValue = name == "CollectionTile"; so.ApplyModifiedPropertiesWithoutUndo();
                if (name == "KeeperBadge") rules.gameObject.SetActive(false);
                if (name == "CardFace") { status.gameObject.SetActive(false); rules.gameObject.SetActive(false); meta.gameObject.SetActive(false); }
                if (name == "HandCard") { rules.gameObject.SetActive(false); }
                if (name == "CardFace" || name == "KeeperCard") { rules.resizeTextForBestFit = true; rules.resizeTextMinSize = 13; rules.resizeTextMaxSize = rules.fontSize; }
                foreach (var text in new[] { title, stats, meta, status }) { text.resizeTextForBestFit = true; text.resizeTextMinSize = 12; text.resizeTextMaxSize = text.fontSize; }
                if (name == "HandCard")
                {
                    rules.gameObject.SetActive(false); title.resizeTextMinSize = 14; title.resizeTextMaxSize = 17;
                    status.font = UiFonts.Bold; status.fontSize = status.resizeTextMinSize = status.resizeTextMaxSize = 14;
                    stats.font = UiFonts.Bold; stats.fontSize = stats.resizeTextMinSize = stats.resizeTextMaxSize = 13;
                }
                foreach (var pair in new[] { new[] { "selectionHighlight", "SelectionHighlight" }, new[] { "actionHighlight", "ActionHintHighlight" } })
                {
                    var glow = Part(rt, pair[1], new Rect(0, 0, w, h), w, h).gameObject.AddComponent<CardFrameHighlight>(); glow.raycastTarget = false; glow.enabled = false;
                    var glowData = new SerializedObject(glow); glowData.FindProperty("thickness").floatValue = pair[0] == "selectionHighlight" ? 2.5f : 1f; glowData.FindProperty("glowWidth").floatValue = pair[0] == "selectionHighlight" ? 4f : 1f; glowData.ApplyModifiedPropertiesWithoutUndo();
                    Ref(so, pair[0], glow);
                }
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(go, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }
    }
}
