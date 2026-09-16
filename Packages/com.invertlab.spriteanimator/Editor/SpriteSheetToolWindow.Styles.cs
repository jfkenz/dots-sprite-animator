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
                _partsTabStyle = new GUIStyle(GUI.skin.button)
                {
                    fontSize = 11,
                    fixedHeight = 22f,
                    stretchWidth = true,
                    border = new RectOffset(4, 4, 4, 4),
                    margin = new RectOffset(0, 1, 0, 0),
                    padding = new RectOffset(6, 6, 2, 2),
                    overflow = new RectOffset(0, 0, 0, 0),
                    alignment = TextAnchor.MiddleCenter,
                    normal = { background = RoundedTexture(new Color(0.15f, 0.18f, 0.215f), BorderColor), textColor = Color.white },
                    hover = { background = RoundedTexture(new Color(0.20f, 0.25f, 0.29f), AccentColor * 0.65f), textColor = Color.white },
                    active = { background = RoundedTexture(new Color(0.12f, 0.36f, 0.4f), AccentColor), textColor = Color.white },
                    onNormal = { background = RoundedTexture(new Color(0.13f, 0.35f, 0.38f), new Color(0.22f, 0.53f, 0.56f)), textColor = Color.white },
                    onHover = { background = RoundedTexture(new Color(0.16f, 0.43f, 0.46f), AccentColor), textColor = Color.white },
                    onActive = { background = RoundedTexture(new Color(0.12f, 0.36f, 0.4f), AccentColor), textColor = Color.white },
                };
                _panelStyle = new GUIStyle
                {
                    border = new RectOffset(6, 6, 6, 6),
                    normal = { background = RoundedTexture(PanelColor, BorderColor) },
                };
                EnsurePartsRowIconStyles();
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

            EnsurePartsRowIconStyles();
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


        void EnsurePartsRowIconStyles()
        {
            // Domain reload / OnDisable can destroy textures while style refs stay non-null.
            bool eyeOk = _partsEyeToggleStyle != null &&
                         _partsEyeToggleStyle.normal.background != null;
            bool lockOk = _partsLockToggleStyle != null &&
                          _partsLockToggleStyle.normal.background != null;
            bool rowOk = _partsRowIconStyle != null;
            if (eyeOk && lockOk && rowOk)
                return;
            _partsEyeToggleStyle = null;
            _partsLockToggleStyle = null;
            _partsRowIconStyle = null;

            var eyeOn = MakeEyeTexture(visible: true);
            var eyeOff = MakeEyeTexture(visible: false);
            var lockOn = MakeLockTexture(locked: true);
            var lockOff = MakeLockTexture(locked: false);

            _partsRowIconStyle = new GUIStyle
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                fixedWidth = 16f,
                fixedHeight = 16f,
                stretchWidth = false,
                stretchHeight = false,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0),
                normal = { textColor = new Color(0.75f, 0.78f, 0.82f, 1f) },
                hover = { textColor = new Color(1f, 0.45f, 0.42f, 1f) },
                active = { textColor = new Color(1f, 0.32f, 0.3f, 1f) },
            };

            _partsEyeToggleStyle = new GUIStyle
            {
                fixedWidth = 16f,
                fixedHeight = 16f,
                stretchWidth = false,
                stretchHeight = false,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                normal = { background = eyeOff },
                hover = { background = eyeOff },
                active = { background = eyeOff },
                onNormal = { background = eyeOn },
                onHover = { background = eyeOn },
                onActive = { background = eyeOn },
            };

            _partsLockToggleStyle = new GUIStyle
            {
                fixedWidth = 16f,
                fixedHeight = 16f,
                stretchWidth = false,
                stretchHeight = false,
                border = new RectOffset(0, 0, 0, 0),
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0),
                overflow = new RectOffset(0, 0, 0, 0),
                alignment = TextAnchor.MiddleCenter,
                normal = { background = lockOff },
                hover = { background = lockOff },
                active = { background = lockOff },
                onNormal = { background = lockOn },
                onHover = { background = lockOn },
                onActive = { background = lockOn },
            };
        }

        Texture2D MakeEyeTexture(bool visible)
        {
            const int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = Color.clear;
            var white = visible
                ? new Color(0.90f, 0.93f, 0.96f, 1f)
                : new Color(0.55f, 0.58f, 0.63f, 0.95f);
            var pupil = new Color(0.10f, 0.12f, 0.15f, 1f);
            var slash = new Color(0.92f, 0.38f, 0.34f, 1f);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                float nx = (px - 8f) / 6.4f;
                float ny = (py - 8f) / 3.5f;
                Color c = clear;
                if (nx * nx + ny * ny <= 1f)
                {
                    c = white;
                    float pr = Mathf.Sqrt((px - 8f) * (px - 8f) + (py - 8f) * (py - 8f));
                    if (visible)
                    {
                        if (pr < 2.2f) c = pupil;
                        else if (pr < 3.0f) c = Color.Lerp(pupil, white, (pr - 2.2f) / 0.8f);
                    }
                }
                if (!visible)
                {
                    float dx = 13f - 3f;
                    float dy = 4f - 12f;
                    float len = Mathf.Sqrt(dx * dx + dy * dy);
                    float t = ((px - 3f) * dx + (py - 12f) * dy) / (len * len);
                    t = Mathf.Clamp01(t);
                    float qx = 3f + t * dx;
                    float qy = 12f + t * dy;
                    float dist = Mathf.Sqrt((px - qx) * (px - qx) + (py - qy) * (py - qy));
                    if (dist < 1.15f)
                        c = slash;
                }
                tex.SetPixel(x, y, c);
            }
            tex.Apply(false, false);
            _styleTextures.Add(tex);
            return tex;
        }

        Texture2D MakeLockTexture(bool locked)
        {
            const int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            var clear = Color.clear;
            var metal = locked
                ? new Color(0.78f, 0.82f, 0.88f, 1f)
                : new Color(0.50f, 0.54f, 0.60f, 0.95f);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float px = x + 0.5f;
                float py = y + 0.5f;
                Color c = clear;
                // body
                if (px >= 4.5f && px <= 11.5f && py >= 2.5f && py <= 8.5f)
                    c = metal;
                // keyhole
                if (locked && Mathf.Sqrt((px - 8f) * (px - 8f) + (py - 5.5f) * (py - 5.5f)) < 1.1f)
                    c = new Color(0.12f, 0.13f, 0.16f, 1f);
                // shackle
                float cx = 8f, cy = 10.5f, r = 3.2f;
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                bool onArc = py >= 10f && d < r + 1.05f && d > r - 1.05f;
                if (locked && onArc)
                    c = metal;
                if (!locked && onArc && px >= 8f)
                    c = metal;
                tex.SetPixel(x, y, c);
            }
            tex.Apply(false, false);
            _styleTextures.Add(tex);
            return tex;
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
