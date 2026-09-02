using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// In-game sprite stats overlay for the Crowd GPU sample — FPS / frame-ms
    /// inside the player loop, live sprite count, and spawn/despawn buttons.
    /// Self-bootstraps on CrowdGpu / Spawner / Soldier / authoring scenes only.
    /// Right-click the panel header to hide.
    /// </summary>
    public sealed class SpriteStatsHud : MonoBehaviour
    {
        public static SpriteStatsHud Instance;

        public float Fps;
        public float FrameMs;
        public float WorstMs;
        public int Sprites;
        public bool Show = true;

        const float Window = 0.5f;

        EntityManager em;
        World cachedWorld;
        bool queryReady;
        EntityQuery qSprites;

        float acc; int frames; float worst5 = 0.016f; float worstResetAt;
        Vector2 scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            if (!IsCrowdSampleScene())
                return;

            if (Instance != null)
                Destroy(Instance.gameObject);
            var go = new GameObject("SpriteStatsHud");
            DontDestroyOnLoad(go);
            go.AddComponent<SpriteStatsHud>();
        }

        static bool IsCrowdSampleScene()
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(scene.name))
                return false;
            // Positive allow-list so Events / Playback / Collider / Showcase stay clean.
            return scene.name.IndexOf("Crowd", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || scene.name.IndexOf("Spawner", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || scene.name.IndexOf("Soldier", System.StringComparison.OrdinalIgnoreCase) >= 0
                   || scene.name.IndexOf("authoring", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 0;
            QualitySettings.vSyncCount = 0;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            acc += dt; frames++;
            if (dt > worst5) worst5 = dt;
            if (Time.unscaledTime >= worstResetAt)
            {
                worstResetAt = Time.unscaledTime + 5f;
                worst5 = dt;
            }
            if (acc >= Window)
            {
                Fps = frames / acc;
                FrameMs = 1000f * acc / math.max(1, frames);
                WorstMs = worst5 * 1000f;
                acc = 0f; frames = 0;
                Refresh();
            }
        }

        void EnsureWorld()
        {
            var w = World.DefaultGameObjectInjectionWorld;
            if (w == null || !w.IsCreated)
            {
                cachedWorld = null;
                queryReady = false;
                return;
            }
            if (w != cachedWorld)
            {
                cachedWorld = w;
                em = w.EntityManager;
                queryReady = false;
            }
        }

        void EnsureQuery()
        {
            if (queryReady) return;
            qSprites = em.CreateEntityQuery(ComponentType.ReadOnly<SpriteAnimFrame>());
            queryReady = true;
        }

        void Refresh()
        {
            try
            {
                EnsureWorld();
                if (cachedWorld == null || !cachedWorld.IsCreated) { Sprites = 0; return; }
                EnsureQuery();
                Sprites = qSprites.CalculateEntityCount();
            }
            catch { Sprites = 0; }
        }

        void Change(int n)
        {
            try
            {
                EnsureWorld();
                if (cachedWorld == null || !cachedWorld.IsCreated || n == 0) return;

                if (n > 0)
                {
                    var center = SpriteBatchSpawner.LayoutXy
                        ? float3.zero
                        : new float3(0, 1.55f, 0);
                    SpriteBatchSpawner.SpawnNow(em, center,
                        4000f, 2f, n, true, true);

                    var qg = em.CreateEntityQuery(
                        ComponentType.ReadOnly<SpriteGpuDriven>());
                    if (!qg.IsEmpty)
                        SpriteGpuAnimSwitch.AllToGpu(em, Time.unscaledTime);
                    return;
                }

                EnsureQuery();
                var protoQ = em.CreateEntityQuery(typeof(SpriteSpawnPrototype));
                Entity proto = protoQ.IsEmpty
                    ? Entity.Null
                    : em.GetComponentData<SpriteSpawnPrototype>(protoQ.GetSingletonEntity()).Value;

                var ents = qSprites.ToEntityArray(Allocator.Temp);
                int kill = math.min(-n, ents.Length);
                var ecb = new EntityCommandBuffer(Allocator.Temp);
                int done = 0;
                for (int i = ents.Length - 1; i >= 0 && done < kill; i--)
                {
                    if (ents[i] == proto) continue;
                    ecb.DestroyEntity(ents[i]);
                    done++;
                }
                ecb.Playback(em);
                ecb.Dispose();
                ents.Dispose();
            }
            catch { /* world tearing down */ }
        }

        void OnGUI()
        {
            if (!Show) return;
            var box = new Rect(8, 8, 268, 268);

            var hdr = new Rect(box.x, box.y, box.width, 22);
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 1 && hdr.Contains(Event.current.mousePosition))
            {
                Show = false;
                Event.current.Use();
                return;
            }

            GUI.Box(box, "Crowd GPU Stats");
            GUILayout.BeginArea(new Rect(box.x + 8, box.y + 24, box.width - 16, box.height - 30));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(string.Format(
                "FPS {0:F0}   frame {1:F2} ms", Fps, FrameMs));
            GUILayout.Label(string.Format("worst {0:F1} ms", WorstMs));
            GUILayout.Label(string.Format("sprites {0:N0}", Sprites));
            GUILayout.Space(2);
            GUILayout.Label("Spawn count buttons below.");
            GUILayout.Label("GPU: Crowd Spawner UseGpuAnim.");
            GUILayout.Label("Clips: 1-9 / 0 / [ ]");
            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+10k")) Change(10_000);
            if (GUILayout.Button("-10k")) Change(-10_000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+100k")) Change(100_000);
            if (GUILayout.Button("-100k")) Change(-100_000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+500k")) Change(500_000);
            if (GUILayout.Button("-500k")) Change(-500_000);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("+1M")) Change(1_000_000);
            if (GUILayout.Button("-1M")) Change(-1_000_000);
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
    }
}