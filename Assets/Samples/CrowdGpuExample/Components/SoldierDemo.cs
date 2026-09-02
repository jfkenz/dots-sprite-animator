using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Scene component retained for Unity serialization. Runtime behavior is handled by
    /// <see cref="SoldierDemoInputSystem"/> and <see cref="SoldierDemoRuntime"/>.
    /// </summary>
    public sealed class SoldierDemo : MonoBehaviour { }

    /// <summary>Compatibility component for scenes serialized against the old script type.</summary>
    public sealed class SoldierTag : MonoBehaviour { }

    /// <summary>Every ECS soldier spawned by the demo.</summary>
    public struct SoldierEntityTag : IComponentData { }

    /// <summary>
    /// Example-scene driver:
    ///   1-4  switch ALL soldiers to Idle / Run / Attack / Block
    ///   5    spawn a square grid batch (BatchSize soldiers)
    ///   6    spawn the same amount at random positions
    /// Rendering is the GPU-instanced path: ONE draw call for the whole crowd.
    /// Sprites lie flat (RotateX -90); camera looks top-down (Euler 90,0,180).
    /// All state lives here as statics; the input system below just forwards keys.
    /// </summary>
    public static class SoldierDemoRuntime
    {
        public const int BatchSize = 400;     // soldiers per key-5/6 press
        public const float SizeUnits = 2f;    // world size of one sprite quad
        public const float HeightY = 1.55f;   // just above the chamber floor (y≈1.4)

        static Entity _proto;
        static bool _ready;
        static readonly string[] StateNames = { "Idle", "Run", "Attack", "Block" };

        public static bool Ready => _ready;

        /// <summary>
        /// Statics survive across Play sessions when "Enter Play Mode Options"
        /// disable domain reload — drop stale handles so EnsureProto rebuilds.
        /// </summary>
        public static void ResetForPlayMode()
        {
            _ready = false;
            _proto = Entity.Null;
        }

        public static void EnsureProto()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (_ready || world == null)
                return;

            var tex = Resources.Load<Texture2D>("Images/tes");
            if (tex == null)
            {
                Debug.LogError("[SoldierDemo] Images/tes not found under Resources");
                return;
            }

            const int cols = 4, rows = 4;
            var clips = new SpriteAnimSetBuilder.ClipInput[rows];
            for (int r = 0; r < rows; r++)
            {
                var idx = new int[cols];
                for (int c = 0; c < cols; c++) idx[c] = r * cols + c;
                clips[r] = new SpriteAnimSetBuilder.ClipInput
                {
                    Name = StateNames[r],
                    Loop = true,
                    FrameRate = 8f,
                    GlobalFrameIndices = idx,
                };
            }
            var (setRef, player) = SpriteAnimSetBuilder.Build(Allocator.Persistent, clips);

            var em = world.EntityManager;
            _proto = em.CreateEntity();
            em.AddComponentData(_proto, LocalTransform.FromPositionRotationScale(
                new float3(0, HeightY, 0),
                quaternion.RotateX(math.radians(-90f)), // lay flat for top-down camera
                SizeUnits));
            em.AddComponentData(_proto, setRef);
            em.AddComponentData(_proto, player);
            em.AddComponentData(_proto, new SpriteAnimFrame { Slot = 0 });
            em.AddComponentData(_proto, new SpriteTint { Value = new float4(1, 1, 1, 1) });
            em.AddComponentData(_proto, new SpriteAnimEnabled());
            em.AddBuffer<SpriteAnimEventBuffer>(_proto);
            em.AddComponent<SpriteAnimEventsPending>(_proto);
            em.SetComponentEnabled<SpriteAnimEventsPending>(_proto, false);
            em.AddComponent<SoldierEntityTag>(_proto);

            // ---- hand the crowd to the instanced renderer (one draw call) ----
            SpriteBatchSpawner.LayoutXy = false;
            SpriteInstanceRenderSystem.Install(em);
            SpriteInstanceRenderSystem.SetSheet(tex);
            SpriteInstanceRenderSystem.SetGrid(em, cols, rows,
                SpriteSheetProfile.GetCellAspect(tex, cols, rows));

            // bulk spawner clones this prototype
            SpriteBatchSpawner.SetPrototype(em, _proto);

            _ready = true;
            Debug.Log("[SoldierDemo] prototype ready — 5 = grid +" + BatchSize +
                      ", 6 = random, 1-4 = states [GPU-instanced]");
        }

        /// <summary>Switch every soldier to the named clip.</summary>
        public static void SetAllStates(string clipName)
        {
            if (!_ready) return;
            var hash = SpriteAnims.Fnv(clipName);
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;

            var q = em.CreateEntityQuery(
                new ComponentType[] {
                    ComponentType.ReadOnly<SoldierEntityTag>(),
                    ComponentType.ReadWrite<SpriteAnimPlayer>(),
                    ComponentType.ReadOnly<SpriteAnimSetRef>(),
                });
            var ents = q.ToEntityArray(Allocator.Temp);
            int changed = 0;

            for (int n = 0; n < ents.Length; n++)
            {
                var e = ents[n];
                ref var set = ref em.GetComponentData<SpriteAnimSetRef>(e).Set.Value;
                for (int i = 0; i < set.Clips.Length; i++)
                {
                    if (set.Clips[i].NameHash == hash)
                    {
                        var player = em.GetComponentData<SpriteAnimPlayer>(e);
                        player.ClipIndex = i;
                        player.Time = 0f;
                        player.Playing = 1;
                        em.SetComponentData(e, player);
                        if (em.HasComponent<SpriteAnimCompleted>(e))
                            em.RemoveComponent<SpriteAnimCompleted>(e);
                        changed++;
                        break;
                    }
                }
            }
            ents.Dispose();
            Debug.Log("[SoldierDemo] state '" + clipName + "' -> " + changed + " soldiers");
        }

        /// <summary>Spawn count soldiers; grid=true square formation, else random scatter.</summary>
        public static void Spawn(int count, bool grid)
        {
            if (!_ready) return;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            int spawned = SpriteBatchSpawner.SpawnNow(
                em,
                new float3(0, HeightY, 0),
                spread: 28f,
                scale: SizeUnits,
                count: count,
                grid: grid,
                randomizeClocks: true);

            int total = CountAll();
            Debug.Log("[SoldierDemo] +" + spawned + (grid ? " (grid)" : " (random)") +
                      " | total soldiers: " + total);
        }

        public static int CountAll()
        {
            if (!_ready) return 0;
            var em = World.DefaultGameObjectInjectionWorld.EntityManager;
            var cq = em.CreateEntityQuery(new ComponentType[] { ComponentType.ReadOnly<SoldierEntityTag>() });
            return cq.CalculateEntityCount();
        }
    }

    /// <summary>
    /// Reads number keys and drives the demo. Managed (not Burst) — input only;
    /// all per-frame work (animation, rendering) runs in Burst systems.
    /// NOTE: deliberately no RequireForUpdate — this system must run even in an
    /// empty world, because it is what creates the prototype and first batch.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial class SoldierDemoInputSystem : SystemBase
    {
        static bool _bootstrapped;

        // statics survive across Play sessions when domain reload is off
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            _bootstrapped = false;
            SoldierDemoRuntime.ResetForPlayMode();
        }

        protected override void OnUpdate()
        {
            SoldierDemoRuntime.EnsureProto();

            if (!_bootstrapped)
            {
                _bootstrapped = true;
                // something on screen the moment you press Play
                SoldierDemoRuntime.Spawn(SoldierDemoRuntime.BatchSize, grid: true);
            }

            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null)
                return;

            if (kb.digit1Key.wasPressedThisFrame) SoldierDemoRuntime.SetAllStates("Idle");
            if (kb.digit2Key.wasPressedThisFrame) SoldierDemoRuntime.SetAllStates("Run");
            if (kb.digit3Key.wasPressedThisFrame) SoldierDemoRuntime.SetAllStates("Attack");
            if (kb.digit4Key.wasPressedThisFrame) SoldierDemoRuntime.SetAllStates("Block");
            if (kb.digit5Key.wasPressedThisFrame) SoldierDemoRuntime.Spawn(SoldierDemoRuntime.BatchSize, grid: true);
            if (kb.digit6Key.wasPressedThisFrame) SoldierDemoRuntime.Spawn(SoldierDemoRuntime.BatchSize, grid: false);
        }
    }
}
