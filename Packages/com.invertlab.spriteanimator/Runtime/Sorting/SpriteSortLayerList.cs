using System;
using System.Collections.Generic;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>One named sorting layer entry (name → baked int layer).</summary>
    [Serializable]
    public struct SpriteSortLayerEntry
    {
        public string Name;
        public int Index;

        public SpriteSortLayerEntry(string name, int index)
        {
            Name = name;
            Index = index;
        }
    }

    /// <summary>
    /// Project-wide named sorting layers (the DOTS analogue of Unity's
    /// Tags and Layers list). Create via Assets → Create → DOTS Sprite
    /// Animator → Sort Layers; the first asset in the project drives the
    /// Sprite Sort Authoring dropdown. Runtime only ever sees baked ints —
    /// names resolve to ints at bake time.
    /// </summary>
    [CreateAssetMenu(menuName = "DOTS Sprite Animator/Sort Layers", fileName = "SpriteSortLayers")]
    public class SpriteSortLayerList : ScriptableObject
    {
        public List<SpriteSortLayerEntry> Layers = new()
        {
            new SpriteSortLayerEntry("Background", -5),
            new SpriteSortLayerEntry("Ground", -4),
            new SpriteSortLayerEntry("Pickups", -1),
            new SpriteSortLayerEntry("Default", 0),
            new SpriteSortLayerEntry("Enemies", 1),
            new SpriteSortLayerEntry("Player", 2),
            new SpriteSortLayerEntry("Weapons", 3),
            new SpriteSortLayerEntry("VFX", 4),
        };

        public const string DefaultAssetName = "SpriteSortLayers";

        public int IndexOf(string name)
        {
            for (int i = 0; i < Layers.Count; i++)
            {
                if (string.Equals(Layers[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return Layers[i].Index;
            }
            return 0;
        }
    }
}
