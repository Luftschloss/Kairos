using System.IO;
using Kairos.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Editor
{
    public static class CollectionFilterPrefabTools
    {
        const string Path = "Assets/Kairos/UI/Resources/KairosUI/CollectionFilter.prefab";
        [MenuItem("KAIROS/UI/Create Missing Collection Filter")]
        public static void CreateMissing()
        {
            if (File.Exists(Path)) return;
            var go = new GameObject("CollectionFilter", typeof(RectTransform), typeof(Image), typeof(Dropdown));
            try
            {
                var root = go.GetComponent<RectTransform>(); root.anchorMin = root.anchorMax = new Vector2(0, 1); root.pivot = new Vector2(0, 1); root.sizeDelta = new Vector2(172, 43);
                var image = go.GetComponent<Image>(); image.color = new Color32(23, 34, 46, 255);
                var dropdown = go.GetComponent<Dropdown>(); dropdown.targetGraphic = image;
                var caption = Text(root, "Caption", new Color32(219, 225, 226, 255), 16); Stretch(caption.rectTransform, new Vector2(12, 4), new Vector2(-28, -4)); caption.alignment = TextAnchor.MiddleLeft;
                var arrow = Text(root, "Arrow", new Color32(225, 190, 116, 255), 18); var ar = arrow.rectTransform; ar.anchorMin = new Vector2(1, 0); ar.anchorMax = Vector2.one; ar.offsetMin = new Vector2(-28, 0); ar.offsetMax = new Vector2(-6, 0); arrow.text = "▾"; arrow.alignment = TextAnchor.MiddleCenter;
                var template = Rect(root, "Template"); template.anchorMin = new Vector2(0, 0); template.anchorMax = new Vector2(1, 0); template.pivot = new Vector2(.5f, 1); template.anchoredPosition = new Vector2(0, -4); template.sizeDelta = new Vector2(0, 252);
                var bg = template.gameObject.AddComponent<Image>(); bg.color = new Color32(29, 43, 57, 255);
                var scroll = template.gameObject.AddComponent<ScrollRect>(); scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
                var viewport = Rect(template, "Viewport"); Stretch(viewport, new Vector2(2, 2), new Vector2(-2, -2)); viewport.gameObject.AddComponent<RectMask2D>(); var vi = viewport.gameObject.AddComponent<Image>(); vi.color = new Color32(23, 34, 46, 255);
                var content = Rect(viewport, "Content"); content.anchorMin = new Vector2(0, 1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f, 1); content.anchoredPosition = Vector2.zero; content.sizeDelta = new Vector2(0, 36);
                var item = Rect(content, "Item"); item.anchorMin = Vector2.zero; item.anchorMax = new Vector2(1, 0); item.pivot = new Vector2(.5f, .5f); item.anchoredPosition = new Vector2(0, 18); item.sizeDelta = new Vector2(0, 36);
                var itemImage = item.gameObject.AddComponent<Image>(); itemImage.color = new Color32(29, 43, 57, 255);
                var toggle = item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = itemImage; toggle.isOn = true;
                var check = Rect(item, "Selected"); check.anchorMin = Vector2.zero; check.anchorMax = new Vector2(0, 1); check.offsetMin = new Vector2(3, 5); check.offsetMax = new Vector2(6, -5); var marker = check.gameObject.AddComponent<Image>(); marker.color = new Color32(74, 181, 176, 255); marker.raycastTarget = false; toggle.graphic = marker;
                var label = Text(item, "ItemLabel", new Color32(219, 225, 226, 255), 16); Stretch(label.rectTransform, new Vector2(12, 2), new Vector2(-8, -2)); label.alignment = TextAnchor.MiddleLeft;
                var colors = toggle.colors; colors.highlightedColor = new Color(1.4f, 1.4f, 1.4f, 1); colors.pressedColor = new Color(.8f, .9f, 1, 1); toggle.colors = colors;
                scroll.viewport = viewport; scroll.content = content; dropdown.template = template; dropdown.captionText = caption; dropdown.itemText = label;
                dropdown.options.Add(new Dropdown.OptionData("全部")); dropdown.RefreshShownValue(); template.gameObject.SetActive(false);
                PrefabUtility.SaveAsPrefabAsset(go, Path);
            }
            finally { Object.DestroyImmediate(go); }
        }
        static RectTransform Rect(Transform parent, string name)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); return go.GetComponent<RectTransform>(); }
        static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
        { rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = min; rt.offsetMax = max; }
        static Text Text(Transform parent, string name, Color color, int size)
        { var rt = Rect(parent, name); var text = rt.gameObject.AddComponent<Text>(); text.font = UiFonts.Regular; text.fontSize = size; text.color = color; text.raycastTarget = false; text.supportRichText = false; return text; }
    }
}
