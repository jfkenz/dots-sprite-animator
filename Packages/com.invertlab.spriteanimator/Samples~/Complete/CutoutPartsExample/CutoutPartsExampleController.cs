using InvertLab.Sprites.DOTS;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Samples
{
    /// <summary>
    /// Sample entry for Package Manager Complete import.
    /// Prefer Tools/DOTS Sprite Animator/Create Parts Demo Scene for one-click setup.
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Samples/Cutout Parts Example")]
    public sealed class CutoutPartsExampleController : MonoBehaviour
    {
        void Awake()
        {
            if (GetComponent<CutoutPartsDemoBootstrap>() == null)
                gameObject.AddComponent<CutoutPartsDemoBootstrap>();
        }
    }
}
