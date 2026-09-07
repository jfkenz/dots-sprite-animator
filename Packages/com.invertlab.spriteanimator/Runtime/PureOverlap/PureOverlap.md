# Pure Overlap (query-only collider detection)

Pure DOTS collider detection: baked hitbox data + rectangle math, no
physics engine, no spawned colliders. Kept as an alternative detection
backend — planned for promotion to a full workflow in a future version.

- SpriteHitboxQuery.cs — world AABB queries over baked hitbox data
