using System;
using System.Collections.Generic;
using System.IO;
using InvertLab.Sprites.DOTS;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class ReleaseValidation
{
    public static void Build()
    {
        const string collectionPath = "Packages/com.invertlab.spriteanimator/Runtime/Resources/DotsSpriteAnimator/RuntimeShaders.shadervariants";
        var collection = AssetDatabase.LoadAssetAtPath<ShaderVariantCollection>(collectionPath);
        if (collection == null)
        {
            collection = new ShaderVariantCollection();
            AssetDatabase.CreateAsset(collection, collectionPath);
        }
        collection.Clear();
        foreach (var name in new[] { SpriteShaderLibrary.UnlitShader, SpriteShaderLibrary.PreviewShader,
            SpriteShaderLibrary.InstancedShader, SpriteShaderLibrary.GpuAnimShader,
            SpriteShaderLibrary.InstancedShaderLit, SpriteShaderLibrary.GpuAnimShaderLit })
        {
            var shader = Shader.Find(name);
            if (shader == null) throw new Exception("Shader missing: " + name);
            int variants = name.EndsWith(" Lit") ? 16 : name == SpriteShaderLibrary.UnlitShader ? 2 : 1;
            int added = 0;
            for (int mask = 0; mask < variants; mask++)
            {
                var keywords = new List<string>();
                if (name.EndsWith(" Lit"))
                    for (int bit = 0; bit < 4; bit++)
                        if ((mask & (1 << bit)) != 0) keywords.Add("USE_SHAPE_LIGHT_TYPE_" + bit);
                if (name == SpriteShaderLibrary.UnlitShader && mask == 1) keywords.Add("DOTS_INSTANCING_ON");
                foreach (var pass in new[] { PassType.Normal, PassType.ScriptableRenderPipeline })
                {
                    try { if (collection.Add(new ShaderVariantCollection.ShaderVariant(shader, pass, keywords.ToArray()))) added++; }
                    catch (ArgumentException) { }
                }
            }
            if (added == 0) throw new Exception("No preserved variants for " + name);
        }
        EditorUtility.SetDirty(collection);
        Directory.CreateDirectory("Assets/ReleaseValidation");
        var renderer = AssetDatabase.LoadAssetAtPath<Renderer2DData>("Assets/ReleaseValidation/Renderer.asset");
        if (renderer == null)
        {
            renderer = ScriptableObject.CreateInstance<Renderer2DData>();
            AssetDatabase.CreateAsset(renderer, "Assets/ReleaseValidation/Renderer.asset");
        }
        var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/ReleaseValidation/Pipeline.asset");
        if (pipeline == null)
        {
            pipeline = UniversalRenderPipelineAsset.Create(renderer);
            AssetDatabase.CreateAsset(pipeline, "Assets/ReleaseValidation/Pipeline.asset");
        }
        GraphicsSettings.defaultRenderPipeline = pipeline;
        QualitySettings.renderPipeline = pipeline;
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("Starter", typeof(SpriteAnimatorStarter), typeof(ReleasePlayerValidation));
        var light = new GameObject("Global 2D Light", typeof(Light2D)).GetComponent<Light2D>();
        light.lightType = Light2D.LightType.Global;
        light.intensity = 1;
        EditorSceneManager.SaveScene(scene, "Assets/ReleaseValidation/Smoke.unity");
        AssetDatabase.SaveAssets();
        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D11 });
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { "Assets/ReleaseValidation/Smoke.unity" },
            target = BuildTarget.StandaloneWindows64,
            locationPathName = "Build/ReleaseValidation.exe",
            options = BuildOptions.Development,
        });
        File.WriteAllText("build-result.txt", report.summary.result + " errors=" + report.summary.totalErrors + " warnings=" + report.summary.totalWarnings);
        if (report.summary.result != BuildResult.Succeeded) throw new Exception("Player build failed.");
    }
}
