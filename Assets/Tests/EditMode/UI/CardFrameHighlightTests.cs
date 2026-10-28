using System.Linq;
using Kairos.Client;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Kairos.Tests
{
    public sealed class CardFrameHighlightTests
    {
        [Test]
        public void AddingHighlightAlsoAddsCanvasRenderer()
        {
            var go = new GameObject("Highlight dependency test", typeof(RectTransform));
            try
            {
                go.AddComponent<CardFrameHighlight>();
                Assert.IsNotNull(go.GetComponent<CanvasRenderer>());
            }
            finally { Object.DestroyImmediate(go); }
        }

        [Test]
        public void AllUiPrefabsIncludeRenderersForInactiveAndNestedHighlights()
        {
            int checkedHighlights = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Kairos/UI" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var highlight in prefab.GetComponentsInChildren<CardFrameHighlight>(true))
                {
                    Assert.IsNotNull(highlight.GetComponent<CanvasRenderer>(), path + "/" + highlight.name);
                    checkedHighlights++;
                }
            }
            Assert.GreaterOrEqual(checkedHighlights, 12, "The six card widgets must each have two highlights.");
        }

        [TestCase("CollectionTile")]
        [TestCase("CardFace")]
        [TestCase("HandCard")]
        [TestCase("BoardUnit")]
        [TestCase("KeeperCard")]
        [TestCase("KeeperBadge")]
        public void CardSelectionAndActionHintSupportRectMaskLifecycle(string prefabName)
        {
            var canvas = new GameObject("Highlight mask test", typeof(RectTransform), typeof(Canvas));
            canvas.hideFlags = HideFlags.HideAndDontSave;
            canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            try
            {
                var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
                viewport.transform.SetParent(canvas.transform, false);
                ((RectTransform)viewport.transform).sizeDelta = new Vector2(500, 450);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Kairos/UI/Resources/KairosUI/" + prefabName + ".prefab");
                Assert.IsNotNull(prefab);
                var card = Object.Instantiate(prefab, viewport.transform).GetComponent<CardVisual>();
                card.Rect.anchorMin = card.Rect.anchorMax = card.Rect.pivot = new Vector2(.5f, .5f);
                card.Rect.anchoredPosition = Vector2.zero;
                var highlights = card.GetComponentsInChildren<CardFrameHighlight>(true);
                Assert.AreEqual(2, highlights.Length);
                var selection = highlights.Single(h => h.name == "SelectionHighlight");
                var action = highlights.Single(h => h.name == "ActionHintHighlight");
                var art = card.GetComponentInChildren<RawImage>();
                Color artColor = art.color;
                card.Interaction.onClick.AddListener(() => card.SetSelected(!card.Selected));
                var mask = viewport.GetComponent<RectMask2D>();

                card.SetActionHint(true, Color.cyan);
                Canvas.ForceUpdateCanvases(); mask.PerformClipping();
                Assert.IsTrue(action.canvasRenderer.hasRectClipping);
                for (int i = 0; i < 3; i++)
                {
                    card.Interaction.onClick.Invoke();
                    Canvas.ForceUpdateCanvases(); mask.PerformClipping();
                    Assert.IsTrue(selection.enabled);
                    Assert.IsFalse(action.enabled, "Selection takes precedence over action hints.");
                    Assert.IsTrue(selection.canvasRenderer.hasRectClipping);
                    Assert.AreEqual(artColor, art.color, "Selection must not tint the illustration.");
                    Assert.IsFalse(selection.raycastTarget);
                    Assert.IsFalse(action.raycastTarget);

                    viewport.SetActive(false);
                    viewport.SetActive(true);
                    Canvas.ForceUpdateCanvases(); mask.PerformClipping();
                    Assert.IsTrue(selection.canvasRenderer.hasRectClipping);
                    card.Interaction.onClick.Invoke();
                    Canvas.ForceUpdateCanvases(); mask.PerformClipping();
                    Assert.IsFalse(selection.enabled);
                    Assert.IsTrue(action.enabled);
                    Assert.IsTrue(action.canvasRenderer.hasRectClipping);
                }
                card.SetActionHint(false, Color.cyan);
                Object.DestroyImmediate(card.gameObject);
                Canvas.ForceUpdateCanvases(); mask.PerformClipping();
            }
            finally { Object.DestroyImmediate(canvas); }
            LogAssert.NoUnexpectedReceived();
        }
    }
}
