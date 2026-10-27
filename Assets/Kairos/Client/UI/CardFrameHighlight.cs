using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    // A transparent, non-interactive highlight over the existing card frame.
    [AddComponentMenu("KAIROS UI/Card Frame Highlight")]
    public sealed class CardFrameHighlight : MaskableGraphic
    {
        [SerializeField, Min(1)] float thickness = 2;
        [SerializeField, Min(0)] float glowWidth = 4;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); var r = rectTransform.rect;
            if (r.width <= 0 || r.height <= 0) return;
            Ring(vh, r, 0, thickness + glowWidth, new Color(color.r, color.g, color.b, color.a * .12f));
            Ring(vh, r, 0, thickness + glowWidth * .5f, new Color(color.r, color.g, color.b, color.a * .25f));
            Ring(vh, r, 1, thickness, color);
        }
        static void Ring(VertexHelper vh, Rect r, float inset, float width, Color tint)
        {
            float x = r.xMin + inset, y = r.yMin + inset, w = Mathf.Max(0, r.width - 2 * inset), h = Mathf.Max(0, r.height - 2 * inset);
            float t = Mathf.Min(width, Mathf.Min(w, h) * .5f);
            Quad(vh, new Rect(x, y, w, t), tint); Quad(vh, new Rect(x, y + h - t, w, t), tint);
            Quad(vh, new Rect(x, y + t, t, Mathf.Max(0, h - 2 * t)), tint); Quad(vh, new Rect(x + w - t, y + t, t, Mathf.Max(0, h - 2 * t)), tint);
        }
        static void Quad(VertexHelper vh, Rect r, Color tint)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(r.xMin, r.yMin), tint, Vector2.zero); vh.AddVert(new Vector3(r.xMin, r.yMax), tint, Vector2.zero);
            vh.AddVert(new Vector3(r.xMax, r.yMax), tint, Vector2.zero); vh.AddVert(new Vector3(r.xMax, r.yMin), tint, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
