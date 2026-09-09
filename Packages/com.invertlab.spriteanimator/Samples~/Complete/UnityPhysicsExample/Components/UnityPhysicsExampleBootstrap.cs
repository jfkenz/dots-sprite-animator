using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Unity Physics example bootstrap: forces UnityPhysics method flags,
    /// enables socket bake for health bars, and ensures each enemy has a
    /// runtime Physics hurtbox (open-scene GOs are not SubScene-baked).
    /// </summary>
    [DisallowMultipleComponent]
    [ExecuteAlways]
    public sealed class UnityPhysicsExampleBootstrap : MonoBehaviour
    {
        public static UnityPhysicsExampleBootstrap Instance;

        public bool BakeUnitySockets = true;

        void Awake()
        {
            Instance = this;
            ApplyColliderMethod();
            ApplyBakeFlags();
            ApplyEnemyDefaults();
        }

        void Start()
        {
            ApplyBakeFlags();
            ApplyEnemyDefaults();
            EnsureEnemyHurtboxes();
        }

        void Update()
        {
            if (!Application.isPlaying)
                return;
            // one cheap pass until every enemy has a body (world may spin up late)
            if (!UnityPhysicsExampleEnemy.AnyColliderAttached())
                EnsureEnemyHurtboxes();
        }

        void ApplyColliderMethod()
        {
            var authors = FindObjectsByType<SpriteColliderAuthoring>(FindObjectsSortMode.None);
            for (int i = 0; i < authors.Length; i++)
            {
                if (authors[i] == null)
                    continue;
                authors[i].Method = SpriteColliderMethod.UnityPhysics;
                authors[i].Scope = SpriteColliderScope.Auto;
#if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(authors[i]);
                if (!Application.isPlaying)
                    authors[i].ApplyToAnimSet();
#endif
            }
        }

        void ApplyBakeFlags()
        {
            var sets = FindObjectsByType<SpriteAnimSetAuthoring>(FindObjectsSortMode.None);
            for (int i = 0; i < sets.Length; i++)
            {
                var set = sets[i];
                if (set == null)
                    continue;
                set.BakeUnitySockets = BakeUnitySockets;
                if (Application.isPlaying)
                    set.SyncUnitySockets();
            }
        }

        void ApplyEnemyDefaults()
        {
            var enemies = FindObjectsByType<UnityPhysicsExampleEnemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
            {
                var enemy = enemies[i];
                if (enemy == null)
                    continue;
                if (string.IsNullOrWhiteSpace(enemy.HealthBarSocketName))
                    enemy.HealthBarSocketName = "HealthBarSocket";
            }
        }

        void EnsureEnemyHurtboxes()
        {
            var enemies = FindObjectsByType<UnityPhysicsExampleEnemy>(FindObjectsSortMode.None);
            for (int i = 0; i < enemies.Length; i++)
                enemies[i]?.EnsurePhysicsHurtbox();
        }
    }
}
