using System;
using System.IO;
using System.Linq;
using Kairos.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Editor
{
    public static class UiPrefabMigration
    {
        const string BasePath = "Assets/Kairos/UI/Resources/KairosUI/";
        public static UiFramework CreateRoot(Transform parent)
        {
            var canvas = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UiFramework)); canvas.transform.SetParent(parent, false);
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay; canvas.GetComponent<Canvas>().pixelPerfect = true;
            var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 900); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            var screen = new GameObject("Screen", typeof(RectTransform), typeof(Image)); screen.transform.SetParent(canvas.transform, false); var rt = screen.GetComponent<RectTransform>(); rt.anchorMin = rt.anchorMax = new Vector2(.5f, .5f); rt.sizeDelta = new Vector2(1600, 900); screen.GetComponent<Image>().color = new Color32(13, 20, 30, 255);
            var ui = canvas.GetComponent<UiFramework>(); ui.screen = rt;
            ui.pageLayer = Layer(rt, "PageLayer"); ui.modalLayer = Layer(rt, "ModalLayer"); ui.effectsLayer = Layer(rt, "EffectsLayer");
            return ui;
        }
        static RectTransform Layer(Transform parent, string name)
        { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent, false); var rt = go.GetComponent<RectTransform>(); UiFramework.Position(rt, new Rect(0, 0, 1600, 900)); return rt; }
        public static void SaveRoot(UiFramework ui)
        {
            string path = BasePath + "UIRoot.prefab";
            if (File.Exists(path)) return;
            var clone = UnityEngine.Object.Instantiate(ui.gameObject);
            try { var f = clone.GetComponent<UiFramework>(); foreach (var layer in new[] { f.pageLayer, f.modalLayer, f.effectsLayer }) for (int i = layer.childCount - 1; i >= 0; i--) UnityEngine.Object.DestroyImmediate(layer.GetChild(i).gameObject); PrefabUtility.SaveAsPrefabAsset(clone, path); }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }
        public static void Save(UiFramework ui, UiPanelTemplate view)
        {
            var clone = UnityEngine.Object.Instantiate(view.gameObject);
            try
            {
                if (view.templateId.StartsWith("Panels/")) foreach (var tag in clone.GetComponents<UiElementSlot>()) UnityEngine.Object.DestroyImmediate(tag);
                // Strip only runtime input adaptors; graphics, buttons and authored child Prefabs remain.
                foreach (var c in clone.GetComponentsInChildren<BattleHit>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in clone.GetComponentsInChildren<BattleDragSource>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var c in clone.GetComponentsInChildren<UnityEngine.EventSystems.EventTrigger>(true)) UnityEngine.Object.DestroyImmediate(c);
                foreach (var button in clone.GetComponentsInChildren<Button>(true)) button.onClick = new Button.ButtonClickedEvent();
                foreach (var input in clone.GetComponentsInChildren<InputField>(true)) input.onValueChanged = new InputField.OnChangeEvent();
                foreach (var dropdown in clone.GetComponentsInChildren<Dropdown>(true)) dropdown.onValueChanged = new Dropdown.DropdownEvent();
                var path = BasePath + view.templateId + ".prefab"; Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                var asset = PrefabUtility.SaveAsPrefabAsset(clone, path); ui.CapturedAsset(view.templateId, asset.GetComponent<UiPanelTemplate>());
            }
            finally { UnityEngine.Object.DestroyImmediate(clone); }
        }
    }
}
