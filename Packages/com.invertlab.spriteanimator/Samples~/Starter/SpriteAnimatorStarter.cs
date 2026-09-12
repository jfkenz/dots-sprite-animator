using System.Collections.Generic;
using InvertLab.Sprites.DOTS;
using Unity.Entities;
using UnityEngine;

/// <summary>Self-contained sample. All demo pixels are generated here; no external art or input package.</summary>
public sealed class SpriteAnimatorStarter : MonoBehaviour
{
    World world;
    Entity cpu, gpu;
    Texture2D atlas;
    readonly List<Sprite> frames = new();
    bool paused;

    void Start()
    {
        world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            Debug.LogError("DOTS Sprite Animator needs the default Entities world.");
            enabled = false;
            return;
        }
        if (Camera.main == null)
        {
            var camera = new GameObject("Main Camera", typeof(Camera)).GetComponent<Camera>();
            camera.tag = "MainCamera";
            camera.orthographic = true;
            camera.orthographicSize = 3;
            camera.transform.position = new Vector3(0, 0, -10);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.025f, .04f, .07f);
        }
        atlas = new Texture2D(64, 16, TextureFormat.RGBA32, false)
        { name = "Generated starter atlas", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[64 * 16];
        for (int frame = 0; frame < 4; frame++)
        {
            int lift = frame == 1 || frame == 2 ? 2 : 0;
            for (int y = 2; y < 12; y++)
                for (int x = 3; x < 13; x++)
                    pixels[(y + lift) * 64 + frame * 16 + x] =
                        y >= 8 && y <= 9 && (x == 6 || x == 10) ? Color.white : new Color(.15f, .7f, .9f);
        }
        atlas.SetPixels32(pixels);
        atlas.Apply();
        for (int i = 0; i < 4; i++) frames.Add(Sprite.Create(atlas, new Rect(i * 16, 0, 16, 16), Vector2.one * .5f, 16));
        SpriteBatchSpawner.LayoutXy = true;
        var em = world.EntityManager;
        cpu = SpriteEntityFactory.Create(em, frames, 6, true, new Vector3(-1, 0, 0), 1.5f);
        gpu = SpriteEntityFactory.Create(em, frames, 6, true, new Vector3(1, 0, 0), 1.5f, new Color(1, .65f, .4f));
        SpriteGpuAnimSwitch.ToGpu(em, gpu, Time.unscaledTime);
    }

    void OnGUI()
    {
        if (world == null || !world.IsCreated || !world.EntityManager.Exists(cpu)) return;
        var em = world.EntityManager;
        GUILayout.BeginArea(new Rect(20, 20, 340, 245), GUI.skin.box);
        GUILayout.Label("DOTS Sprite Animator — Starter");
        GUILayout.Label("Left: CPU playback   Right: GPU clock");
        GUILayout.Label("Generated demo art; no external assets required.");
        if (GUILayout.Button(paused ? "Resume left sprite" : "Pause left sprite"))
        {
            paused = !paused;
            if (paused) SpriteAnims.Pause(em, cpu); else SpriteAnims.Resume(em, cpu);
        }
        if (GUILayout.Button("Restart left sprite")) { SpriteAnims.Play(em, cpu, "clip"); paused = false; }
        if (GUILayout.Button("Flip left sprite"))
        {
            var flip = em.GetComponentData<SpriteFlip>(cpu);
            flip.X = (byte)(flip.X == 0 ? 1 : 0);
            em.SetComponentData(cpu, flip);
        }
        if (GUILayout.Button("Switch right sprite CPU / GPU"))
        {
            if (em.HasComponent<SpriteGpuDriven>(gpu)) SpriteGpuAnimSwitch.ToCpuAtTime(em, gpu, Time.unscaledTime);
            else SpriteGpuAnimSwitch.ToGpu(em, gpu, Time.unscaledTime);
        }
        GUILayout.EndArea();
    }

    void OnDestroy()
    {
        if (world != null && world.IsCreated)
        {
            if (world.EntityManager.Exists(cpu)) world.EntityManager.DestroyEntity(cpu);
            if (world.EntityManager.Exists(gpu)) world.EntityManager.DestroyEntity(gpu);
        }
        foreach (var frame in frames) Destroy(frame);
        if (atlas != null) Destroy(atlas);
    }
}
