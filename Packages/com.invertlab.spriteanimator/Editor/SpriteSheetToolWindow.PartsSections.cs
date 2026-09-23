using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace InvertLab.Sprites.DOTS.Editor
{
    // Collapsible inspector sections: a header bar with a foldout arrow, the title, and a short summary on the
    // right (so a closed section still says what it holds). Open / closed is remembered per section title.
    public sealed partial class SpriteSheetToolWindow
    {
        [SerializeField] List<string> _partsClosedSections = new List<string>();
        GUIStyle _partsFoldoutStyle;
        GUIStyle _partsSummaryStyle;

        void EnsurePartsSectionStyles()
        {
            if (_partsFoldoutStyle != null)
                return;
            _partsFoldoutStyle = new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };
            if (_sectionStyle != null)
                _partsFoldoutStyle.fontSize = _sectionStyle.fontSize;
            _partsSummaryStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                clipping = TextClipping.Clip,
            };
        }

        /// <summary>Draws a section header; returns true when the section is open (draw its content).</summary>
        bool PartsSection(string title, string summary = null)
        {
            EnsurePartsSectionStyles();
            GUILayout.Space(6f);
            var r = GUILayoutUtility.GetRect(10f, 20f, GUILayout.ExpandWidth(true));
            bool open = !_partsClosedSections.Contains(title);
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(r, new Color(1f, 1f, 1f, 0.045f));
            if (!string.IsNullOrEmpty(summary))
                GUI.Label(new Rect(r.x + r.width * 0.42f, r.y + 2f, r.width * 0.58f - 6f, 16f), summary, _partsSummaryStyle);
            bool next = EditorGUI.Foldout(new Rect(r.x + 4f, r.y + 2f, r.width * 0.55f, 16f), open, title, true, _partsFoldoutStyle);
            if (next != open)
            {
                if (next)
                    _partsClosedSections.Remove(title);
                else
                    _partsClosedSections.Add(title);
                Repaint();
            }
            return next;
        }
    }
}
