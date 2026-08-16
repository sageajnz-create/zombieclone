using UnityEngine;
using System.Collections.Generic;

namespace Overrun.Data
{
    [CreateAssetMenu(fileName = "AugmentDef_", menuName = "Overrun/Augment Definition")]
    public class AugmentDefinition : ScriptableObject
    {
        public string Name;
        public string Description;
        public int Rarity = 1;
        public List<string> Tags = new List<string>();
        public int DefinitionId;
        
        // In VS004, this will link to specific modifier logic
    }
}
