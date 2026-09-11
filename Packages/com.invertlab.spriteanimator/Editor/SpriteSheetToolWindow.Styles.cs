using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using InvertLab.Sprites.DOTS;

namespace InvertLab.Sprites.DOTS.Editor
{
    public sealed partial class SpriteSheetToolWindow
    {

        void EnsureStyles()
        {
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 15,
                    normal = { textColor = Color.white },
                };
                _sectionStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 11,
                    normal = { textColor = new Color(0.76f, 0.83f, 0.91f) },
                };
                _mutedStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    fontSize = 11,
                    normal = { textColor = TextMuted },
                    clipping = TextClipping.Clip,
                };
                _mutedWrapStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    wordWrap = true,
                    fontSize = 10,
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Overflow,
                    normal = { textColor = TextMuted },
                };
                _frameLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 13,
                    normal = { textColor = TextMuted },
                };
                _onionBadgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    normal = { textColor = Color.white },
                };
                _transportStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 11,
                    fixedHeight = 28f,
                    border = new RectOffset(6, 6, 6, 6),
                    padding = new RectOffset(5, 5, 2, 2),
                    normal = { background = RoundedTexture(new Color(0.15f, 0.18f, 0.215f), BorderColor), textColor = Color.white },
                    hover = { background = RoundedTexture(new Color(0.20f, 0.25f, 0.29f), AccentColor * 0.65f), textColor = Color.white },
                    active = { background = RoundedTexture(new Color(0.12f, 0.36f, 0.4f), AccentColor), textColor = Color.white },
                };
                _primaryStyle = new GUIStyle(_transportStyle)
                {
                    fontStyle = FontStyle.Bold,
                    normal = { background = RoundedTexture(new Color(0.13f, 0.35f, 0.38f), new Color(0.22f, 0.53f, 0.56f)) },
                    hover = { background = RoundedTexture(new Color(0.16f, 0.43f, 0.46f), AccentColor) },
                };
                _panelStyle = new GUIStyle
                {
                    border = new RectOffset(6, 6, 6, 6),
                    normal = { background = RoundedTexture(PanelColor, BorderColor) },
                };
                _saveStatusStyle = new GUIStyle(_mutedStyle) { alignment = TextAnchor.MiddleRight };
                _clipStyle = new GUIStyle(GUI.skin.button)
                {
                    alignment = TextAnchor.MiddleLeft,
                    normal = { background = SolidTexture(PanelAltColor) },
                    hover = { background = SolidTexture(new Color(0.17f, 0.195f, 0.235f)) },
                };
                _clipSelectedStyle = new GUIStyle(_clipStyle)
                {
                    normal = { background = SolidTexture(new Color(0.12f, 0.30f, 0.34f)), textColor = Color.white },
                    hover = { background = SolidTexture(new Color(0.14f, 0.37f, 0.41f)), textColor = Color.white },
                };
            }

            if (_socketLabelStyle == null)
            {
                _socketLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    fontSize = 10,
                    padding = new RectOffset(4, 4, 1, 1),
                    normal = { textColor = Color.white },
                };
                _socketBalloonStyle = new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 11,
                    wordWrap = true,
                    normal = { textColor = Color.white },
                };
            }
        }

        Texture2D RoundedTexture(Color fill, Color border)
        {
            const int size = 16;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(Mathf.Abs(x + 0.5f - size * 0.5f) - 3f, 0f);
                float dy = Mathf.Max(Mathf.Abs(y + 0.5f - size * 0.5f) - 3f, 0f);
                float distance = Mathf.Sqrt(dx * dx + dy * dy) - 5f;
                Color color = Color.Lerp(fill, border, Mathf.Clamp01(distance + 1.5f));
                color.a = Mathf.Clamp01(0.5f - distance);
                texture.SetPixel(x, y, color);
            }
            texture.Apply();
            _styleTextures.Add(texture);
            return texture;
        }

        Texture2D SolidTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            _styleTextures.Add(texture);
            return texture;
        }

        struct PreviewState
        {
            public int Frame;
            public float Fraction;
            public float TimelineTime;
            public bool Ended;
        }

    }
}
