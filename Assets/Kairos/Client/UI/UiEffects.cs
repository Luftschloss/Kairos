using UnityEngine;
using UnityEngine.UI;

namespace Kairos.Client
{
    public sealed partial class GameController
    {
        RectTransform EffectWidget(string id, Rect area)
        {
            var template = Resources.Load<GameObject>("KairosUI/Effects/" + id);
            if (template == null) throw new System.InvalidOperationException("Missing effect Prefab " + id);
            var rt = Instantiate(template, ui.effectsLayer, false).GetComponent<RectTransform>(); UiFramework.Position(rt, area); return rt;
        }
    }
}
