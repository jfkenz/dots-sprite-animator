using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    /// <summary>Undo payload for overwriting a spritesheet texture file.</summary>
    class SpriteSheetTextureFileUndo : ScriptableObject
    {
        public string AssetPath;
        public byte[] Bytes;
    }

    [InitializeOnLoad]
    static class SpriteSheetTextureFlip
    {
        static SpriteSheetTextureFileUndo _undoState;

        static SpriteSheetTextureFlip()
        {
            Undo.undoRedoPerformed -= RestoreFileIfNeeded;
            Undo.undoRedoPerformed += RestoreFileIfNeeded;
        }

        public static SpriteSheetTextureFileUndo UndoState
        {
            get
            {
                if (_undoState == null)
                {
                    _undoState = ScriptableObject.CreateInstance<SpriteSheetTextureFileUndo>();
                    _undoState.hideFlags = HideFlags.HideAndDontSave;
                    _undoState.name = "Sprite Sheet Texture Flip Undo";
                }
                return _undoState;
            }
        }

        public static bool TryReadPixels(
            Texture2D texture,
            out Color32[] pixels,
            out int width,
            out int height,
            out string assetPath,
            out byte[] originalBytes,
            out string error)
        {
            pixels = null;
            width = 0;
            height = 0;
            assetPath = null;
            originalBytes = null;
            error = null;
            if (texture == null)
            {
                error = "Assign a sprite sheet before flipping";
                return false;
            }

            assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "The sheet texture is not a project asset";
                return false;
            }
            if (!File.Exists(assetPath))
            {
                error = "Could not find the texture file on disk";
                return false;
            }

            originalBytes = File.ReadAllBytes(assetPath);
            string ext = Path.GetExtension(assetPath).ToLowerInvariant();
            if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".tga")
            {
                error = "Flip Sheet can overwrite PNG, JPG, or TGA files";
                return false;
            }

            if (ext is ".png" or ".jpg" or ".jpeg")
            {
                var loaded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!loaded.LoadImage(originalBytes, false))
                    {
                        error = "Could not decode the texture file";
                        return false;
                    }
                    width = loaded.width;
                    height = loaded.height;
                    pixels = loaded.GetPixels32();
                    return pixels != null && pixels.Length >= width * height;
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(loaded);
                }
            }

            Texture2D readable = texture.isReadable ? texture : DuplicateReadable(texture);
            bool destroy = readable != texture;
            try
            {
                width = readable.width;
                height = readable.height;
                pixels = readable.GetPixels32();
                return pixels != null && pixels.Length >= width * height;
            }
            catch (Exception exception)
            {
                error = "Could not read texture pixels: " + exception.Message;
                return false;
            }
            finally
            {
                if (destroy && readable != null)
                    UnityEngine.Object.DestroyImmediate(readable);
            }
        }

        public static bool TryWritePixels(
            string assetPath, Color32[] pixels, int width, int height, out byte[] writtenBytes, out string error)
        {
            writtenBytes = null;
            error = null;
            if (string.IsNullOrEmpty(assetPath) || pixels == null || width <= 0 || height <= 0)
            {
                error = "Invalid texture write";
                return false;
            }

            AssetDatabase.MakeEditable(assetPath);
            var output = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                output.SetPixels32(pixels);
                output.Apply(false, false);
                string ext = Path.GetExtension(assetPath).ToLowerInvariant();
                writtenBytes = ext switch
                {
                    ".jpg" or ".jpeg" => output.EncodeToJPG(100),
                    ".tga" => output.EncodeToTGA(),
                    _ => output.EncodeToPNG(),
                };
                if (writtenBytes == null || writtenBytes.Length == 0)
                {
                    error = "Could not encode the flipped texture";
                    return false;
                }
                File.WriteAllBytes(assetPath, writtenBytes);
            }
            catch (Exception exception)
            {
                error = "Could not write the texture file: " + exception.Message;
                return false;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(output);
            }

            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            return true;
        }

        public static bool BytesEqual(byte[] a, byte[] b)
        {
            if (ReferenceEquals(a, b))
                return true;
            if (a == null || b == null || a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        static void RestoreFileIfNeeded()
        {
            if (_undoState == null ||
                string.IsNullOrEmpty(_undoState.AssetPath) ||
                _undoState.Bytes == null ||
                _undoState.Bytes.Length == 0)
                return;
            if (!File.Exists(_undoState.AssetPath))
                return;
            byte[] disk = File.ReadAllBytes(_undoState.AssetPath);
            if (BytesEqual(disk, _undoState.Bytes))
                return;
            try
            {
                File.WriteAllBytes(_undoState.AssetPath, _undoState.Bytes);
                AssetDatabase.ImportAsset(_undoState.AssetPath, ImportAssetOptions.ForceUpdate);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Could not restore flipped sprite sheet: " + exception.Message);
            }
        }

        static Texture2D DuplicateReadable(Texture2D source)
        {
            var previous = RenderTexture.active;
            var temporary = RenderTexture.GetTemporary(source.width, source.height, 0,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
            Graphics.Blit(source, temporary);
            RenderTexture.active = temporary;
            var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0f, 0f, source.width, source.height), 0, 0);
            copy.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(temporary);
            return copy;
        }
    }
}
