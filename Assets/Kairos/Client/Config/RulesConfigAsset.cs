using Kairos.Engine;
using UnityEngine;

namespace Kairos.Client
{
    [CreateAssetMenu(menuName = "KAIROS/Rules Config", fileName = "DemoRules")]
    public sealed class RulesConfigAsset : ScriptableObject
    {
        public RulesConfig Values = new RulesConfig();
    }
}
