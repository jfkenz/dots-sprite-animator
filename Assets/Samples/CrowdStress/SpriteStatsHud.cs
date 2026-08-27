using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace BallForge.Sprites.DOTS
{
    /// <summary>
    /// In-game sprite stats overlay — the authoritative way to find the spawn
    /// ceiling. Measures FPS / frame-ms INSIDE the player loop (CLI timestamp
    /// sampling is unreliable: editor gameview pacing pollutes it), shows the
    /// live sprite count, and spawns/despawns via on-screen buttons.
    ///
    /// Deliberately mouse-only (no Input System reference needed) so it stays
    /// self-contained inside the runtime asmdef's existing dependencies.
    /// Self-bootstraps every play session; H-free, toggle via right-click on
    /// the panel header.
    /// </summary>
    public sealed class SpriteStatsHud : MonoBehaviour
    {
        public static SpriteStatsHud Instance;

        // ---- measured inside the game loop (authoritative) ----
        public float Fps;      // smoothed over the sample window
        public float FrameMs;  // average frame ms over the window
        public float WorstMs;  // worst single frame in the last 5 s

        // ---- live world stats ----
        public int Sprites;   // total animated sprite entities
        public bool Show = true;

        const float Window = 0.5f; // fps averaging window (seconds)

        EntityManager em;
        World cachedWorld;
        bool queryReady;
        EntityQuery qSprites;

        float acc; int frames; float worst5 = 0.016f; float worstResetAt;
        Vector2 scroll;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Bootstrap()
        {
            // domain reload may be off: an Instance from the LAST play session
            // survives as a destroyed husk — replace it unconditionally.
            if (Instance != null)
                Destroy(Instance.gameObject);
            var go = new GameObject("SpriteStatsHud");
            DontDestroyOnLoad(go);
            go.AddComponent<SpriteStatsHud>();
        }

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = 0; // uncapped; judge by frame ms
            QualitySettings.vSyncCount = 0;  // else editor caps at monitor Hz
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
            catch { Sprites = 0; } // world tearing down mid-frame
        }

        void Change(int n)
        {
            try
            {
                EnsureWorld();
                if (cachedWorld == null || !cachedWorld.IsCreated || n == 0) return;

                if (n > 0)
                {
                    // square grid formation around origin — dense block fully
                    // visible at normal zoom (random scatter spread sprites
                    // over an 8km field where you'd only ever see a few).
                    SpriteBatchSpawner.SpawnNow(em, new float3(0, 1.55f, 0),
                        4000f, 2f, n, true, true);

                    // keep the session's mode consistent: if anything is
                    // already GPU-driven, convert the fresh spawns too so a
                    // big +press doesn't silently reintroduce the CPU tax.
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
            var box = new Rect(8, 8, 250, 220);

            // right-click header toggles visibility
            var hdr = new Rect(box.x, box.y, box.width, 22);
            if (Event.current.type == EventType.MouseDown &&
                Event.current.button == 1 && hdr.Contains(Event.current.mousePosition))
            {
                Show = false;
                Event.current.Use();
                return;
            }

            GUI.Box(box, "Sprite Stats (in-game)");
            GUILayout.BeginArea(new Rect(box.x + 8, box.y + 24, box.width - 16, box.height - 30));
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label(string.Format(
                "FPS {0:F0}   frame {1:F2} ms", Fps, FrameMs));
            GUILayout.Label(string.Format("worst {0:F1} ms", WorstMs));
            GUILayout.Label(string.Format("sprites {0:N0}", Sprites));
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
