using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    [AddComponentMenu("KAIROS UI/Repeater Layout")]
    public sealed class UiRepeaterLayout : MonoBehaviour
    {
        public Vector2 cellSize = new Vector2(220, 110);
        public Vector2 spacing = new Vector2(14, 0);
        public int columns = 5;
        public Vector2 origin;
        public Rect Area(int index)
        { return new Rect(origin.x + index % Mathf.Max(1, columns) * (cellSize.x + spacing.x), origin.y + index / Mathf.Max(1, columns) * (cellSize.y + spacing.y), cellSize.x, cellSize.y); }
    }
}
