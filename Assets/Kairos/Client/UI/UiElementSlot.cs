using UnityEngine;

namespace Kairos.Client
{
    // Stable binding identity. Author layout/style on the Transform and Graphic, not in controller code.
    public sealed class UiElementSlot : MonoBehaviour
    {
        public string key;
        public Rect sourceRect;
        public bool dynamicLayout;
        public bool bindColor;
        public bool subpanel;
    }
}
