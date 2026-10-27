using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kairos.Client
{
    public sealed class UiPanelTemplate : MonoBehaviour
    {
        public string templateId;
        public RectTransform content;
        readonly Dictionary<string, UiElementSlot> slots = new Dictionary<string, UiElementSlot>();
        readonly Dictionary<string, int> counts = new Dictionary<string, int>();
        readonly Dictionary<string, UiElementSlot> dynamicPrototypes = new Dictionary<string, UiElementSlot>();
        readonly HashSet<UiElementSlot> used = new HashSet<UiElementSlot>();
        bool indexed;
        public void BeginBinding()
        {
            if (!indexed)
            {
                foreach (var slot in GetComponentsInChildren<UiElementSlot>(true))
                {
                    if (slot.transform == transform) continue;
                    var owner = slot.subpanel ? slot.transform.parent.GetComponentInParent<UiPanelTemplate>(true) : slot.GetComponentInParent<UiPanelTemplate>(true);
                    if (owner != this) continue;
                    if (slots.ContainsKey(slot.key)) throw new InvalidOperationException(templateId + " duplicate UI slot: " + slot.key);
                    slots.Add(slot.key, slot);
                    if (slot.dynamicLayout) dynamicPrototypes[Family(slot.key)] = slot;
                }
                indexed = true;
            }
            counts.Clear(); used.Clear();
        }
        public UiElementSlot Take(Transform parent, string role, Rect source, out string key)
        {
            string parentKey = parent == transform ? "root" : parent.GetComponent<UiElementSlot>()?.key ?? parent.name;
            string signature = parentKey + "/" + role + "/" + Number(source.x) + "," + Number(source.y);
            counts.TryGetValue(signature, out int ordinal); counts[signature] = ordinal + 1;
            key = signature + "/" + ordinal;
            if (slots.TryGetValue(key, out var slot))
            {
                used.Add(slot); slot.gameObject.SetActive(true); return slot;
            }
            if (dynamicPrototypes.TryGetValue(Family(key), out var prototype))
            {
                var clone = Instantiate(prototype, parent, false); string previous = prototype.key; clone.key = key; clone.gameObject.name = prototype.gameObject.name;
                slots.Add(key, clone); used.Add(clone); clone.gameObject.SetActive(true);
                foreach (var child in clone.GetComponentsInChildren<UiElementSlot>(true))
                {
                    if (child == clone || child.GetComponentInParent<UiPanelTemplate>(true) != this) continue;
                    if (child.key.StartsWith(previous + "/", StringComparison.Ordinal)) child.key = key + child.key.Substring(previous.Length);
                    slots[child.key] = child;
                    if (child.dynamicLayout) dynamicPrototypes[Family(child.key)] = child;
                }
                return clone;
            }
            return null;
        }
        string Family(string key)
        {
            int ordinal = key.LastIndexOf('/'); int rect = key.LastIndexOf('/', ordinal - 1);
            string family = key.Substring(0, rect);
            if (templateId == "Panels/Timeline") { var point = key.Substring(rect + 1, ordinal - rect - 1).Split(','); family += "/row:" + point[point.Length - 1]; }
            return family;
        }
        public void Register(UiElementSlot slot) { slots[slot.key] = slot; used.Add(slot); }
        public void EndBinding()
        {
            foreach (var entry in slots) if (entry.Value != null && !used.Contains(entry.Value)) entry.Value.gameObject.SetActive(false);
        }
        static string Number(float n) => n.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    }
}
