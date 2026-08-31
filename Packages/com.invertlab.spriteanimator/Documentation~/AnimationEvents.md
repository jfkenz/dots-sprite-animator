# Animation events

Frame events are authored in **Window > DOTS Sprite Animator**. Each marker has a
byte ID, an exact in-frame timestamp, optional payload, and a fire mode.

**EventMarkers** on the clip is the source of truth. Multiple markers may share
one frame (footstep + land). `EventIds[]` still stores the first marker on each
frame so older profiles and GPU eligibility keep working.

Existing profiles migrate to one marker per non-zero `EventIds` entry, at the
stored in-frame time (default `0.0`).

## Fire mode

| Mode | When it fires |
| --- | --- |
| **Loop** (default) | Every time playback crosses the marker, including clip wraps. |
| **Once** | Until the clip changes or `SpriteAnims.Play()` restarts it. |

## Payload

Each marker has a list of up to 8 values. A row is optional **name**, **type**,
value, and ×. Types are Unity.Mathematics gameplay values: Int / Int2 / Int3 /
Int4, Float / Float2 / Float3 / Float4, Byte, Bool, Color (`float4`), Text, and
Half (stored as `float`; cast with `new half(payload.Floats.x)`). There is no
float8 in Unity.Mathematics, so that is not a payload type.

`SpriteAnimEventBuffer.Payloads` holds every row. Vectors live on `Ints` /
`Floats`. For simple receivers, the first Int / Float / Text still copy onto
`IntPayload`, `FloatPayload`, and `TextHash`.

```csharp
evt.TryNamed(SpriteAnims.Fnv("knockback"), out var knock);
float2 dir = knock.Float2;
```

A C# struct cannot live in the blob. Name the rows (`damage`, `knockback`) and build the
struct in the receiver from `TryNamed`.

**Asset** stores a project object (ScriptableObject, AudioClip, TextAsset). Runtime gets
`TextHash` of the GUID and `NameHash` of the row name. Keep a catalog keyed by those
hashes — the blob cannot hold a UnityEngine.Object.

```csharp
evt.TryNamed(SpriteAnims.Fnv("sfx"), out var sfx);
if (_clips.TryGetValue(sfx.TextHash, out var clip))
    Play(clip);
```

Clips cap at 64 baked event keys.

## Pure ECS receiver

Run receivers after `SpriteAnimPlayerSystem`. Only entities with events are
matched because `SpriteAnimEventsPending` is enableable.

```csharp
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpriteAnimPlayerSystem))]
public partial struct FootstepSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        foreach (var (events, entity) in
                 SystemAPI.Query<DynamicBuffer<SpriteAnimEventBuffer>>()
                          .WithAll<SpriteAnimEventsPending>()
                          .WithEntityAccess())
        {
            for (int i = 0; i < events.Length; i++)
            {
                var evt = events[i];
                Handle(entity, evt.Id, evt.ClipIndex, evt.FrameIndex, evt.Payloads);
            }
        }
    }
}
```

Events remain readable through the rest of the simulation tick. The package
clears them immediately before animation playback on the next tick.

## Managed receiver

MonoBehaviours and other managed systems can subscribe to the bridge:

```csharp
void OnEnable()  => SpriteAnimEvents.Raised += OnAnimationEvent;
void OnDisable() => SpriteAnimEvents.Raised -= OnAnimationEvent;

void OnAnimationEvent(Entity entity, SpriteAnimEventBuffer evt)
{
    if (evt.Id == 1)
        PlayFootstep(entity);
}
```

Programmatically-created animator entities can call
`SpriteAnimEvents.Ensure(entityManager, entity)`. Authoring and factory paths
install the required components automatically.

Clips with any event markers stay on the CPU player. The GPU clock cannot emit
them.
