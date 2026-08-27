using UnityEngine;

namespace BallForge.Sprites.DOTS
{
    /// <summary>Centralized shader menu paths used by runtime/editor validation.</summary>
    public static class SpriteShaderLibrary
    {
        public const string UnlitShader = "DOTS Sprite Animator/Sprite Unlit 2D";
        public const string InstancedShader = "DOTS Sprite Animator/Sprite Unlit 2D Instanced";
        public const string GpuAnimShader = "DOTS Sprite Animator/Sprite Unlit 2D GPU Anim";

        public static bool TryFindAll(out string message)
        {
            var unlit = Shader.Find(UnlitShader);
            var instanced = Shader.Find(InstancedShader);
            var gpuAnim = Shader.Find(GpuAnimShader);
            if (unlit != null && instanced != null && gpuAnim != null)
            {
                message = "All DOTS Sprite Animator shaders found.";
                return true;
            }

            message =
                $"Missing shaders. Unlit={unlit != null}, Instanced={instanced != null}, GPUAnim={gpuAnim != null}";
            return false;
        }
    }
}
