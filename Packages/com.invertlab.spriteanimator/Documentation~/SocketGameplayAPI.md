# Socket Gameplay API

Socket `Name` is an editor label. `ID` is the stable gameplay contract and is
shared by Frame-Attached and Independent Motion sockets. Prefer dotted IDs:

- `equipment.head`
- `combat.muzzle`
- `effect.orbit_01`

## Attach a helmet in authoring

Add `Sprite Socket Attachment` to a direct child of the animated player and set
Socket ID to `equipment.head`. The baker resolves the stable ID; renaming the
socket's display label does not break the attachment.

## Fetch a muzzle from a custom system

```csharp
static readonly ulong MuzzleId = SpriteSockets.Hash("combat.muzzle");

foreach (var (sockets, localToWorld) in
         SystemAPI.Query<DynamicBuffer<SpriteSocketBuffer>, RefRO<LocalToWorld>>())
{
    if (!SpriteSockets.TryGetWorldPose(
            sockets, MuzzleId, localToWorld.ValueRO, out var muzzle))
        continue;

    // Spawn a projectile at muzzle.Position with muzzle.Rotation.
}
```

For a Frame-Attached attack, consume the existing `SpriteAnimEventBuffer` and
look up `combat.muzzle` when the fire event arrives.

## Consume Independent Motion triggers

Right-click the top half of an Independent Motion timeline row to add a trigger.
The marker references an Event ID from the profile event list.

```csharp
foreach (var (events, sockets, localToWorld) in
         SystemAPI.Query<
             DynamicBuffer<SpriteSocketEventBuffer>,
             DynamicBuffer<SpriteSocketBuffer>,
             RefRO<LocalToWorld>>()
         .WithAll<SpriteSocketEventsPending>())
{
    for (int i = 0; i < events.Length; i++)
    {
        var trigger = events[i];
        if (trigger.EventId != FireballEventId)
            continue;
        if (!SpriteSockets.TryGetWorldPose(
                sockets, trigger.SocketIdHash, localToWorld.ValueRO, out var origin))
            continue;

        // Spawn the fireball at origin.Position with origin.Rotation.
    }
}
```

Managed code can subscribe to `SpriteSocketEvents.Raised`, or call
`SpriteSockets.TryGetPose(EntityManager, entity, "combat.muzzle", out pose)`.
