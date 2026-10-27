using System;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        Dropdown collectionFilterPrefab;
        void FilterDropdown(Transform parent, Rect rect, string name, string prefix, string[] labels, int selected, Action<int> changed)
        {
            if (collectionFilterPrefab == null)
            {
                var asset = Resources.Load<GameObject>("KairosUI/CollectionFilter");
                if (asset == null) throw new InvalidOperationException("Missing CollectionFilter Prefab");
                collectionFilterPrefab = asset.GetComponent<Dropdown>();
            }
            var slot = ui.Existing(parent, "Dropdown:" + name, rect, out var key);
            Dropdown dropdown;
            if (slot != null) dropdown = slot.GetComponent<Dropdown>();
            else
            {
                if (!uiCaptured) throw new InvalidOperationException("Missing dropdown slot " + key);
                dropdown = Instantiate(collectionFilterPrefab, parent, false);
                var rt = (RectTransform)dropdown.transform; rt.anchoredPosition = new Vector2(rect.x, -rect.y); rt.sizeDelta = rect.size;
                ui.RegisterWidget(parent, rt, key, rect);
            }
            dropdown.name = name;
            dropdown.ClearOptions();
            foreach (var label in labels) dropdown.options.Add(new Dropdown.OptionData(prefix + label));
            dropdown.SetValueWithoutNotify(Mathf.Clamp(selected, 0, labels.Length - 1));
            dropdown.RefreshShownValue();
            dropdown.onValueChanged.RemoveAllListeners();
            dropdown.onValueChanged.AddListener(index => { changed(index); cardPage = 0; ShowCollection(); });
        }
    }
}
