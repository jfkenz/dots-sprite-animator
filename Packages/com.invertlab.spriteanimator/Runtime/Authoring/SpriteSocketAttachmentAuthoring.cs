using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>
    /// Attaches this GameObject to a named socket on an animated parent.
    /// Keep this object as a direct child of the referenced SpriteAnimSetAuthoring.
    /// </summary>
    [AddComponentMenu("DOTS Sprite Animator/Sprite Socket Attachment")]
    [DisallowMultipleComponent]
    public sealed class SpriteSocketAttachmentAuthoring : MonoBehaviour
    {
        [Tooltip("Animated source. When empty, the direct parent is used.")]
        public SpriteAnimSetAuthoring Player;

        [Tooltip("Socket identity shared by the profile's frame keys.")]
        public string SocketName = "Weapon";

        [Tooltip("Stable gameplay ID, for example equipment.head. Empty uses the legacy Socket Name.")]
        public string SocketId = string.Empty;

        [Tooltip("Extra local offset, in world units, rotated with the socket.")]
        public Vector2 PositionOffset;

        [Tooltip("Extra Z rotation in degrees.")]
        public float AngleOffset;

        sealed class Baker : Baker<SpriteSocketAttachmentAuthoring>
        {
            public override void Bake(SpriteSocketAttachmentAuthoring authoring)
            {
                var player = authoring.Player;
                if (player == null && authoring.transform.parent != null)
                    player = authoring.transform.parent.GetComponent<SpriteAnimSetAuthoring>();

                if (player == null)
                {
                    Debug.LogWarning(
                        $"[{nameof(SpriteSocketAttachmentAuthoring)}] '{authoring.name}' needs a " +
                        $"{nameof(SpriteAnimSetAuthoring)} source.", authoring);
                    return;
                }

                if (authoring.transform.parent != player.transform)
                {
                    Debug.LogWarning(
                        $"[{nameof(SpriteSocketAttachmentAuthoring)}] '{authoring.name}' must be a direct " +
                        $"child of '{player.name}' so its baked transform is socket-local.", authoring);
                    return;
                }

                string socketName = SpriteSocketKeys.CanonicalName(authoring.SocketName);
                string socketId = SpriteSocketIdUtility.Canonical(authoring.SocketId, socketName);
                if (player.Profile != null)
                    DependsOn(player.Profile);

                if (!HasSocket(player, socketId, socketName))
                {
                    Debug.LogWarning(
                        $"[{nameof(SpriteSocketAttachmentAuthoring)}] Socket ID '{socketId}' was not found " +
                        $"on any frame or independent track in '{player.name}'. The attachment keeps its authored pose until a key exists.",
                        authoring);
                }

                Entity targetEntity = GetEntity(TransformUsageFlags.Dynamic);
                Entity sourceEntity = GetEntity(player.gameObject, TransformUsageFlags.Dynamic);
                AddComponent(targetEntity, new SpriteSocketAttachment
                {
                    Source = sourceEntity,
                    SocketName = new FixedString64Bytes(socketName),
                    SocketId = new FixedString64Bytes(socketId),
                    SocketIdHash = SpriteSockets.Hash(socketId),
                    PositionOffset = new float2(authoring.PositionOffset.x, authoring.PositionOffset.y),
                    AngleOffset = authoring.AngleOffset,
                    BaseScale = authoring.transform.localScale.x,
                });

                Vector3 authoredScale = authoring.transform.localScale;
                bool hasNonUniformScale =
                    !Mathf.Approximately(authoring.transform.localScale.x, authoredScale.y) ||
                    !Mathf.Approximately(authoring.transform.localScale.x, authoredScale.z);
                if (!hasNonUniformScale)
                {
                    AddComponent(targetEntity, new PostTransformMatrix
                    {
                        Value = float4x4.identity,
                    });
                }
                else
                {
                    Debug.LogWarning(
                        $"[{nameof(SpriteSocketAttachmentAuthoring)}] '{authoring.name}' has non-uniform " +
                        "root scale. Socket scale drives that root; put permanent visual scaling on a child.",
                        authoring);
                }
            }

            static bool HasSocket(SpriteAnimSetAuthoring player, string socketId, string socketName)
            {
                var profile = player.Profile?.Data;
                profile?.EnsureSocketCatalog();
                var catalog = profile?.SocketCatalog?.Items;
                if (catalog != null)
                {
                    for (int i = 0; i < catalog.Count; i++)
                    {
                        var item = catalog[i];
                        if (item != null &&
                            string.Equals(item.SocketId, socketId,
                                System.StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                }
                var motions = profile?.SocketMotions;
                if (motions != null)
                {
                    for (int i = 0; i < motions.Count; i++)
                    {
                        var motion = motions[i];
                        if (motion != null && motion.Keys != null && motion.Keys.Count > 0 &&
                            SpriteSocketKeys.NamesEqual(motion.SocketName, socketName))
                            return true;
                    }
                }

                var profileClips = profile?.Clips;
                if (profileClips != null && profileClips.Count > 0)
                {
                    for (int clipIndex = 0; clipIndex < profileClips.Count; clipIndex++)
                    {
                        var sockets = profileClips[clipIndex]?.Sockets;
                        if (sockets == null)
                            continue;
                        for (int socketIndex = 0; socketIndex < sockets.Count; socketIndex++)
                        {
                            var socket = sockets[socketIndex];
                            if (socket != null && SpriteSocketKeys.NamesEqual(socket.Name, socketName))
                                return true;
                        }
                    }
                    return false;
                }

                var clips = player.Clips;
                if (clips == null)
                    return false;
                for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
                {
                    var sockets = clips[clipIndex].Sockets;
                    if (sockets == null)
                        continue;
                    for (int socketIndex = 0; socketIndex < sockets.Length; socketIndex++)
                    {
                        var socket = sockets[socketIndex];
                        if (socket != null && SpriteSocketKeys.NamesEqual(socket.Name, socketName))
                            return true;
                    }
                }
                return false;
            }
        }
    }
}
