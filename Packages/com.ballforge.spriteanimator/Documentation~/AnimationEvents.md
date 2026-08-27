# Animation events

Frame events are authored as byte IDs with an exact in-frame timestamp in
**Window > DOTS Sprite Animator**. The player emits an event when playback
crosses that marker, including every marker crossed during a long update. Existing
profiles migrate to frame-start timing (`0.0`).

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
                Handle(entity, events[i].Id, events[i].ClipIndex, events[i].FrameIndex);
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
