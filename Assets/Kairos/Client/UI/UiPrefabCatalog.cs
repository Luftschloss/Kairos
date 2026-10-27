using UnityEngine;

namespace Kairos.Client
{
    public sealed class UiPrefabCatalog : ScriptableObject
    {
        public GameObject uiRoot;
        public GameObject[] pages, panels, modals, effects, widgets;
    }
}
