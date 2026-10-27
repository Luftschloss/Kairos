using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed class UiFramework : MonoBehaviour
    {
        public RectTransform screen, pageLayer, modalLayer, effectsLayer;
        [SerializeField] string currentPage;
        public string CurrentPage => currentPage;
        public bool HasModal
        { get { if (modalLayer == null) return false; for (int i = 0; i < modalLayer.childCount; i++) if (modalLayer.GetChild(i).gameObject.activeSelf) return true; return false; } }
        public bool CaptureMode { get; set; }
        public Action<UiPanelTemplate> Captured;
        UiPanelTemplate page;
        readonly List<UiPanelTemplate> instances = new List<UiPanelTemplate>();
        readonly Dictionary<string, UiPanelTemplate> templates = new Dictionary<string, UiPanelTemplate>();
        public void ClearViews()
        {
            EndBinding();
            foreach (var parent in new[] { pageLayer, modalLayer, effectsLayer }) if (parent != null)
                for (int i = parent.childCount - 1; i >= 0; i--) { var go = parent.GetChild(i).gameObject; go.SetActive(false); if (Application.isPlaying) Destroy(go); else DestroyImmediate(go); }
            instances.Clear(); page = null; currentPage = "";
        }
        public RectTransform ShowPage(string id)
        {
            currentPage = id; page = InstantiatePanel("Pages/" + id, pageLayer, new Rect(0, 0, 1600, 900));
            return page.content != null ? page.content : (RectTransform)page.transform;
        }
        public void CloseModal()
        {
            for (int i = modalLayer.childCount - 1; i >= 0; i--)
            {
                var go = modalLayer.GetChild(i).gameObject; instances.Remove(go.GetComponent<UiPanelTemplate>()); go.SetActive(false);
                if (Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            effectsLayer.SetAsLastSibling();
        }
        public RectTransform ShowModal(string id)
        {
            CloseModal();
            var modal = InstantiatePanel("Modals/" + id, modalLayer, new Rect(0, 0, 1600, 900));
            return modal.content;
        }
        public RectTransform Subpanel(Transform parent, string id, Rect rect)
        {
            var owner = parent.GetComponentInParent<UiPanelTemplate>(true);
            if (owner == null) throw new InvalidOperationException("Missing owner for panel " + id);
            var slot = owner.Take(parent, "Panel:" + id, rect, out var key);
            if (slot != null)
            {
                var existing = slot.GetComponent<UiPanelTemplate>(); existing.BeginBinding(); instances.Add(existing); return (RectTransform)slot.transform;
            }
            if (!CaptureMode) throw new InvalidOperationException(owner.templateId + " missing panel " + id);
            var panel = InstantiatePanel("Panels/" + id, parent, rect);
            var tag = panel.GetComponent<UiElementSlot>() ?? panel.gameObject.AddComponent<UiElementSlot>(); tag.key = key; tag.sourceRect = rect; tag.subpanel = true; owner.Register(tag);
            return (RectTransform)panel.transform;
        }
        public RectTransform Resolve(Transform parent, string role, string name, Rect source, out bool created)
        {
            var owner = parent.GetComponentInParent<UiPanelTemplate>(true);
            if (owner == null) throw new InvalidOperationException("UI element outside managed panel: " + name);
            var slot = owner.Take(parent, role, source, out var key);
            if (slot != null)
            {
                created = false;
                if (slot.dynamicLayout) Position((RectTransform)slot.transform, source);
                if (slot.subpanel) { var panel = slot.GetComponent<UiPanelTemplate>(); panel.BeginBinding(); instances.Add(panel); }
                return (RectTransform)slot.transform;
            }
            if (!CaptureMode) throw new InvalidOperationException(owner.templateId + " missing " + role + " slot " + key);
            var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform; Position(rt, source);
            var tag = go.AddComponent<UiElementSlot>(); tag.key = key; tag.sourceRect = source; owner.Register(tag);
            created = true; return rt;
        }
        public void Adopt(Transform parent, RectTransform instance, string role, Rect source)
        {
            var owner = parent.GetComponentInParent<UiPanelTemplate>(true);
            var slot = owner.Take(parent, role, source, out var key);
            if (slot != null) throw new InvalidOperationException("Adopt used for existing slot");
            var tag = instance.gameObject.AddComponent<UiElementSlot>(); tag.key = key; tag.sourceRect = source; owner.Register(tag);
        }
        public UiElementSlot Existing(Transform parent, string role, Rect source, out string key)
        { return parent.GetComponentInParent<UiPanelTemplate>(true).Take(parent, role, source, out key); }
        public void RegisterWidget(Transform parent, RectTransform instance, string key, Rect source)
        { var slot = instance.gameObject.AddComponent<UiElementSlot>(); slot.key = key; slot.sourceRect = source; parent.GetComponentInParent<UiPanelTemplate>(true).Register(slot); }
        public void EndBinding()
        {
            foreach (var panel in instances) if (panel != null) panel.EndBinding();
            if (CaptureMode && Captured != null) for (int i = instances.Count - 1; i >= 0; i--) if (instances[i] != null) Captured(instances[i]);
            if (HasModal) effectsLayer.SetAsFirstSibling(); else effectsLayer.SetAsLastSibling();
        }
        UiPanelTemplate InstantiatePanel(string id, Transform parent, Rect rect)
        {
            UiPanelTemplate result;
            if (CaptureMode && !templates.ContainsKey(id))
            { var known = Resources.Load<GameObject>("KairosUI/" + id); if (known != null) templates[id] = known.GetComponent<UiPanelTemplate>(); }
            if (CaptureMode && templates.TryGetValue(id, out var captured) && captured != null)
            { result = Instantiate(captured, parent, false); }
            else if (CaptureMode)
            {
                var go = new GameObject(id.Substring(id.LastIndexOf('/') + 1), typeof(RectTransform), typeof(UiPanelTemplate)); go.transform.SetParent(parent, false);
                result = go.GetComponent<UiPanelTemplate>(); Position((RectTransform)go.transform, rect);
                if (id.StartsWith("Modals/"))
                {
                    var shade = go.AddComponent<Image>(); shade.color = new Color(.025f, .04f, .06f, .97f);
                    var body = new GameObject("Body", typeof(RectTransform), typeof(Image), typeof(UiElementSlot)); body.transform.SetParent(go.transform, false);
                    var br = (RectTransform)body.transform; Position(br, new Rect(230, 168, 1140, 620)); body.GetComponent<Image>().color = new Color32(23, 34, 46, 255);
                    var tag = body.GetComponent<UiElementSlot>(); tag.key = "Body"; tag.sourceRect = new Rect(230, 168, 1140, 620); result.content = br;
                }
                else result.content = (RectTransform)go.transform;
                if (id.StartsWith("Panels/")) { var image = go.AddComponent<Image>(); image.color = new Color32(23, 34, 46, 255); image.raycastTarget = false; }
            }
            else
            {
                if (!templates.TryGetValue(id, out var asset))
                { var prefab = Resources.Load<GameObject>("KairosUI/" + id); asset = prefab == null ? null : prefab.GetComponent<UiPanelTemplate>(); templates[id] = asset; }
                if (asset == null) throw new InvalidOperationException("Missing UI Prefab: " + id);
                result = Instantiate(asset, parent, false);
            }
            result.templateId = id; result.BeginBinding(); instances.Add(result);
            // The modal body is structural, not a data slot.
            if (result.content != null && result.content != result.transform && result.content.GetComponent<UiElementSlot>() is UiElementSlot bodySlot) result.Register(bodySlot);
            return result;
        }
        public void ResetTemplateCache() => templates.Clear();
        public void CapturedAsset(string id, UiPanelTemplate asset) { templates[id] = asset; }
        public static void Position(RectTransform rt, Rect area)
        { rt.anchorMin = rt.anchorMax = new Vector2(0, 1); rt.pivot = new Vector2(0, 1); rt.anchoredPosition = new Vector2(area.x, -area.y); rt.sizeDelta = area.size; }
    }
}
