using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace InvertLab.Sprites.DOTS.Tests
{
    public sealed class SpriteShaderRegressionTests
    {
        [TestCase(SpriteShaderLibrary.GpuAnimShader)]
        [TestCase(SpriteShaderLibrary.GpuAnimShaderLit)]
        public void GpuFrameWrapsFromLastColumnToNextRow(string shaderName)
        {
            var atlas = new Texture2D(4, 2, TextureFormat.RGBA32, false, true)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            atlas.SetPixels(new[]
            {
                Color.green, Color.red, Color.red, Color.red,
                Color.red, Color.red, Color.red, Color.red,
            });
            atlas.Apply();
            try
            {
                var pixel = Render(shaderName, atlas, new float4(1f), false);
                Assert.Greater(pixel.g, 0.9f);
                Assert.Less(pixel.r, 0.1f);
            }
            finally { Object.DestroyImmediate(atlas); }
        }

        [TestCase(SpriteShaderLibrary.GpuAnimShaderLit)]
        [TestCase(SpriteShaderLibrary.InstancedShaderLit)]
        public void WhiteLightPreservesAllColorChannels(string shaderName)
        {
            var pixel = Render(shaderName, Texture2D.whiteTexture, new float4(1f), true);
            Assert.Greater(pixel.r, 0.9f);
            Assert.Greater(pixel.g, 0.9f);
            Assert.Greater(pixel.b, 0.9f);
        }

        [TestCase(SpriteShaderLibrary.GpuAnimShader)]
        [TestCase(SpriteShaderLibrary.GpuAnimShaderLit)]
        [TestCase(SpriteShaderLibrary.InstancedShader)]
        [TestCase(SpriteShaderLibrary.InstancedShaderLit)]
        public void ZeroTintAlphaDoesNotWriteDepth(string shaderName)
        {
            // Invisible near sprite must not occlude the green sprite drawn afterward.
            var pixel = Render(shaderName, Texture2D.whiteTexture, new float4(1, 1, 1, 0), false, true);
            Assert.Greater(pixel.g, 0.9f);
            Assert.Less(pixel.r, 0.1f);
        }

        static Color Render(string shaderName, Texture texture, float4 tint, bool lit, bool drawBehind = false)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.Ignore("Requires a graphics device.");
            bool gpu = shaderName.Contains("GPU Anim");
            var shader = Shader.Find(shaderName);
            Assert.IsNotNull(shader);
            Assert.IsTrue(shader.isSupported, shaderName);
            var mat = new Material(shader);
            using var buffer = new ComputeBuffer(2, gpu ? SpriteGpuAnimResources.Stride : SpriteRenderResources.Stride);
            if (gpu)
                buffer.SetData(new[]
                {
                    new SpriteGpuInstanceData { PosScale = new float4(0,0,1,0.25f), Cell = new float4(0.25f,0.5f,0.75f,0.5f), Anim = new float4(0,1,2,1), Color = tint },
                    new SpriteGpuInstanceData { PosScale = new float4(0,0,1,-0.25f), Cell = new float4(1,1,0,0), Anim = new float4(0,0,1,1), Color = new float4(0,1,0,1) },
                });
            else
                buffer.SetData(new[]
                {
                    new SpriteInstanceData { PosScale = new float4(0,0,1,0.25f), CropST = new float4(1,1,0,0), FrameTRS = new float4(1,1,0,0), Transform2 = new float4(1,1,0,0), Color = tint },
                    new SpriteInstanceData { PosScale = new float4(0,0,1,-0.25f), CropST = new float4(1,1,0,0), FrameTRS = new float4(1,1,0,0), Transform2 = new float4(1,1,0,0), Color = new float4(0,1,0,1) },
                });
            mat.SetTexture("_MainTex", texture);
            mat.SetBuffer("_InstanceData", buffer);
            mat.SetFloat("_LayoutXy", 1);
            mat.SetFloat("_CellAspect", 1);
            mat.SetFloat("_Cutoff", 0); // zero alpha must be discarded even with cutoff disabled
            if (gpu) mat.SetFloat("_Now", 1);
            if (lit)
            {
                mat.EnableKeyword("USE_SHAPE_LIGHT_TYPE_0");
                mat.SetTexture("_ShapeLightTexture0", Texture2D.whiteTexture);
                mat.SetVector("_ShapeLightBlendFactors0", new Vector4(1,0,0,0));
                mat.SetFloat("_HDREmulationScale", 1);
            }
            var target = new RenderTexture(8, 8, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var readback = new Texture2D(8, 8, TextureFormat.RGBA32, false, true);
            var previous = RenderTexture.active;
            using var commands = new CommandBuffer();
            try
            {
                target.Create();
                commands.SetRenderTarget(target);
                commands.ClearRenderTarget(true, true, Color.black);
                commands.SetViewProjectionMatrices(Matrix4x4.Translate(new Vector3(0,0,-2)), Matrix4x4.Ortho(-0.5f,0.5f,-0.5f,0.5f,0.1f,10));
                commands.DrawProcedural(Matrix4x4.identity, mat, 0, MeshTopology.Triangles, 6, drawBehind ? 2 : 1);
                Graphics.ExecuteCommandBuffer(commands);
                RenderTexture.active = target;
                readback.ReadPixels(new Rect(0,0,8,8), 0, 0);
                readback.Apply();
                return readback.GetPixel(4,4);
            }
            finally
            {
                RenderTexture.active = previous;
                target.Release();
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(readback);
                Object.DestroyImmediate(mat);
            }
        }
    }
}
