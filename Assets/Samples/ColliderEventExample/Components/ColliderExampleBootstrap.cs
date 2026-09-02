using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Ensures BakeUnityColliders / BakeFrameColliders / BakeUnitySockets are on for collider demos.
    /// Also strips CrowdStress SpriteStatsHud so the demo stays uncluttered.
    /// </summary>
    public sealed class ColliderExampleBootstrap : MonoBehaviour
    {
        public bool BakeUnityColliders = true;
        public bool BakeFrameColliders = true;
        public bool BakeUnitySockets = true;

        void Awake()
        {
            DestroySpriteStatsHud();
            ApplyEnemyDefaults();
            ApplyBakeFlags();
        }

        void Start()
        {
            // HUD bootstraps AfterSceneLoad; catch anything that appeared after Awake.
            DestroySpriteStatsHud();
        }

        void ApplyBakeFlags()
        {
            var sets = FindObjectsByType<SpriteAnimSetAuthoring>(FindObjectsSortMode.None);
            for (int i = 0; i < sets.Length; i++)
            {
                var set = sets[i];
                if (set == null)
                    continue;
                set.BakeUnityColliders = BakeUnityColliders;
                set.BakeFrameColliders = BakeFrameColliders;
                set.BakeUnitySockets = BakeUnitySockets;
                if (Application.isPlaying)
                {
                    set.SyncUnityColliders();
                    set.SyncUnitySockets();
                }
            }
        }

        void ApplyEnemyDefaults()
        {
            var enemies = FindObjectsByType<ColliderExampleEnemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                var enemy = enemies[i];
                if (enemy == null)
                    continue;
                if (string.IsNullOrWhiteSpace(enemy.HealthBarSocketName))
                    enemy.HealthBarSocketName = "HealthBarSocket";
            }
        }

        static void DestroySpriteStatsHud()
        {
            if (SpriteStatsHud.Instance != null)
            {
                Destroy(SpriteStatsHud.Instance.gameObject);
                SpriteStatsHud.Instance = null;
            }

            var named = GameObject.Find("SpriteStatsHud");
            if (named != null)
                Destroy(named);
        }
    }
}
