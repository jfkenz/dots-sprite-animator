using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Parts-specific ECS controls. SpriteAnims dispatches here by player kind.</summary>
    public static partial class SpriteParts
    {
        public static bool IsPartsRoot(EntityManager em, Entity e)
            => em.Exists(e) && em.HasComponent<SpritePartsPlayer>(e) && em.HasComponent<SpritePartsSetRef>(e);

        public static bool IsPartsEntity(EntityManager em, Entity e)
            => IsPartsRoot(em, e) || (em.Exists(e) && em.HasComponent<SpritePartSlot>(e));

        /// <param name="crossfadeSeconds">Negative = the profile's mix table (pair, any, default); 0 = instant.</param>
        public static bool Play(EntityManager em, Entity e, string clipName, bool force = false,
            float crossfadeSeconds = -1f)
        {
            if (!IsPartsRoot(em, e))
                return false;
            var blob = em.GetComponentData<SpritePartsSetRef>(e).Set;
            if (!blob.IsCreated)
                return false;
            ref var set = ref blob.Value;
            int index = SpritePartsPlayback.FindClipIndexByName(ref set, clipName);
            if (index < 0)
                return false;
            return Play(em, e, index, force, crossfadeSeconds);
        }

        /// <param name="crossfadeSeconds">Negative = the profile's mix table (pair, any, default); 0 = instant.</param>
        public static bool Play(EntityManager em, Entity e, int clipIndex, bool force = false,
            float crossfadeSeconds = -1f)
        {
            if (!IsPartsRoot(em, e))
                return false;
            var blob = em.GetComponentData<SpritePartsSetRef>(e).Set;
            if (!blob.IsCreated)
                return false;
            ref var set = ref blob.Value;
            if (clipIndex < 0 || clipIndex >= set.Clips.Length)
                return false;

            var player = em.GetComponentData<SpritePartsPlayer>(e);
            bool completed = player.Completed != 0 || em.HasComponent<SpritePartsCompleted>(e);
            if (!force && !BlendActive(em, e))
            {
                // Same running clip: idempotent.
                if (player.Playing != 0 && !completed && player.ClipIndex == clipIndex)
                    return true;
                // Same paused incomplete clip: resume.
                if (player.Playing == 0 && !completed && player.ClipIndex == clipIndex)
                {
                    player.Playing = 1;
                    player.Paused = 0;
                    em.SetComponentData(e, player);
                    return true;
                }
            }

            ClearQueue(em, e);
            StartClip(em, e, clipIndex, crossfadeSeconds, -1, 0f);
            SpritePartsPoseUtility.ApplyPose(em, e);
            return true;
        }

        public static void Pause(EntityManager em, Entity e)
        {
            if (!IsPartsRoot(em, e)) return;
            var player = em.GetComponentData<SpritePartsPlayer>(e);
            player.Playing = 0;
            player.Paused = 1;
            em.SetComponentData(e, player);
        }

        public static void Resume(EntityManager em, Entity e)
        {
            if (!IsPartsRoot(em, e)) return;
            var player = em.GetComponentData<SpritePartsPlayer>(e);
            player.Paused = 0;
            // Completion stops the incoming clip, but its crossfade may still
            // need to finish. Resume that fade without restarting the clip.
            if (player.Completed == 0 && !em.HasComponent<SpritePartsCompleted>(e))
                player.Playing = 1;
            em.SetComponentData(e, player);
        }

        public static void Stop(EntityManager em, Entity e)
        {
            if (!IsPartsRoot(em, e)) return;
            var player = em.GetComponentData<SpritePartsPlayer>(e);
            player.Playing = 0;
            player.TimeSeconds = 0f;
            player.Completed = 0;
            player.Paused = 1;
            player.PlayedSeconds = 0f;
            ClearFades(em, e, ref player);
            em.SetComponentData(e, player);
            if (em.HasComponent<SpritePartsCompleted>(e))
                em.RemoveComponent<SpritePartsCompleted>(e);
            SpritePartsPoseUtility.ApplyPose(em, e);
        }

        public static void Restart(EntityManager em, Entity e)
        {
            if (!IsPartsRoot(em, e)) return;
            var player = em.GetComponentData<SpritePartsPlayer>(e);
            player.TimeSeconds = 0f;
            player.Playing = 1;
            player.Completed = 0;
            player.Paused = 0;
            player.PlayedSeconds = 0f;
            ClearFades(em, e, ref player);
            em.SetComponentData(e, player);
            if (em.HasComponent<SpritePartsCompleted>(e))
                em.RemoveComponent<SpritePartsCompleted>(e);
            SpritePartsPoseUtility.ApplyPose(em, e);
        }

        public static void SetSpeed(EntityManager em, Entity e, float speed)
        {
            if (!IsPartsRoot(em, e)) return;
            var player = em.GetComponentData<SpritePartsPlayer>(e);
            player.SpeedMultiplier = math.isfinite(speed) ? speed : 0f;
            em.SetComponentData(e, player);
        }

        public static float GetSpeed(EntityManager em, Entity e)
            => IsPartsRoot(em, e) ? em.GetComponentData<SpritePartsPlayer>(e).SpeedMultiplier : 0f;

        public static void SeekNormalized(EntityManager em, Entity e, float t01)
        {
            if (!IsPartsRoot(em, e)) return;
            var blob = em.GetComponentData<SpritePartsSetRef>(e).Set;
            if (!blob.IsCreated) return;
            var player = em.GetComponentData<SpritePartsPlayer>(e);
            ref var set = ref blob.Value;
            if (player.ClipIndex < 0 || player.ClipIndex >= set.Clips.Length)
                return;
            float duration = set.Clips[player.ClipIndex].Duration;
            player.TimeSeconds = SpritePartsPlayback.SeekNormalized(t01, duration);
            // Seek does not restart, complete, or clear completion.
            em.SetComponentData(e, player);
            SpritePartsPoseUtility.ApplyPose(em, e);
        }

        public static void SetFacing(EntityManager em, Entity e, bool flipX)
        {
            if (!IsPartsRoot(em, e)) return;
            var facing = em.HasComponent<SpritePartsFacing>(e)
                ? em.GetComponentData<SpritePartsFacing>(e)
                : default;
            facing.FlipX = flipX ? (byte)1 : (byte)0;
            if (!em.HasComponent<SpritePartsFacing>(e))
                em.AddComponentData(e, facing);
            else
                em.SetComponentData(e, facing);
            SpritePartsPoseUtility.ApplyFacing(em, e);
        }

        public static void SetFlip(EntityManager em, Entity e, bool flipX, bool flipY)
        {
            if (!IsPartsRoot(em, e)) return;
            var facing = new SpritePartsFacing
            {
                FlipX = flipX ? (byte)1 : (byte)0,
                FlipY = flipY ? (byte)1 : (byte)0,
            };
            if (!em.HasComponent<SpritePartsFacing>(e))
                em.AddComponentData(e, facing);
            else
                em.SetComponentData(e, facing);
            SpritePartsPoseUtility.ApplyFacing(em, e);
        }

        public static bool TryGetFlip(EntityManager em, Entity e, out bool flipX, out bool flipY)
        {
            flipX = flipY = false;
            if (!IsPartsRoot(em, e) || !em.HasComponent<SpritePartsFacing>(e))
                return false;
            var facing = em.GetComponentData<SpritePartsFacing>(e);
            flipX = facing.FlipX != 0;
            flipY = facing.FlipY != 0;
            return true;
        }

        public static bool TryGetSlot(EntityManager em, Entity root, string slotId, out Entity part)
        {
            part = Entity.Null;
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartLink>(root))
                return false;
            string id = SpritePartIdUtility.Canonical(slotId);
            ulong hash = SpritePartIdUtility.Hash(id);
            var links = em.GetBuffer<SpritePartLink>(root);
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].SlotIdHash == hash)
                {
                    part = links[i].Part;
                    return part != Entity.Null && em.Exists(part);
                }
            }
            return false;
        }

        public static void SetSpriteSwitch(EntityManager em, Entity root, SpritePartsSpriteSwitch rule)
        {
            if (!IsPartsRoot(em, root)) return;
            var player = em.GetComponentData<SpritePartsPlayer>(root);
            player.SpriteSwitch = (byte)rule;
            em.SetComponentData(root, player);
        }

        public static void SetOverride(EntityManager em, Entity root, in SpritePartsPoseOverride value)
        {
            if (!IsPartsRoot(em, root)) return;
            SpritePartsPoseWriter.EnsureBuffers(em, root);
            var buf = em.GetBuffer<SpritePartsPoseOverride>(root);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].Id == value.Id)
                {
                    buf[i] = value;
                    return;
                }
            }
            buf.Add(value);
        }

        public static void ClearOverride(EntityManager em, Entity root, int id)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartsPoseOverride>(root))
                return;
            var buf = em.GetBuffer<SpritePartsPoseOverride>(root);
            for (int i = buf.Length - 1; i >= 0; i--)
            {
                if (buf[i].Id == id)
                    buf.RemoveAt(i);
            }
        }

        public static void ClearOverrides(EntityManager em, Entity root)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartsPoseOverride>(root))
                return;
            em.GetBuffer<SpritePartsPoseOverride>(root).Clear();
        }

        public static void SetLayer(EntityManager em, Entity root, int clipIndex, float weight, uint slotMask = 0)
        {
            if (!IsPartsRoot(em, root)) return;
            SpritePartsPoseWriter.EnsureBuffers(em, root);
            var buf = em.GetBuffer<SpritePartsAnimLayer>(root);
            var layer = new SpritePartsAnimLayer
            {
                ClipIndex = clipIndex,
                Weight = math.saturate(weight),
                SlotMask = slotMask,
            };
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].ClipIndex == clipIndex)
                {
                    buf[i] = layer;
                    return;
                }
            }
            buf.Add(layer);
        }

        public static void ClearLayer(EntityManager em, Entity root, int clipIndex)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartsAnimLayer>(root))
                return;
            var buf = em.GetBuffer<SpritePartsAnimLayer>(root);
            for (int i = buf.Length - 1; i >= 0; i--)
            {
                if (buf[i].ClipIndex == clipIndex)
                    buf.RemoveAt(i);
            }
        }

        public static void ClearLayers(EntityManager em, Entity root)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartsAnimLayer>(root))
                return;
            em.GetBuffer<SpritePartsAnimLayer>(root).Clear();
        }

        public static bool BindSocket(EntityManager em, Entity root, string socketId, string slotId,
            float2 localOffset, float localRotationDeg = 0f)
        {
            if (!TryGetSlot(em, root, slotId, out _))
                return false;
            SpritePartsPoseWriter.EnsureBuffers(em, root);
            ulong hash = SpritePartIdUtility.Hash(SpritePartIdUtility.Canonical(socketId, "socket"));
            int slot = SlotIndex(em, root, slotId);
            var buf = em.GetBuffer<SpritePartSocketBinding>(root);
            var binding = new SpritePartSocketBinding
            {
                SocketIdHash = hash,
                SlotIndex = slot,
                LocalOffset = localOffset,
                LocalRotation = localRotationDeg,
            };
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SocketIdHash == hash)
                {
                    buf[i] = binding;
                    return true;
                }
            }
            buf.Add(binding);
            return true;
        }

        public static bool BindHitbox(EntityManager em, Entity root, string slotId,
            float2 localCenter, float2 localSize, float localRotationDeg = 0f, byte kind = 0)
        {
            if (!TryGetSlot(em, root, slotId, out _))
                return false;
            SpritePartsPoseWriter.EnsureBuffers(em, root);
            em.GetBuffer<SpritePartHitboxBinding>(root).Add(new SpritePartHitboxBinding
            {
                SlotIndex = SlotIndex(em, root, slotId),
                LocalCenter = localCenter,
                LocalSize = localSize,
                LocalRotation = localRotationDeg,
                Kind = kind,
            });
            return true;
        }

        public static bool TryGetSocketWorld(EntityManager em, Entity root, string socketId,
            out float2 worldPosition, out float worldRotationDeg)
        {
            worldPosition = float2.zero;
            worldRotationDeg = 0f;
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartSocketWorld>(root))
                return false;
            ulong hash = SpritePartIdUtility.Hash(SpritePartIdUtility.Canonical(socketId, "socket"));
            var buf = em.GetBuffer<SpritePartSocketWorld>(root);
            for (int i = 0; i < buf.Length; i++)
            {
                if (buf[i].SocketIdHash != hash)
                    continue;
                worldPosition = buf[i].WorldPosition;
                worldRotationDeg = buf[i].WorldRotation;
                return true;
            }
            return false;
        }

        static int SlotIndex(EntityManager em, Entity root, string slotId)
        {
            ulong hash = SpritePartIdUtility.Hash(SpritePartIdUtility.Canonical(slotId));
            var links = em.GetBuffer<SpritePartLink>(root);
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].SlotIdHash == hash)
                    return links[i].SlotIndex;
            }
            return -1;
        }

        /// <summary>
        /// Friendly per-slot appearance swap by baked AppearanceId.
        /// Changes sheet/cell + derived visual geometry ONLY — never joint TRS, keys, time, clip, or speed.
        /// </summary>
        public static bool SetSlotAppearance(EntityManager em, Entity root, string slotId, string appearanceId)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartLink>(root))
                return false;
            var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
            if (!blob.IsCreated)
                return false;
            ref var set = ref blob.Value;
            int appIndex = SpritePartsPlayback.FindAppearanceIndex(ref set, appearanceId);
            if (appIndex < 0)
                return false;
            if (!TryGetSlot(em, root, slotId, out Entity part))
                return false;
            ref var app = ref set.Appearances[appIndex];
            if (!IsAppearanceGeometryValid(ref app))
                return false;

            ApplyAppearanceBlob(em, root, part, appIndex, ref app, 0, default, false);
            return true;
        }

        /// <summary>
        /// Low-level sheet swap with explicit geometry metadata. Rejects missing/invalid metadata.
        /// Updates sheet binding + derived frame offset/scale only — never joint TRS or playback.
        /// </summary>
        public static bool SetSlotSheet(
            EntityManager em,
            Entity root,
            string slotId,
            Entity sheetEntity,
            int cellIndex,
            float2 logicalWorldSize,
            float2 pivot)
        {
            if (!IsPartsRoot(em, root))
                return false;
            if (!TryGetSlot(em, root, slotId, out Entity part))
                return false;
            if (!SpritePartsGeometry.TryResolveExplicit(logicalWorldSize, pivot, sheetIndex: -1, cellIndex,
                    out var geo, out _))
                return false;

            int sheetTable = ResolveOrRegisterSheet(em, root, sheetEntity);
            var state = em.HasComponent<SpritePartAppearanceState>(part)
                ? em.GetComponentData<SpritePartAppearanceState>(part)
                : default;
            state.AppearanceIndex = -1; // ad-hoc / not a baked appearance id
            state.SheetTableIndex = sheetTable;
            state.CellIndex = geo.CellIndex;
            state.LogicalWorldSize = geo.LogicalWorldSize;
            state.Pivot = geo.Pivot;
            state.FrameOffset = geo.FrameOffset;
            state.FrameScale = geo.FrameScale;
            if (!em.HasComponent<SpritePartAppearanceState>(part))
                em.AddComponentData(part, state);
            else
                em.SetComponentData(part, state);

            WriteFrame(em, part, geo.CellIndex, geo.FrameOffset, geo.FrameScale, default, false);
            WriteSheetBinding(em, part, sheetEntity, default, false);
            return true;
        }

        /// <summary>
        /// Named skin patch. Unknown skin fails with no changes. Unlisted slots unchanged.
        /// Validates every applicable appearance before any write (atomic fail).
        /// </summary>
        public static bool ApplySkin(EntityManager em, Entity root, string skinId)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartLink>(root))
                return false;
            var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
            if (!blob.IsCreated)
                return false;
            ref var set = ref blob.Value;
            int skinIndex = SpritePartsPlayback.FindSkinIndex(ref set, skinId);
            if (skinIndex < 0)
                return false;

            ref var skin = ref set.SkinPatches[skinIndex];
            var links = em.GetBuffer<SpritePartLink>(root);

            // Validate all applicable bindings first (atomic).
            for (int b = 0; b < skin.Bindings.Length; b++)
            {
                int slotIndex = skin.Bindings[b].SlotIndex;
                int appIndex = skin.Bindings[b].AppearanceIndex;
                if (slotIndex < 0 || slotIndex >= set.Slots.Length)
                    continue; // not applicable to this set
                if (!TryFindLinkPart(links, slotIndex, out _))
                    continue; // slot not present on this instance
                if (appIndex < 0 || appIndex >= set.Appearances.Length)
                    return false;
                if (!IsAppearanceGeometryValid(ref set.Appearances[appIndex]))
                    return false;
            }

            // Apply after validation.
            for (int b = 0; b < skin.Bindings.Length; b++)
            {
                int slotIndex = skin.Bindings[b].SlotIndex;
                int appIndex = skin.Bindings[b].AppearanceIndex;
                if (slotIndex < 0 || slotIndex >= set.Slots.Length)
                    continue;
                if (!TryFindLinkPart(links, slotIndex, out Entity part))
                    continue;
                ref var app = ref set.Appearances[appIndex];
                ApplyAppearanceBlob(em, root, part, appIndex, ref app, 0, default, false);
            }

            var active = new SpritePartsActiveSkin { SkinIdHash = skin.SkinIdHash };
            if (!em.HasComponent<SpritePartsActiveSkin>(root))
                em.AddComponentData(root, active);
            else
                em.SetComponentData(root, active);
            return true;
        }

        /// <summary>
        /// Restore every slot to its baked default appearance. Explicit reset — not an outfit undo stack.
        /// </summary>
        public static bool ResetSkin(EntityManager em, Entity root)
        {
            if (!IsPartsRoot(em, root) || !em.HasBuffer<SpritePartLink>(root))
                return false;
            var blob = em.GetComponentData<SpritePartsSetRef>(root).Set;
            if (!blob.IsCreated)
                return false;
            ref var set = ref blob.Value;
            var links = em.GetBuffer<SpritePartLink>(root);

            // Validate all defaults first.
            for (int i = 0; i < links.Length; i++)
            {
                int slotIndex = links[i].SlotIndex;
                if (slotIndex < 0 || slotIndex >= set.Slots.Length)
                    return false;
                int appIndex = set.Slots[slotIndex].DefaultAppearanceIndex;
                if (appIndex < 0)
                    continue;
                if (appIndex >= set.Appearances.Length)
                    return false;
                if (!IsAppearanceGeometryValid(ref set.Appearances[appIndex]))
                    return false;
            }

            for (int i = 0; i < links.Length; i++)
            {
                var part = links[i].Part;
                if (part == Entity.Null || !em.Exists(part))
                    continue;
                int slotIndex = links[i].SlotIndex;
                int appIndex = set.Slots[slotIndex].DefaultAppearanceIndex;
                if (appIndex < 0 || appIndex >= set.Appearances.Length)
                    continue;
                ref var app = ref set.Appearances[appIndex];
                ApplyAppearanceBlob(em, root, part, appIndex, ref app, 0, default, false);
            }

            var active = new SpritePartsActiveSkin { SkinIdHash = 0 };
            if (!em.HasComponent<SpritePartsActiveSkin>(root))
                em.AddComponentData(root, active);
            else
                em.SetComponentData(root, active);
            return true;
        }

        static bool TryFindLinkPart(DynamicBuffer<SpritePartLink> links, int slotIndex, out Entity part)
        {
            part = Entity.Null;
            for (int i = 0; i < links.Length; i++)
            {
                if (links[i].SlotIndex == slotIndex)
                {
                    part = links[i].Part;
                    return part != Entity.Null;
                }
            }
            return false;
        }

        static bool IsAppearanceGeometryValid(ref SpritePartAppearanceBlob app)
        {
            if (!math.isfinite(app.LogicalWorldSize.x) || !math.isfinite(app.LogicalWorldSize.y))
                return false;
            if (app.LogicalWorldSize.x <= 0f || app.LogicalWorldSize.y <= 0f)
                return false;
            if (!math.isfinite(app.Pivot.x) || !math.isfinite(app.Pivot.y))
                return false;
            if (!math.isfinite(app.FrameOffset.x) || !math.isfinite(app.FrameOffset.y))
                return false;
            if (!math.isfinite(app.FrameScale.x) || !math.isfinite(app.FrameScale.y))
                return false;
            if (app.CellIndex < 0)
                return false;
            return true;
        }

        /// <summary>
        /// Drive part appearance from clip sampling. sampledAppIndex >= 0 applies that appearance
        /// as a keyed override. sampledAppIndex < 0 clears keyed override and restores active skin
        /// binding or slot default (joint TRS untouched).
        /// </summary>
        public static void ApplySampledAppearance(
            EntityManager em, Entity root, Entity part, int slotIndex, int sampledAppIndex, ref SpritePartsSetBlob set)
            => ApplySampledAppearance(em, root, part, slotIndex, sampledAppIndex, ref set, default, false);

        internal static void ApplySampledAppearance(
            EntityManager em, Entity root, Entity part, int slotIndex, int sampledAppIndex,
            ref SpritePartsSetBlob set, EntityCommandBuffer commands)
            => ApplySampledAppearance(em, root, part, slotIndex, sampledAppIndex, ref set, commands, true);

        static void ApplySampledAppearance(
            EntityManager em, Entity root, Entity part, int slotIndex, int sampledAppIndex,
            ref SpritePartsSetBlob set, EntityCommandBuffer commands, bool deferred)
        {
            if (part == Entity.Null || !em.Exists(part))
                return;

            var state = em.HasComponent<SpritePartAppearanceState>(part)
                ? em.GetComponentData<SpritePartAppearanceState>(part)
                : default;
            bool wasKeyed = state.KeyedOverride != 0;

            if (sampledAppIndex >= 0)
            {
                if (sampledAppIndex >= set.Appearances.Length)
                    return;
                if (state.KeyedOverride != 0 && state.AppearanceIndex == sampledAppIndex)
                    return;
                ref var app = ref set.Appearances[sampledAppIndex];
                if (!IsAppearanceGeometryValid(ref app))
                    return;
                ApplyAppearanceBlob(em, root, part, sampledAppIndex, ref app, 1, commands, deferred);
                return;
            }

            if (!wasKeyed)
                return;

            // Restore skin binding if active, else default appearance.
            int restore = -1;
            if (em.HasComponent<SpritePartsActiveSkin>(root))
            {
                ulong skinHash = em.GetComponentData<SpritePartsActiveSkin>(root).SkinIdHash;
                if (skinHash != 0)
                {
                    for (int s = 0; s < set.SkinPatches.Length; s++)
                    {
                        if (set.SkinPatches[s].SkinIdHash != skinHash) continue;
                        ref var skin = ref set.SkinPatches[s];
                        for (int b = 0; b < skin.Bindings.Length; b++)
                        {
                            if (skin.Bindings[b].SlotIndex == slotIndex)
                            {
                                restore = skin.Bindings[b].AppearanceIndex;
                                break;
                            }
                        }
                        break;
                    }
                }
            }
            if (restore < 0 && slotIndex >= 0 && slotIndex < set.Slots.Length)
                restore = set.Slots[slotIndex].DefaultAppearanceIndex;
            if (restore < 0 || restore >= set.Appearances.Length)
            {
                if (em.HasComponent<SpritePartAppearanceState>(part))
                {
                    state.KeyedOverride = 0;
                    em.SetComponentData(part, state);
                }
                return;
            }
            ref var restoreApp = ref set.Appearances[restore];
            if (!IsAppearanceGeometryValid(ref restoreApp))
                return;
            ApplyAppearanceBlob(em, root, part, restore, ref restoreApp, 0, commands, deferred);
        }
        static void ApplyAppearanceBlob(
            EntityManager em, Entity root, Entity part, int appIndex, ref SpritePartAppearanceBlob app,
            byte keyedOverride, EntityCommandBuffer commands, bool deferred)
        {
            var state = new SpritePartAppearanceState
            {
                AppearanceIndex = appIndex,
                SheetTableIndex = app.SheetTableIndex,
                CellIndex = app.CellIndex,
                LogicalWorldSize = app.LogicalWorldSize,
                Pivot = app.Pivot,
                FrameOffset = app.FrameOffset,
                FrameScale = app.FrameScale,
                KeyedOverride = keyedOverride,
            };
            if (!em.HasComponent<SpritePartAppearanceState>(part))
                SpritePartsPoseUtility.AddComponent(em, part, state, commands, deferred);
            else
                em.SetComponentData(part, state);

            WriteFrame(em, part, app.CellIndex, app.FrameOffset, app.FrameScale, commands, deferred);

            Entity sheet = Entity.Null;
            if (em.HasBuffer<SpritePartSheetEntry>(root))
            {
                var sheets = em.GetBuffer<SpritePartSheetEntry>(root);
                for (int i = 0; i < sheets.Length; i++)
                {
                    if (sheets[i].SheetTableIndex == app.SheetTableIndex)
                    {
                        sheet = sheets[i].Sheet;
                        break;
                    }
                }
            }
            WriteSheetBinding(em, part, sheet, commands, deferred);
        }

        static void WriteFrame(EntityManager em, Entity part, int cell, float2 offset, float2 scale,
            EntityCommandBuffer commands, bool deferred)
        {
            var frame = em.HasComponent<SpriteAnimFrame>(part)
                ? em.GetComponentData<SpriteAnimFrame>(part)
                : default;
            frame.Slot = cell;
            frame.Offset = offset;
            frame.Scale = scale;
            frame.Rotation = 0f;
            if (!em.HasComponent<SpriteAnimFrame>(part))
                SpritePartsPoseUtility.AddComponent(em, part, frame, commands, deferred);
            else
                em.SetComponentData(part, frame);
        }

        static void WriteSheetBinding(EntityManager em, Entity part, Entity sheet,
            EntityCommandBuffer commands, bool deferred)
        {
            if (em.HasComponent<SpriteSheetBinding>(part))
            {
                var binding = em.GetComponentData<SpriteSheetBinding>(part);
                binding.Sheet = sheet;
                em.SetComponentData(part, binding);
            }
            else
            {
                SpritePartsPoseUtility.AddComponent(em, part, new SpriteSheetBinding { Sheet = sheet },
                    commands, deferred);
            }
        }

        static int ResolveOrRegisterSheet(EntityManager em, Entity root, Entity sheetEntity)
        {
            if (!em.HasBuffer<SpritePartSheetEntry>(root))
                em.AddBuffer<SpritePartSheetEntry>(root);
            var sheets = em.GetBuffer<SpritePartSheetEntry>(root);
            if (sheetEntity != Entity.Null)
            {
                for (int i = 0; i < sheets.Length; i++)
                {
                    if (sheets[i].Sheet == sheetEntity)
                        return sheets[i].SheetTableIndex;
                }
            }

            int next = 0;
            for (int i = 0; i < sheets.Length; i++)
                next = math.max(next, sheets[i].SheetTableIndex + 1);
            sheets.Add(new SpritePartSheetEntry
            {
                Sheet = sheetEntity,
                SheetTableIndex = next,
            });
            return next;
        }
    }

    /// <summary>Immediate pose/facing writers used by controls and systems.</summary>
    public static class SpritePartsPoseUtility
    {
        internal static void AddComponent<T>(EntityManager em, Entity entity, T value,
            EntityCommandBuffer commands, bool deferred) where T : unmanaged, IComponentData
        {
            if (deferred)
                commands.AddComponent(entity, value);
            else
                em.AddComponentData(entity, value);
        }

        public static void ApplyPose(EntityManager em, Entity root)
        {
            SpritePartsPoseWriter.Apply(em, root);
        }

        public static void ApplyPartTransform(EntityManager em, Entity part, in SpritePartsSampler.Pose pose)
            => ApplyPartTransform(em, part, pose, default, false);

        public static void ApplyPartTransform(EntityManager em, Entity part, in SpritePartsSampler.Pose pose,
            EntityCommandBuffer commands)
            => ApplyPartTransform(em, part, pose, commands, true);

        internal static void ApplyPartTransform(EntityManager em, Entity part, in SpritePartsSampler.Pose pose,
            EntityCommandBuffer commands, bool deferred)
        {
            if (!em.HasComponent<LocalTransform>(part))
                return;
            var lt = em.GetComponentData<LocalTransform>(part);
            lt.Position = new float3(pose.Position.x, pose.Position.y, 0f);
            lt.Rotation = quaternion.RotateZ(math.radians(pose.Rotation));
            lt.Scale = 1f;
            em.SetComponentData(part, lt);

            var scaleMatrix = SpritePartsPlayback.ScaleMatrix(pose.Scale.x, pose.Scale.y);
            if (!em.HasComponent<PostTransformMatrix>(part))
                AddComponent(em, part, new PostTransformMatrix { Value = scaleMatrix }, commands, deferred);
            else
                em.SetComponentData(part, new PostTransformMatrix { Value = scaleMatrix });

            var lattice = new SpritePartLattice { Value = pose.Lattice };
            if (!em.HasComponent<SpritePartLattice>(part))
            {
                if (!pose.Lattice.IsIdentity)
                    AddComponent(em, part, lattice, commands, deferred);
            }
            else
                em.SetComponentData(part, lattice);
        }

        public static void ApplyFacing(EntityManager em, Entity root)
            => ApplyFacing(em, root, default, false);

        public static void ApplyFacing(EntityManager em, Entity root, EntityCommandBuffer commands)
            => ApplyFacing(em, root, commands, true);

        internal static void ApplyFacing(EntityManager em, Entity root, EntityCommandBuffer commands, bool deferred)
        {
            if (!em.HasComponent<SpritePartsFacing>(root))
                return;
            var facing = em.GetComponentData<SpritePartsFacing>(root);
            var matrix = SpritePartsPlayback.FacingMatrix(facing.FlipX != 0, facing.FlipY != 0);
            Entity visual = Entity.Null;
            if (em.HasComponent<SpritePartsVisualRootRef>(root))
                visual = em.GetComponentData<SpritePartsVisualRootRef>(root).VisualRoot;
            if (visual == Entity.Null || !em.Exists(visual))
                return;
            if (!em.HasComponent<PostTransformMatrix>(visual))
                AddComponent(em, visual, new PostTransformMatrix { Value = matrix }, commands, deferred);
            else
                em.SetComponentData(visual, new PostTransformMatrix { Value = matrix });
        }
    }
}

