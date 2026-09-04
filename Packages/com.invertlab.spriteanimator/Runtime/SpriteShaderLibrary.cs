using UnityEngine;

namespace InvertLab.Sprites.DOTS
{
    /// <summary>Centralized shader menu paths used by runtime/editor validation.</summary>
    public static class SpriteShaderLibrary
    {
        public const string UnlitShader = "DOTS Sprite Animator/Sprite Unlit 2D";
        public const string InstancedShader = "DOTS Sprite Animator/Sprite Unlit 2D Instanced";
        public const string GpuAnimShader = "DOTS Sprite Animator/Sprite Unlit 2D GPU Anim";
        /// <summary>MeshRenderer Scene Quad only - no DOTS_INSTANCING_ON; material props in UnityPerMaterial (SRP Batcher / BRG).</summary>
        public const string PreviewShader = "DOTS Sprite Animator/Sprite Unlit 2D Preview";

        // ---- URP 2D lighting (lit variants sample the 2D Renderer's light
        // textures; toggled from Tools > DOTS Sprite Animator) ----
        public const string InstancedShaderLit = "DOTS Sprite Animator/Sprite Unlit 2D Instanced Lit";
        public const string GpuAnimShaderLit = "DOTS Sprite Animator/Sprite Unlit 2D GPU Anim Lit";

        /// <summary>True = sprites composite with URP 2D lights (lit shaders).</summary>
        public static bool UseLit;

        public static string ActiveInstancedShader => UseLit ? InstancedShaderLit : InstancedShader;
        public static string ActiveGpuAnimShader => UseLit ? GpuAnimShaderLit : GpuAnimShader;

        public static bool TryFindAll(out string message)
        {
            var unlit = Shader.Find(UnlitShader);
            var instanced = Shader.Find(InstancedShader);
            var gpuAnim = Shader.Find(GpuAnimShader);
            var preview = Shader.Find(PreviewShader);
            if (unlit != null && instanced != null && gpuAnim != null && preview != null)
            {
                message = "All DOTS Sprite Animator shaders found.";
                return true;
            }

            message =
                $"Missing shaders. Unlit={unlit != null}, Instanced={instanced != null}, GPUAnim={gpuAnim != null}, Preview={preview != null}";
            return false;
        }
    }
}