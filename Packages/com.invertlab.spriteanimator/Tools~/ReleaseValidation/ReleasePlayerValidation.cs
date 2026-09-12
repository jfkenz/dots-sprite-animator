using System;
using System.Collections;
using System.IO;
using InvertLab.Sprites.DOTS;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering;

public sealed class ReleasePlayerValidation : MonoBehaviour
{
    IEnumerator Start()
    {
        var variants = Resources.Load<ShaderVariantCollection>("DotsSpriteAnimator/RuntimeShaders");
        if (variants != null) variants.WarmUp();
        yield return null;
        var camera = Camera.main;
        var target = new RenderTexture(320, 180, 24, RenderTextureFormat.ARGB32);
        target.Create();
        camera.targetTexture = target;
        string result = "";
        for (int stage = 0; stage < 3; stage++)
        {
            SpriteShaderLibrary.UseLit = stage != 0;
            SpriteSheetRegistry.ResetMaterials();
            SpriteGpuAnimResources.MarkDirty();
            var light = UnityEngine.Object.FindAnyObjectByType<Light2D>();
            light.intensity = stage == 2 ? 0 : 1;
            for (int i = 0; i < 12; i++) yield return null;
            try
            {
                if (!SpriteShaderLibrary.TryFindAll(out var message)) throw new Exception(message);
                if (!SpriteInstanceRenderSystem.Active || !SpriteGpuAnimRenderSystem.Active)
                    throw new Exception("Both CPU and GPU renderers must draw.");
                var world = World.DefaultGameObjectInjectionWorld;
                world.GetExistingSystem<SpriteInstanceRenderSystem>().Update(world.Unmanaged);
                world.GetExistingSystem<SpriteGpuAnimRenderSystem>().Update(world.Unmanaged);
                foreach (var record in SpriteSheetRegistry.Records)
                {
                    if (record.Count == 0 || record.Buffer == null) continue;
                    var packed = new SpriteInstanceData[1]; record.Buffer.GetData(packed, 0, 0, 1);
                    Debug.Log("CPU count=" + record.Count + " pos=" + packed[0].PosScale + " crop=" + packed[0].CropST + " basis=" + packed[0].Transform2 + " tint=" + packed[0].Color);
                }
                RenderPipeline.SubmitRenderRequest(camera, new UniversalRenderPipeline.SingleCameraRequest { destination = target });
                var previous = RenderTexture.active;
                var image = new Texture2D(320, 180, TextureFormat.RGBA32, false);
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 320, 180), 0, 0);
                image.Apply();
                RenderTexture.active = previous;
                File.WriteAllBytes(Path.Combine(Application.dataPath, "../render-stage-" + stage + ".rgba"), image.GetRawTextureData<byte>().ToArray());
                foreach (float x in new[] {-1f,1f})
                {
                    var uv = camera.WorldToViewportPoint(new Vector3(x, 0, 0));
                    var pixel = image.GetPixel(Mathf.RoundToInt(uv.x * 320), Mathf.RoundToInt(uv.y * 180));
                    Debug.Log("RENDER stage=" + stage + " x=" + x + " pixel=" + pixel);
                    if (stage < 2 && (pixel.g < .12f || pixel.b < .12f)) throw new Exception("Sprite missing or wrong color at stage " + stage + " x=" + x);
                    if (stage == 2 && pixel.maxColorComponent > .12f) throw new Exception("Zero-intensity 2D light did not darken sprite x=" + x);
                }
                UnityEngine.Object.Destroy(image);
            }
            catch (Exception e) { result = "FAIL: " + e; break; }
        }
        if (result.Length == 0) result = "PASS: Windows player renders CPU/GPU sprites; URP 2D lighting responds to intensity; runtime shader variants retained.";
        Debug.Log(result);
        File.WriteAllText(Path.Combine(Application.dataPath, "../player-result.txt"), result);
        camera.targetTexture = null;
        target.Release();
        UnityEngine.Object.Destroy(target);
        Application.Quit(result.StartsWith("PASS") ? 0 : 1);
    }
}
