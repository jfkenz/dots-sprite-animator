using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Pure variant of ColliderExampleBootstrap: forces every collider
    /// authoring to the Query method (pure DOTS — no Unity 2D colliders,
    /// no Rigidbody2D, no physics package). Detection happens in gameplay
    /// code via SpriteHitboxQuery bounds overlap.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PureColliderExampleBootstrap : MonoBehaviour
    {
        void Awake()
        {
            var authors = FindObjectsByType<SpriteColliderAuthoring>(FindObjectsSortMode.None);
            for (int i = 0; i < authors.Length; i++)
            {
                if (authors[i] == null)
                    continue;
                authors[i].Method = SpriteColliderMethod.Query;
                authors[i].ShowSceneGizmos = true;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(authors[i]);
#endif
            }

            // belt-and-suspenders: no Unity 2D collider spawning anywhere
            var sets = FindObjectsByType<SpriteAnimSetAuthoring>(FindObjectsSortMode.None);
            for (int i = 0; i < sets.Length; i++)
            {
                if (sets[i] == null)
                    continue;
                sets[i].BakeUnityColliders = false;
                sets[i].BakeFrameColliders = false;
                sets[i].BakeUnitySockets = true; // sockets are anchors, not colliders
                if (Application.isPlaying)
                    sets[i].SyncUnitySockets();
            }
        }
    }
}
