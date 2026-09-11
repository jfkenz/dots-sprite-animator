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

        void DrawClipBrowser(Rect rect)
        {
            _profile.EnsureSheets(_selectedSheet);
            if (_profile.Sheets.Count > 0)
                _selectedSheet = Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1);
            EnsureSheetFoldState();

            int sheetCount = _profile.Sheets.Count;
            int clipCount = _profile.Clips != null ? _profile.Clips.Count : 0;
            CacheSheetClipCounts(sheetCount);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 10f, rect.width - 24f, 20f), "CLIPS", _sectionStyle);
            GUI.Label(new Rect(rect.x + 12f, rect.y + 31f, rect.width - 24f, 16f),
                $"{sheetCount} sheet{(sheetCount == 1 ? "" : "s")} · {clipCount} clip{(clipCount == 1 ? "" : "s")}",
                _mutedStyle);

            const float cardPad = 8f;
            const float headerH = 24f;
            const float insetMargin = 8f;
            const float clipRowH = NestedClipRowHeight;
            const float actionH = 58f;
            const float addSheetH = 28f;
            const float addSheetW = 72f;
            const float cardGap = 6f;
            bool stackAddSheet = sheetCount > 1;
            const float columnFooterH = 38f;

            var listRect = new Rect(rect.x + 8f, rect.y + 52f, rect.width - 16f,
                Mathf.Max(24f, rect.height - 52f - (stackAddSheet ? columnFooterH : 4f)));

            float contentHeight = 4f;
            for (int s = 0; s < sheetCount; s++)
            {
                bool expanded = s == _selectedSheet;
                int n = expanded ? _sheetClipCounts[s] : 0;
                float cardH = cardPad + headerH + cardPad;
                if (expanded)
                {
                    float clipsH = MeasureSheetClipRowsHeight(s, clipCount, clipRowH);
                    float insetH = insetMargin + clipsH + 4f + actionH + insetMargin;
                    cardH = cardPad + headerH + 6f + insetH + cardPad;
                    if (!stackAddSheet)
                        cardH += 4f + addSheetH;
                }
                contentHeight += cardH + cardGap;
            }
            contentHeight = Mathf.Max(listRect.height, contentHeight);

            _clipScroll = GUI.BeginScrollView(listRect, _clipScroll,
                new Rect(0f, 0f, listRect.width - 15f, contentHeight));

            var input = Event.current;
            if (_renamingClip < 0)
                _hasClipRenameFieldRect = false;
            HandleBrowserRenameKeys(input);

            float y = 4f;
            float rowW = listRect.width - 21f;
            var sheetCardColor = new Color(0.155f, 0.172f, 0.205f, 1f);
            var clipInsetColor = new Color(0.068f, 0.078f, 0.098f, 1f);
            var quietBorder = new Color(0.2f, 0.225f, 0.265f, 1f);
            var insetBorder = new Color(0.155f, 0.175f, 0.21f, 1f);

            for (int s = 0; s < sheetCount; s++)
            {
                var def = _profile.Sheets[s];
                bool expanded = s == _selectedSheet;
                int clipsOnSheet = _sheetClipCounts[s];

                float insetH = 0f;
                if (expanded)
                {
                    float clipsH = MeasureSheetClipRowsHeight(s, clipCount, clipRowH);
                    insetH = insetMargin + clipsH + 4f + actionH + insetMargin;
                }
                float cardH = cardPad + headerH + cardPad;
                if (expanded)
                {
                    cardH = cardPad + headerH + 6f + insetH + cardPad;
                    if (!stackAddSheet)
                        cardH += 4f + addSheetH;
                }

                var cardRect = new Rect(2f, y, rowW, cardH);
                EditorGUI.DrawRect(cardRect, sheetCardColor);
                DrawBorder(cardRect, quietBorder, 1f);

                var headerRect = new Rect(cardRect.x + cardPad, cardRect.y + cardPad,
                    cardRect.width - cardPad * 2f, headerH);
                float sheetDelW = ClipRowDeleteWidth;
                float countW = expanded ? 0f : 58f;
                float trailingW = sheetDelW + 2f + countW;
                var nameRect = new Rect(headerRect.x, headerRect.y,
                    Mathf.Max(20f, headerRect.width - trailingW), headerH);
                var sheetDeleteRect = new Rect(headerRect.xMax - sheetDelW, headerRect.y + 4f,
                    sheetDelW, 16f);
                var countRect = expanded
                    ? Rect.zero
                    : new Rect(sheetDeleteRect.x - 2f - countW, headerRect.y + 4f, countW, 16f);

                bool renaming = s == _renamingSheet;
                if (renaming)
                {
                    GUI.SetNextControlName(SheetRenameControl);
                    _renameSheetValue = GUI.TextField(nameRect, _renameSheetValue, EditorStyles.boldLabel);
                    if (_focusSheetRename || GUI.GetNameOfFocusedControl() != SheetRenameControl)
                    {
                        EditorGUI.FocusTextInControl(SheetRenameControl);
                        if (GUI.GetNameOfFocusedControl() == SheetRenameControl)
                            _focusSheetRename = false;
                    }
                }
                else
                {
                    string sheetName = string.IsNullOrWhiteSpace(def?.Name)
                        ? (def?.Texture != null && !string.IsNullOrEmpty(def.Texture.name)
                            ? def.Texture.name
                            : $"Sheet {s + 1}")
                        : def.Name;
                    GUI.Label(nameRect, new GUIContent(sheetName,
                        "Click to select this sheet. F2 or double-click the name to rename."),
                        EditorStyles.boldLabel);
                }

                if (!expanded)
                {
                    GUI.Label(countRect,
                        $"{clipsOnSheet} clip{(clipsOnSheet == 1 ? "" : "s")}", _mutedStyle);
                }

                // Sheet delete X — same affordance as nested clip rows.
                bool canDeleteSheet = sheetCount > 1;
                using (new EditorGUI.DisabledScope(!canDeleteSheet || renaming))
                {
                    Color prevSheetGui = GUI.color;
                    bool sheetDelHover = sheetDeleteRect.Contains(input.mousePosition);
                    if (canDeleteSheet && sheetDelHover && !renaming)
                        GUI.color = new Color(1f, 0.42f, 0.42f, 1f);
                    if (GUI.Button(sheetDeleteRect,
                        new GUIContent("✕",
                            canDeleteSheet
                                ? "Delete this sheet and its clips (Undo supported)."
                                : "Keep at least one sheet."),
                        EditorStyles.miniButton))
                    {
                        CancelAllRenames();
                        if (canDeleteSheet)
                        {
                            string delName = string.IsNullOrWhiteSpace(def?.Name)
                                ? $"Sheet {s + 1}"
                                : def.Name;
                            int clipN = clipsOnSheet;
                            string msg = clipN > 0
                                ? $"Delete sheet '{delName}' and its {clipN} clip{(clipN == 1 ? "" : "s")}?"
                                : $"Delete sheet '{delName}'?";
                            if (EditorUtility.DisplayDialog("Delete Sheet", msg, "Delete", "Cancel"))
                                DeleteSheetAt(s);
                        }
                    }
                    GUI.color = prevSheetGui;
                }

                if (!renaming && input.type == EventType.MouseDown && input.button == 0 &&
                    headerRect.Contains(input.mousePosition) &&
                    !sheetDeleteRect.Contains(input.mousePosition))
                {
                    SelectSheetRow(s);
                    if (nameRect.Contains(input.mousePosition) && input.clickCount >= 2)
                        BeginSheetRename(s);
                    input.Use();
                }

                if (expanded)
                {
                    var inset = new Rect(cardRect.x + insetMargin, headerRect.yMax + 6f,
                        cardRect.width - insetMargin * 2f, insetH);
                    EditorGUI.DrawRect(inset, clipInsetColor);
                    DrawBorder(inset, insetBorder, 1f);

                    float clipY = inset.y + insetMargin;
                    int pendingDeleteClip = -1;
                    int pendingDuplicateClip = -1;
                    SyncClipMultiSelection();
                    if (_profile.Clips != null)
                    {
                        for (int i = 0; i < clipCount; i++)
                        {
                            var clip = _profile.Clips[i];
                            if (clip == null || clip.SheetIndex != s)
                                continue;

                            bool isPrimary = i == _selectedClip;
                            bool isSelected = _selectedClips.Contains(i) || isPrimary;
                            bool showDetail = isPrimary && isSelected && _clipRowDetailsExpanded;
                            float detailH = showDetail ? MeasureClipRowDetailHeight(clip) : 0f;
                            float rowH = clipRowH + detailH;
                            var itemRect = new Rect(inset.x + 4f, clipY, inset.width - 8f, rowH - 2f);
                            var headerRow = new Rect(itemRect.x, itemRect.y, itemRect.width, clipRowH - 2f);

                            // Compact row: [fold][name……][✕] — fold column reserved on all rows for alignment.
                            float foldW = ClipRowFoldWidth;
                            float delW = ClipRowDeleteWidth;
                            float nameLeft = headerRow.x + 4f + foldW;
                            float nameRight = headerRow.xMax - 4f - delW - 2f;
                            var foldRect = new Rect(headerRow.x + 2f, headerRow.y + 2f, foldW, 16f);
                            var deleteRect = new Rect(nameRight + 2f, headerRow.y + 2f, delW, 16f);
                            var clipNameRect = new Rect(nameLeft, headerRow.y + 2f,
                                Mathf.Max(20f, nameRight - nameLeft), 16f);
                            var detailRect = new Rect(
                                itemRect.x + ClipNestIndent,
                                headerRow.yMax + 2f,
                                Mathf.Max(40f, itemRect.width - ClipNestIndent - 4f),
                                showDetail ? Mathf.Max(0f, detailH - 4f) : 0f);

                            bool isRenamingClip = i == _renamingClip;
                            // Draw selection chrome with DrawRect (not a button-styled Box) so the
                            // row never competes with the inline rename TextField for hotControl.
                            EditorGUI.DrawRect(itemRect, isSelected || isRenamingClip
                                ? new Color(0.12f, 0.34f, 0.47f, 1f)
                                : PanelAltColor);
                            if (showDetail)
                            {
                                var detailBg = new Rect(itemRect.x + 1f, headerRow.yMax,
                                    itemRect.width - 2f, itemRect.yMax - headerRow.yMax - 1f);
                                EditorGUI.DrawRect(detailBg, new Color(0.08f, 0.1f, 0.13f, 0.95f));
                            }

                            // Draw interactive controls FIRST so they receive MouseDown before
                            // any row-select Event.Use() on leftover chrome.
                            if (isPrimary && !isRenamingClip)
                            {
                                bool nextOpen = EditorGUI.Foldout(foldRect, showDetail,
                                    GUIContent.none, true);
                                if (nextOpen != showDetail)
                                {
                                    _clipRowDetailsExpanded = nextOpen;
                                    if (nextOpen && !isSelected)
                                        SelectClipCard(i);
                                    Repaint();
                                }
                            }

                            if (isRenamingClip)
                            {
                                _clipRenameFieldRect = clipNameRect;
                                _hasClipRenameFieldRect = true;
                                DrawInlineRenameField(clipNameRect, ClipRenameControl,
                                    ref _renameClipValue, ref _focusClipRename, EditorStyles.textField);
                            }
                            else
                            {
                                string clipName = string.IsNullOrWhiteSpace(clip.Name)
                                    ? $"Clip {i + 1}"
                                    : clip.Name;
                                string clipLabel = $"[{i}] {clipName}";
                                string tip = showDetail
                                    ? $"{clipLabel}\nPrimary selection (detail expanded). F2 / double-click name to rename. Ctrl/Cmd multi, Shift range."
                                    : isPrimary
                                        ? $"{clipLabel}\nPrimary selection (detail collapsed — use ▸ to expand). F2 / double-click name to rename."
                                        : $"{clipLabel}\nClick to select. Ctrl/Cmd toggle, Shift range. F2 / double-click name to rename.";
                                GUI.Label(clipNameRect, new GUIContent(clipLabel, tip),
                                    EditorStyles.boldLabel);
                            }

                            if (!isRenamingClip)
                            {
                                // ✕ immediately after the name column.
                                Color prevGui = GUI.color;
                                bool delHover = deleteRect.Contains(input.mousePosition);
                                if (delHover)
                                    GUI.color = new Color(1f, 0.42f, 0.42f, 1f);
                                if (GUI.Button(deleteRect,
                                    new GUIContent("✕",
                                        _selectedClips.Count > 1 && _selectedClips.Contains(i)
                                            ? $"Delete {_selectedClips.Count} selected clips."
                                            : "Delete clip"),
                                    EditorStyles.miniButton))
                                    pendingDeleteClip = i;
                                GUI.color = prevGui;
                            }

                            if (showDetail && !isRenamingClip)
                            {
                                if (DrawClipRowDetail(detailRect, clip, i))
                                    pendingDuplicateClip = i;
                            }

                            // Leftover clicks on name chrome only — never Use() on ✕ / fold /
                            // expanded detail (EditorGUI controls must process those first).
                            if (!isRenamingClip &&
                                input.type == EventType.MouseDown &&
                                !deleteRect.Contains(input.mousePosition) &&
                                !(isPrimary && foldRect.Contains(input.mousePosition)) &&
                                !(showDetail && detailRect.height > 0f &&
                                  detailRect.Contains(input.mousePosition)))
                            {
                                if (input.button == 0 &&
                                    clipNameRect.Contains(input.mousePosition))
                                {
                                    bool toggle = input.control || input.command;
                                    bool range = input.shift;
                                    SelectClipCard(i, toggle, range);
                                    if (!toggle && !range && input.clickCount >= 2)
                                    {
                                        BeginClipRename(i);
                                        input.Use();
                                        GUIUtility.ExitGUI();
                                    }
                                    input.Use();
                                }
                                else if (input.button == 1 &&
                                         headerRow.Contains(input.mousePosition))
                                {
                                    if (!_selectedClips.Contains(i))
                                        SelectClipCard(i);
                                    ShowClipListContextMenu(i);
                                    input.Use();
                                }
                            }
                            clipY += rowH;
                        }
                    }

                    if (pendingDuplicateClip >= 0)
                    {
                        CommitAllRenames();
                        SelectClipCard(pendingDuplicateClip);
                        DuplicateClip();
                    }

                    var actionBar = new Rect(inset.x + 4f, inset.yMax - insetMargin - actionH,
                        inset.width - 8f, actionH);
                    bool canMutate = CurrentClip != null && CurrentClip.SheetIndex == s;
                    DrawClipInsetActions(actionBar, canMutate);
                    if (pendingDeleteClip >= 0)
                    {
                        CancelAllRenames();
                        if (_selectedClips.Count > 1 && _selectedClips.Contains(pendingDeleteClip))
                        {
                            int n = _selectedClips.Count;
                            if (EditorUtility.DisplayDialog(
                                "Delete Clips",
                                $"Delete {n} selected clips? This cannot be undone except via Undo.",
                                "Delete", "Cancel"))
                                DeleteSelectedClips();
                        }
                        else
                            DeleteClipAt(pendingDeleteClip);
                    }

                    if (!stackAddSheet)
                    {
                        var addRect = new Rect(cardRect.xMax - cardPad - addSheetW,
                            inset.yMax + 4f, addSheetW, addSheetH);
                        if (GUI.Button(addRect, "+ Sheet", _transportStyle))
                        {
                            CommitAllRenames();
                            AddSheet();
                        }
                    }
                }

                y += cardH + cardGap;
            }
            GUI.EndScrollView();

            if (stackAddSheet)
            {
                float btnW = Mathf.Min(addSheetW, Mathf.Max(48f, rect.width - 16f));
                var addRect = new Rect(rect.xMax - 8f - btnW, rect.yMax - 34f, btnW, addSheetH);
                if (GUI.Button(addRect, "+ Sheet", _transportStyle))
                {
                    CommitAllRenames();
                    AddSheet();
                }
            }
        }

        void DrawClipInsetActions(Rect bar, bool canMutateClip)
        {
            float gap = 3f;
            float rowH = 26f;
            var top = new Rect(bar.x, bar.y, bar.width, rowH);
            var bottom = new Rect(bar.x, bar.y + rowH + 4f, bar.width, rowH);
            float w1 = 48f, w2 = 64f, w3 = 48f, w4 = 56f;
            float need = w1 + w2 + w3 + w4 + gap * 3f;
            if (need > top.width && top.width > 40f)
            {
                float scale = top.width / need;
                w1 *= scale;
                w2 *= scale;
                w3 *= scale;
                w4 *= scale;
            }
            float x = top.x;
            if (GUI.Button(new Rect(x, top.y, w1, top.height), "+ Clip", _transportStyle))
            {
                CommitAllRenames();
                AddClip();
            }
            x += w1 + gap;
            using (new EditorGUI.DisabledScope(!canMutateClip))
            {
                string dupLabel = _selectedClips.Count > 1 ? $"Dup {_selectedClips.Count}" : "Duplicate";
                string delLabel = _selectedClips.Count > 1 ? $"Del {_selectedClips.Count}" : "Delete";
                if (GUI.Button(new Rect(x, top.y, w2, top.height), dupLabel, _transportStyle))
                {
                    CommitAllRenames();
                    DuplicateSelectedClips();
                }
                x += w2 + gap;
                if (GUI.Button(new Rect(x, top.y, w3, top.height), delLabel, _transportStyle))
                {
                    CancelAllRenames();
                    DeleteSelectedClips();
                }
            }
            x += w3 + gap;
            if (GUI.Button(new Rect(x, top.y, w4, top.height),
                new GUIContent("Action", "Sheet actions: duplicate sheet, add event marker, delete sheet."),
                _transportStyle))
            {
                CommitAllRenames();
                ShowSheetActionMenu();
            }

            int cols = Mathf.Max(1, _profile.Columns);
            int rows = Mathf.Max(1, _profile.Rows);
            float half = (bottom.width - gap) * 0.5f;
            if (GUI.Button(new Rect(bottom.x, bottom.y, half, bottom.height),
                new GUIContent($"{rows} from rows",
                    $"Create one clip per sheet row ({rows} clips × {cols} frames). Skips empty rows and rows that already have a clip."),
                EditorStyles.miniButton))
            {
                CommitAllRenames();
                CreateClipsFromSheetRows();
            }
            if (GUI.Button(new Rect(bottom.x + half + gap, bottom.y, half, bottom.height),
                new GUIContent($"{cols} from cols",
                    $"Create one clip per sheet column ({cols} clips that play down the column). Skips empty columns."),
                EditorStyles.miniButton))
            {
                CommitAllRenames();
                CreateClipsFromSheetColumns();
            }
        }

        void ShowClipListContextMenu(int clipIndex)
        {
            if (clipIndex < 0 || _profile?.Clips == null || clipIndex >= _profile.Clips.Count)
                return;
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Duplicate"), false, () =>
            {
                _selectedClip = clipIndex;
                DuplicateClip();
            });
            menu.AddItem(new GUIContent("Delete"), false, () => DeleteClipAt(clipIndex));
            menu.ShowAsContext();
        }

        void HandleBrowserRenameKeys(Event input)
        {
            string focused = GUI.GetNameOfFocusedControl();
            if (input.type == EventType.MouseDown && input.button == 0 && IsRenamingAnything())
            {
                // Click-away commits the active inline rename (same as Enter).
                // Prefer rect hit-test: Focus()/other controls can clear the focused name
                // before we run, which previously false-committed on the rename field click.
                bool onRenameField =
                    focused == ClipRenameControl ||
                    focused == SheetRenameControl ||
                    focused == SocketNameRenameControl ||
                    focused == SocketIdRenameControl ||
                    focused == InventoryRenameControl ||
                    focused == EventRenameControl ||
                    _focusClipRename ||
                    _focusSheetRename ||
                    _focusSocketNameRename ||
                    _focusSocketIdRename ||
                    _focusInventoryRename ||
                    _focusEventRename ||
                    (_renamingClip >= 0 && _hasClipRenameFieldRect &&
                     _clipRenameFieldRect.Contains(input.mousePosition));
                if (!onRenameField)
                {
                    CommitAllRenames();
                    // do not Use() - let the click select underneath
                }
            }

            if (input.type == EventType.KeyDown &&
                (input.keyCode == KeyCode.Return || input.keyCode == KeyCode.KeypadEnter))
            {
                if (_renamingSheet >= 0 && focused == SheetRenameControl)
                {
                    CommitSheetRename();
                    input.Use();
                    return;
                }
                if (_renamingClip >= 0 && focused == ClipRenameControl)
                {
                    CommitClipRename();
                    input.Use();
                    return;
                }
                if (!string.IsNullOrEmpty(_renamingSocketName) && focused == SocketNameRenameControl)
                {
                    CommitSocketNameRename();
                    input.Use();
                    return;
                }
                if (!string.IsNullOrEmpty(_renamingSocketId) && focused == SocketIdRenameControl)
                {
                    CommitSocketIdRename();
                    input.Use();
                    return;
                }
                if (_renamingInventoryIndex >= 0 && focused == InventoryRenameControl)
                {
                    CommitInventoryRename();
                    input.Use();
                    return;
                }
                if (_renamingEventId != 0 && focused == EventRenameControl)
                {
                    CommitEventRename();
                    input.Use();
                    return;
                }
            }

            if (input.type == EventType.KeyDown && input.keyCode == KeyCode.Escape)
            {
                if (_renamingSheet >= 0 && focused == SheetRenameControl)
                {
                    CancelSheetRename();
                    input.Use();
                    return;
                }
                if (_renamingClip >= 0 && focused == ClipRenameControl)
                {
                    CancelClipRename();
                    input.Use();
                    return;
                }
                if (!string.IsNullOrEmpty(_renamingSocketName) && focused == SocketNameRenameControl)
                {
                    CancelSocketNameRename();
                    input.Use();
                    return;
                }
                if (!string.IsNullOrEmpty(_renamingSocketId) && focused == SocketIdRenameControl)
                {
                    CancelSocketIdRename();
                    input.Use();
                    return;
                }
                if (_renamingInventoryIndex >= 0 && focused == InventoryRenameControl)
                {
                    CancelInventoryRename();
                    input.Use();
                    return;
                }
                if (_renamingEventId != 0 && focused == EventRenameControl)
                {
                    CancelEventRename();
                    input.Use();
                    return;
                }
            }

            if (input.type == EventType.KeyDown && input.keyCode == KeyCode.F2 &&
                !IsRenamingAnything() && !IsEditingStringTextField())
            {
                if (TryBeginPreferredRename())
                    input.Use();
            }
        }

        bool IsRenamingAnything()
            => _renamingClip >= 0
               || _renamingSheet >= 0
               || !string.IsNullOrEmpty(_renamingSocketName)
               || !string.IsNullOrEmpty(_renamingSocketId)
               || _renamingInventoryIndex >= 0
               || _renamingEventId != 0
               || _pendingEventRename;

        bool TryBeginPreferredRename()
        {
            if (!string.IsNullOrEmpty(_selectedSocketName))
            {
                BeginSocketNameRename(_selectedSocketName);
                return true;
            }
            if (_renameInventoryTargetIndex >= 0 &&
                _profile?.SocketInventories != null &&
                _renameInventoryTargetIndex < _profile.SocketInventories.Count)
            {
                BeginInventoryRename(_renameInventoryTargetIndex);
                return true;
            }
            SyncEventTypeSelection();
            if (_selectedEventTypeIndex >= 0 &&
                _profile?.Events != null &&
                _selectedEventTypeIndex < _profile.Events.Count)
            {
                var definition = _profile.Events[_selectedEventTypeIndex];
                if (definition != null && definition.Id != 0)
                {
                    BeginEventRename(definition.Id);
                    return true;
                }
            }
            if (CurrentClip != null)
            {
                BeginClipRename(_selectedClip);
                return true;
            }
            if (_profile?.Sheets != null && _profile.Sheets.Count > 0)
            {
                BeginSheetRename(_selectedSheet);
                return true;
            }
            return false;
        }

        void EnsureSheetFoldState()
        {
            if (_sheetFoldInitialized || _profile?.Sheets == null)
                return;
            _sheetFoldInitialized = true;
            _collapsedSheets.Clear();
            if (_profile.Sheets.Count > 1)
            {
                for (int i = 0; i < _profile.Sheets.Count; i++)
                {
                    if (i != _selectedSheet)
                        _collapsedSheets.Add(i);
                }
            }
        }

        bool IsSheetExpanded(int sheetIndex) => !_collapsedSheets.Contains(sheetIndex);

        void ToggleSheetFold(int sheetIndex)
        {
            if (_collapsedSheets.Contains(sheetIndex))
                _collapsedSheets.Remove(sheetIndex);
            else
                _collapsedSheets.Add(sheetIndex);
        }

        int CountClipsOnSheet(int sheetIndex)
        {
            int n = 0;
            if (_profile?.Clips == null)
                return 0;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                if (_profile.Clips[i] != null && _profile.Clips[i].SheetIndex == sheetIndex)
                    n++;
            }
            return n;
        }

        void CacheSheetClipCounts(int sheetCount)
        {
            _sheetClipCounts.Clear();
            for (int i = 0; i < sheetCount; i++)
                _sheetClipCounts.Add(0);
            if (_profile?.Clips == null)
                return;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var clip = _profile.Clips[i];
                if (clip != null && clip.SheetIndex >= 0 && clip.SheetIndex < sheetCount)
                    _sheetClipCounts[clip.SheetIndex]++;
            }
        }

        int FirstClipIndexOfSheet(int sheetIndex)
        {
            if (_profile?.Clips == null)
                return -1;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                if (_profile.Clips[i] != null && _profile.Clips[i].SheetIndex == sheetIndex)
                    return i;
            }
            return -1;
        }

        void SelectSheetRow(int index)
        {
            if (_profile?.Sheets == null || index < 0 || index >= _profile.Sheets.Count)
                return;
            CommitAllRenames();
            if (_selectedSheet == index)
            {
                ReleaseShortcutKeyboardFocus();
                return;
            }
            _selectedSheet = index;
            _collapsedSheets.Clear();
            if (_profile.Sheets.Count > 1)
            {
                for (int i = 0; i < _profile.Sheets.Count; i++)
                {
                    if (i != index)
                        _collapsedSheets.Add(i);
                }
            }
            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            var current = CurrentClip;
            if (current == null || current.SheetIndex != _selectedSheet)
            {
                int first = FirstClipIndexOfSheet(_selectedSheet);
                if (first >= 0)
                    SelectClipCard(first);
                else
                    ClearClipSelection();
            }
            ReleaseShortcutKeyboardFocus();
            Repaint();
        }

        void ClearClipSelection()
        {
            _selectedClip = -1;
            _selectedFrames.Clear();
            _selectedFrame = 0;
            ClearColliderSelection();
            _selectedEventFrame = -1;
            _selectedEventIndex = -1;
            _selectedOnionFrame = -1;
            _previewTime = 0f;
        }

        void DrawSheetRowThumb(SpriteSheetDef def, Rect rect)
        {
            if (def?.Texture == null)
            {
                EditorGUI.DrawRect(rect, PanelAltColor);
                DrawBorder(rect, BorderColor, 1f);
                return;
            }
            int columns = Mathf.Max(1, def.Columns);
            int rows = Mathf.Max(1, def.Rows);
            DrawCellTinted(def.Texture, 0, rect, Color.white, columns, rows);
            DrawBorder(rect, BorderColor, 1f);
        }

        void AddSheet()
        {
            RecordProfileUndo("Add Sprite Sheet");
            _profile.EnsureSheets(_selectedSheet);
            int n = _profile.Sheets.Count + 1;
            _profile.Sheets.Add(new SpriteSheetDef
            {
                Name = UniqueSheetName($"Sheet {n}"),
                Texture = null,
                Columns = SpriteSheetProfile.DefaultColumns,
                Rows = SpriteSheetProfile.DefaultRows,
                PixelsPerUnit = SpriteSheetProfile.DefaultPixelsPerUnit,
                Pivot = SpriteSheetProfile.DefaultPivot,
            });
            int index = _profile.Sheets.Count - 1;
            _collapsedSheets.Remove(index);
            _selectedSheet = index;
            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            ClearClipSelection();
            _status = $"Added {_profile.Sheets[index].Name}";
            SaveDirty();
            Repaint();
        }

        void ShowSheetActionMenu()
        {
            var menu = new GenericMenu();
            int sheet = _selectedSheet;
            bool multiSheet = _profile?.Sheets != null && _profile.Sheets.Count > 1;
            menu.AddItem(new GUIContent("Duplicate Sheet"), false, () =>
            {
                CommitAllRenames();
                DuplicateSheetAt(sheet);
            });
            if (CurrentClip != null)
            {
                menu.AddItem(new GUIContent("Add Event Marker On Current Frame"), false, () =>
                {
                    CommitAllRenames();
                    AddEventMarkerOnSelectedFrame();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Add Event Marker On Current Frame"));
            }
            if (multiSheet)
            {
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Delete Sheet"), false, () =>
                {
                    CancelAllRenames();
                    if (_profile?.Sheets == null || sheet < 0 || sheet >= _profile.Sheets.Count)
                        return;
                    var def = _profile.Sheets[sheet];
                    string delName = string.IsNullOrWhiteSpace(def?.Name)
                        ? $"Sheet {sheet + 1}"
                        : def.Name;
                    int clipN = 0;
                    if (_profile.Clips != null)
                    {
                        for (int i = 0; i < _profile.Clips.Count; i++)
                        {
                            if (_profile.Clips[i] != null && _profile.Clips[i].SheetIndex == sheet)
                                clipN++;
                        }
                    }
                    string msg = clipN > 0
                        ? $"Delete sheet '{delName}' and its {clipN} clip{(clipN == 1 ? "" : "s")}?"
                        : $"Delete sheet '{delName}'?";
                    if (EditorUtility.DisplayDialog("Delete Sheet", msg, "Delete", "Cancel"))
                        DeleteSheetAt(sheet);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Delete Sheet"));
            }
            menu.ShowAsContext();
        }

        void DuplicateSheetAt(int index)
        {
            if (_profile?.Sheets == null || index < 0 || index >= _profile.Sheets.Count)
                return;
            var src = _profile.Sheets[index];
            if (src == null)
                return;

            RecordProfileUndo("Duplicate Sprite Sheet");
            var copy = new SpriteSheetDef
            {
                Name = UniqueSheetName(string.IsNullOrWhiteSpace(src.Name) ? "Sheet" : src.Name),
                Texture = src.Texture,
                Columns = src.Columns,
                Rows = src.Rows,
                PixelsPerUnit = src.PixelsPerUnit,
                Pivot = src.Pivot,
                CellLayoutMode = src.CellLayoutMode,
                CroppedCellRects = src.CroppedCellRects != null
                    ? (RectInt[])src.CroppedCellRects.Clone()
                    : null,
                CellPivots = src.CellPivots != null
                    ? new List<SpriteCellPivot>(src.CellPivots)
                    : null,
            };
            _profile.Sheets.Add(copy);
            int newIndex = _profile.Sheets.Count - 1;
            if (_profile.Clips != null)
            {
                var extras = new List<SpriteClipDef>();
                for (int i = 0; i < _profile.Clips.Count; i++)
                {
                    var clip = _profile.Clips[i];
                    if (clip == null || clip.SheetIndex != index)
                        continue;
                    extras.Add(CloneClipForSheet(clip, newIndex));
                }
                foreach (var c in extras)
                    _profile.Clips.Add(c);
            }
            _collapsedSheets.Remove(newIndex);
            _selectedSheet = newIndex;
            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            int first = FirstClipIndexOfSheet(_selectedSheet);
            if (first >= 0)
            {
                _selectedClip = first;
                SelectOnlyFrame(0);
            }
            else
                ClearClipSelection();
            _status = $"Duplicated sheet as {copy.Name}";
            SaveDirty();
            Repaint();
        }

        static SpriteClipDef CloneClipForSheet(SpriteClipDef src, int sheetIndex)
        {
            src.EnsureFrameData();
            var clone = new SpriteClipDef
            {
                Name = src.Name,
                SheetIndex = sheetIndex,
                Row = src.Row,
                Frames = src.Frames != null ? (int[])src.Frames.Clone() : new[] { 0 },
                FrameRows = src.FrameRows != null ? (int[])src.FrameRows.Clone() : null,
                FrameRate = src.FrameRate,
                WrapMode = src.WrapMode,
                Interrupt = src.Interrupt,
                CancelAfter = src.CancelAfter,
                Priority = src.Priority,
                OnCompleteClipIndex = src.OnCompleteClipIndex,
                ComboWindowStartFrame = src.ComboWindowStartFrame,
                ComboWindowEndFrame = src.ComboWindowEndFrame,
                ComboWindowPriorityBoost = src.ComboWindowPriorityBoost,
                FrameDurationScales = src.FrameDurationScales != null
                    ? (float[])src.FrameDurationScales.Clone() : null,
                EventIds = src.EventIds != null ? (byte[])src.EventIds.Clone() : null,
                EventNormalizedTimes = src.EventNormalizedTimes != null
                    ? (float[])src.EventNormalizedTimes.Clone() : null,
                OnionOffsets = src.OnionOffsets != null
                    ? (Vector2[])src.OnionOffsets.Clone() : null,
                FrameScales = src.FrameScales != null
                    ? (Vector2[])src.FrameScales.Clone() : null,
                FrameRotations = src.FrameRotations != null
                    ? (float[])src.FrameRotations.Clone() : null,
                FrameTweenModes = src.FrameTweenModes != null
                    ? (byte[])src.FrameTweenModes.Clone() : null,
                FacingGroup = src.FacingGroup,
                Facing = src.Facing,
                Sockets = src.Sockets != null
                    ? new List<FrameSocketDef>(src.Sockets) : new List<FrameSocketDef>(),
                EventMarkers = src.EventMarkers != null
                    ? new List<SpriteClipEventMarker>(src.EventMarkers)
                    : new List<SpriteClipEventMarker>(),
            };
            clone.EnsureFrameData();
            return clone;
        }

        void AddEventMarkerOnSelectedFrame()
        {
            var clip = CurrentClip;
            if (clip == null)
            {
                _status = "Select a clip before adding an event marker.";
                Repaint();
                return;
            }
            clip.EnsureFrameData();
            int frame = Mathf.Clamp(_selectedFrame, 0, Mathf.Max(0, clip.Frames.Length - 1));
            RecordProfileUndo("Add Event Marker");
            byte eventId = 0;
            if (_profile.Events != null && _profile.Events.Count > 0)
                eventId = _profile.Events[0].Id;
            float normalized = 0f;
            if (clip.EventNormalizedTimes != null &&
                frame < clip.EventNormalizedTimes.Length)
                normalized = clip.EventNormalizedTimes[frame];
            clip.AddEventMarker(frame, eventId, normalized);
            _status = $"Added event marker on frame {frame}";
            SaveDirty();
            Repaint();
        }

        void DeleteSheetAt(int index)
        {
            if (_profile?.Sheets == null || index < 0 || index >= _profile.Sheets.Count)
                return;
            if (_profile.Sheets.Count <= 1)
                return;

            RecordProfileUndo("Delete Sprite Sheet");
            string sheetName = _profile.Sheets[index]?.Name ?? $"Sheet {index + 1}";
            if (_profile.Clips != null)
            {
                for (int i = _profile.Clips.Count - 1; i >= 0; i--)
                {
                    var clip = _profile.Clips[i];
                    if (clip == null || clip.SheetIndex != index)
                        continue;
                    if (_profile.Hitboxes != null)
                        _profile.Hitboxes.RemoveAll(box => box.ClipName == clip.Name);
                    _profile.Clips.RemoveAt(i);
                    if (i < _selectedClip)
                        _selectedClip--;
                    else if (i == _selectedClip)
                        _selectedClip = -1;
                    if (_renamingClip == i)
                        ClearClipRename();
                    else if (_renamingClip > i)
                        _renamingClip--;
                }
                for (int i = 0; i < _profile.Clips.Count; i++)
                {
                    if (_profile.Clips[i] != null && _profile.Clips[i].SheetIndex > index)
                        _profile.Clips[i].SheetIndex--;
                }
            }
            _profile.Sheets.RemoveAt(index);
            var nextCollapsed = new HashSet<int>();
            foreach (int collapsed in _collapsedSheets)
            {
                if (collapsed == index)
                    continue;
                nextCollapsed.Add(collapsed > index ? collapsed - 1 : collapsed);
            }
            _collapsedSheets.Clear();
            foreach (int collapsed in nextCollapsed)
                _collapsedSheets.Add(collapsed);

            if (_selectedSheet == index)
                _selectedSheet = Mathf.Clamp(index, 0, _profile.Sheets.Count - 1);
            else if (_selectedSheet > index)
                _selectedSheet--;
            _selectedSheet = Mathf.Clamp(_selectedSheet, 0, Mathf.Max(0, _profile.Sheets.Count - 1));
            if (_renamingSheet == index)
                ClearSheetRename();
            else if (_renamingSheet > index)
                _renamingSheet--;

            _profile.SyncLegacyFromSheet(_selectedSheet);
            InvalidateSheetPixelCache();
            var current = CurrentClip;
            if (current == null || current.SheetIndex != _selectedSheet)
            {
                int first = FirstClipIndexOfSheet(_selectedSheet);
                if (first >= 0)
                {
                    _selectedClip = first;
                    SelectOnlyFrame(0);
                }
                else
                    ClearClipSelection();
            }
            _status = $"Deleted sheet {sheetName}";
            SaveDirty();
            Repaint();
        }

        void BeginSheetRename(int sheetIndex)
        {
            if (_profile?.Sheets == null || sheetIndex < 0 || sheetIndex >= _profile.Sheets.Count)
                return;
            CommitAllRenames();
            _selectedSheet = sheetIndex;
            _renamingSheet = sheetIndex;
            _renameSheetOriginal = _profile.Sheets[sheetIndex]?.Name ?? string.Empty;
            _renameSheetValue = string.IsNullOrWhiteSpace(_renameSheetOriginal)
                ? $"Sheet {sheetIndex + 1}"
                : _renameSheetOriginal;
            _focusSheetRename = true;
            Repaint();
        }

        void CommitSheetRename()
        {
            if (_renamingSheet < 0 || _profile?.Sheets == null ||
                _renamingSheet >= _profile.Sheets.Count)
            {
                ClearSheetRename();
                return;
            }
            var def = _profile.Sheets[_renamingSheet];
            string newName = UniqueSheetName(_renameSheetValue, _renamingSheet);
            if (def != null && !string.Equals(def.Name, newName, StringComparison.Ordinal))
            {
                SyncWorkingProfileToAsset();
                RecordDiscreteUndo("Rename Sprite Sheet");
                def.Name = newName;
                SyncWorkingProfileToAsset();
                _status = $"Renamed sheet to {newName}";
                SaveDirty();
                SealUndoGroup();
            }
            ClearSheetRename();
        }

        void CancelSheetRename()
        {
            if (_renamingSheet >= 0)
                _status = $"Kept sheet name {_renameSheetOriginal}";
            ClearSheetRename();
        }

        void ClearSheetRename()
        {
            _renamingSheet = -1;
            _renameSheetValue = string.Empty;
            _renameSheetOriginal = string.Empty;
            _focusSheetRename = false;
            GUI.FocusControl(null);
            Repaint();
        }

        string UniqueSheetName(string requestedName, int ignoredSheetIndex = -1)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName)
                ? $"Sheet {Mathf.Max(1, ignoredSheetIndex + 1)}"
                : requestedName.Trim();
            string candidate = baseName;
            int suffix = 2;
            while (SheetNameExists(candidate, ignoredSheetIndex))
                candidate = $"{baseName} {suffix++}";
            return candidate;
        }

        bool SheetNameExists(string candidate, int ignoredSheetIndex)
        {
            if (_profile?.Sheets == null)
                return false;
            for (int i = 0; i < _profile.Sheets.Count; i++)
            {
                if (i == ignoredSheetIndex || _profile.Sheets[i] == null)
                    continue;
                if (string.Equals(_profile.Sheets[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        void CommitAllRenames()
        {
            if (_renamingClip >= 0)
                CommitClipRename();
            if (_renamingSheet >= 0)
                CommitSheetRename();
            if (!string.IsNullOrEmpty(_renamingSocketName))
                CommitSocketNameRename();
            if (!string.IsNullOrEmpty(_renamingSocketId))
                CommitSocketIdRename();
            if (_renamingInventoryIndex >= 0)
                CommitInventoryRename();
            if (_renamingEventId != 0)
                CommitEventRename();
        }

        void CancelAllRenames()
        {
            if (_renamingClip >= 0)
                CancelClipRename();
            if (_renamingSheet >= 0)
                CancelSheetRename();
            if (!string.IsNullOrEmpty(_renamingSocketName))
                CancelSocketNameRename();
            if (!string.IsNullOrEmpty(_renamingSocketId))
                CancelSocketIdRename();
            if (_renamingInventoryIndex >= 0)
                CancelInventoryRename();
            if (_renamingEventId != 0)
                CancelEventRename();
        }

        void WriteActiveSheetFromLegacy()
        {
            if (_profile?.Sheets == null || _profile.Sheets.Count == 0)
                return;
            int index = Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1);
            var before = _profile.Sheets[index];
            Texture2D oldTex = before != null ? before.Texture : null;
            int oldCols = before != null ? before.Columns : 0;
            int oldRows = before != null ? before.Rows : 0;
            _profile.WriteLegacyIntoSheet(index);
            var after = _profile.Sheets[index];
            if (after == null || after.Texture != oldTex || after.Columns != oldCols || after.Rows != oldRows)
                InvalidateSheetPixelCache();
        }

        int WorldSizeSourceForTextureAssign(int assignedIndex)
        {
            if (_profile?.Sheets == null)
                return assignedIndex;
            for (int i = 0; i < _profile.Sheets.Count; i++)
            {
                if (i == assignedIndex)
                    continue;
                if (_profile.Sheets[i]?.Texture != null)
                    return i;
            }
            return assignedIndex;
        }

        void RematchSheetsWorldSize(int sourceSheetIndex)
        {
            if (_profile?.Sheets == null || _profile.Sheets.Count == 0)
                return;
            var source = _profile.SheetAt(sourceSheetIndex);
            if (source?.Texture == null)
                return;
            _profile.MatchSheetsWorldSize(sourceSheetIndex);
            _profile.SyncLegacyFromSheet(Mathf.Clamp(_selectedSheet, 0, _profile.Sheets.Count - 1));
        }


        float MeasureSheetClipRowsHeight(int sheetIndex, int clipCount, float baseRowH)
        {
            float h = 0f;
            if (_profile?.Clips == null)
                return h;
            SyncClipMultiSelection();
            for (int i = 0; i < clipCount; i++)
            {
                var clip = _profile.Clips[i];
                if (clip == null || clip.SheetIndex != sheetIndex)
                    continue;
                bool isPrimary = i == _selectedClip;
                bool isSelected = _selectedClips.Contains(i) || isPrimary;
                bool showDetail = isPrimary && isSelected && _clipRowDetailsExpanded;
                h += baseRowH;
                if (showDetail)
                    h += MeasureClipRowDetailHeight(clip);
            }
            return h;
        }

        float MeasureClipRowDetailHeight(SpriteClipDef clip)
        {
            float line = EditorGUIUtility.singleLineHeight + 3f;
            // Single-column stacked detail (full width):
            // FPS, Wrap, Interrupt, [Cancel After], Priority, Frames, On Done,
            // Combo Start, Combo End, Boost, Duplicate.
            int rows = 10; // base fields + Duplicate row
            if (clip != null && clip.Interrupt == (byte)SpriteClipInterrupt.AfterTime)
                rows++;
            return 8f + rows * line;
        }

        /// <summary>
        /// Collider-style indented detail under the primary clip row (single column).
        /// Returns true when Duplicate was pressed.
        /// </summary>
        bool DrawClipRowDetail(Rect rect, SpriteClipDef clip, int clipIndex)
        {
            _ = clipIndex;
            if (clip == null || rect.height < 8f)
                return false;

            bool duplicate = false;
            GUILayout.BeginArea(rect);
            float prevLabelWidth = EditorGUIUtility.labelWidth;
            // Full-width single column: keep labels narrow so controls stay clickable.
            // Default labelWidth (150) still crushes IntField/Popup on typical clip cards.
            EditorGUIUtility.labelWidth = Mathf.Clamp(rect.width * 0.36f, 72f, 100f);
            try
            {
                // Order: FPS → Wrap → Interrupt (+ Cancel After) → Priority → Frames →
                // On Done → Combo Start → Combo End → Boost → Duplicate.
                Rect fpsRow = EditorGUILayout.GetControlRect();
                Rect fpsFieldRect = EditorGUI.PrefixLabel(fpsRow,
                    new GUIContent("FPS", ClipFpsTooltip));
                DrawClipFpsField(fpsFieldRect, clip);

                EditorGUI.BeginChangeCheck();
                byte wrap = (byte)EditorGUILayout.Popup(
                    new GUIContent("Wrap", "Loop / Once / Ping Pong / Reverse Loop / Reverse Once"),
                    clip.WrapMode,
                    new[] { "Loop", "Once", "Ping Pong", "Reverse Loop", "Reverse Once" });
                if (EditorGUI.EndChangeCheck())
                {
                    RecordProfileUndo("Set Sprite Clip Wrap Mode");
                    clip.WrapMode = wrap;
                    SaveDirty();
                }

                EditorGUI.BeginChangeCheck();
                byte interrupt = (byte)EditorGUILayout.Popup(
                    new GUIContent("Interrupt",
                        "Always = locomotion. Never = hard cast/death. AfterTime = cancel window."),
                    clip.Interrupt,
                    new[] { "Always", "Never", "After Time" });
                if (EditorGUI.EndChangeCheck())
                {
                    RecordProfileUndo("Set Sprite Clip Interrupt");
                    clip.Interrupt = interrupt;
                    SaveDirty();
                }

                if (clip.Interrupt == (byte)SpriteClipInterrupt.AfterTime)
                {
                    EditorGUI.BeginChangeCheck();
                    float cancelAfter = EditorGUILayout.Slider(
                        new GUIContent("Cancel After",
                            "Normalized 0-1. Play() blocked until this point unless force."),
                        clip.CancelAfter, 0f, 1f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        RecordProfileUndo("Set Sprite Clip Cancel After");
                        clip.CancelAfter = cancelAfter;
                        SaveDirty();
                    }
                }

                EditorGUI.BeginChangeCheck();
                int priority = EditorGUILayout.IntField(
                    new GUIContent("Priority",
                        "Higher priority blocks lower-priority Play() while playing (!force)."),
                    clip.Priority);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordProfileUndo("Set Sprite Clip Priority");
                    clip.Priority = priority;
                    SaveDirty();
                }

                int frameCount = clip.Frames?.Length ?? 0;
                EditorGUILayout.LabelField(
                    new GUIContent("Frames", "Frame count for this clip (readonly)."),
                    new GUIContent(frameCount.ToString()),
                    EditorStyles.miniLabel);

                DrawOnCompleteClipFieldCompact(clip);

                EditorGUI.BeginChangeCheck();
                int comboStart = EditorGUILayout.IntField(
                    new GUIContent("Combo Start",
                        "Inclusive start frame. Combo window disabled while End < 0."),
                    clip.ComboWindowStartFrame);
                int comboEnd = EditorGUILayout.IntField(
                    new GUIContent("Combo End",
                        "Inclusive end frame. Set to -1 to disable."),
                    clip.ComboWindowEndFrame);
                int comboBoost = EditorGUILayout.IntField(
                    new GUIContent("Boost",
                        "While inside the window, subtract from Priority for Play gating."),
                    clip.ComboWindowPriorityBoost);
                if (EditorGUI.EndChangeCheck())
                {
                    RecordProfileUndo("Set Sprite Clip Combo Window");
                    clip.ComboWindowStartFrame = comboStart;
                    clip.ComboWindowEndFrame = comboEnd;
                    clip.ComboWindowPriorityBoost = comboBoost;
                    SaveDirty();
                }

                if (GUILayout.Button(
                    new GUIContent("Duplicate this clip", "Duplicate the primary clip."),
                    EditorStyles.miniButton))
                    duplicate = true;
            }
            finally
            {
                EditorGUIUtility.labelWidth = prevLabelWidth;
                GUILayout.EndArea();
            }
            return duplicate;
        }

        void DrawOnCompleteClipFieldCompact(SpriteClipDef clip)
        {
            if (clip == null || _profile?.Clips == null)
                return;

            var names = new List<string> { "(None)" };
            var indices = new List<int> { -1 };
            int selected = 0;
            for (int i = 0; i < _profile.Clips.Count; i++)
            {
                var other = _profile.Clips[i];
                if (other == null)
                    continue;
                string label = string.IsNullOrWhiteSpace(other.Name) ? $"Clip {i}" : other.Name;
                names.Add($"{i}: {label}");
                indices.Add(i);
                if (i == clip.OnCompleteClipIndex)
                    selected = names.Count - 1;
            }
            if (clip.OnCompleteClipIndex >= 0 && selected == 0)
            {
                names.Add($"{clip.OnCompleteClipIndex}: (missing)");
                indices.Add(clip.OnCompleteClipIndex);
                selected = names.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            int pick = EditorGUILayout.Popup(
                new GUIContent("On Done",
                    "When Once ends: auto-Play this clip. (None) = -1."),
                selected, names.ToArray());
            if (EditorGUI.EndChangeCheck() && pick >= 0 && pick < indices.Count)
            {
                RecordProfileUndo("Set Sprite Clip On Complete");
                clip.OnCompleteClipIndex = indices[pick];
                SaveDirty();
            }
        }

        void SyncClipMultiSelection()
        {
            if (_profile?.Clips == null)
            {
                _selectedClips.Clear();
                return;
            }
            _selectedClips.RemoveWhere(i => i < 0 || i >= _profile.Clips.Count);
            if (_selectedClip >= 0 && _selectedClip < _profile.Clips.Count)
            {
                if (_selectedClips.Count == 0)
                    _selectedClips.Add(_selectedClip);
                else if (!_selectedClips.Contains(_selectedClip))
                {
                    _selectedClips.Clear();
                    _selectedClips.Add(_selectedClip);
                }
            }
            else if (_selectedClips.Count > 0)
            {
                // Keep primary on first remaining selection.
                int primary = int.MaxValue;
                foreach (int i in _selectedClips)
                    if (i < primary) primary = i;
                if (primary != int.MaxValue)
                    _selectedClip = primary;
            }
        }

        void SelectClipCard(int index) => SelectClipCard(index, false, false);

        void SelectClipCard(int index, bool toggle, bool range)
        {
            if (_profile?.Clips == null || index < 0 || index >= _profile.Clips.Count)
                return;
            if (_renamingClip >= 0 && _renamingClip != index)
                CommitClipRename();
            if (_renamingSheet >= 0)
                CommitSheetRename();
            if (!string.IsNullOrEmpty(_renamingSocketName) ||
                !string.IsNullOrEmpty(_renamingSocketId) ||
                _renamingInventoryIndex >= 0 ||
                _renamingEventId != 0)
                CommitAllRenames();

            if (range && _selectedClip >= 0)
            {
                int a = Mathf.Min(_selectedClip, index);
                int b = Mathf.Max(_selectedClip, index);
                if (!toggle)
                    _selectedClips.Clear();
                for (int i = a; i <= b; i++)
                {
                    var c = _profile.Clips[i];
                    if (c != null && c.SheetIndex == _profile.Clips[index].SheetIndex)
                        _selectedClips.Add(i);
                }
                ApplyPrimaryClip(index, refreshPreview: true);
                return;
            }

            if (toggle)
            {
                if (_selectedClips.Contains(index) && _selectedClips.Count > 1)
                {
                    _selectedClips.Remove(index);
                    if (_selectedClip == index)
                    {
                        int next = int.MaxValue;
                        foreach (int i in _selectedClips)
                            if (i < next) next = i;
                        ApplyPrimaryClip(next == int.MaxValue ? index : next, refreshPreview: true);
                    }
                    else
                        ReleaseShortcutKeyboardFocus();
                    return;
                }
                _selectedClips.Add(index);
                ApplyPrimaryClip(index, refreshPreview: true);
                return;
            }

            // Exclusive select - clip list is now the F2 rename target (not a stale socket).
            _selectedSocketName = null;
            _renameInventoryTargetIndex = -1;
            if (_selectedClip == index && _selectedClips.Count == 1 && _selectedClips.Contains(index))
            {
                if (!IsRenamingAnything())
                    ReleaseShortcutKeyboardFocus();
                return;
            }
            _selectedClips.Clear();
            _selectedClips.Add(index);
            ApplyPrimaryClip(index, refreshPreview: _selectedClip != index);
        }

        void ApplyPrimaryClip(int index, bool refreshPreview)
        {
            if (_profile?.Clips == null || index < 0 || index >= _profile.Clips.Count)
                return;
            if (refreshPreview && _selectedClip != index)
            {
                _selectedOnionFrame = -1;
                ClearColliderSelection();
                _selectedEventFrame = -1;
                _selectedEventIndex = -1;
            }
            bool primaryChanged = _selectedClip != index;
            _selectedClip = index;
            _selectedClips.Add(index);
            if (primaryChanged)
                _clipRowDetailsExpanded = true;
            var clip = _profile.Clips[index];
            if (clip != null && _profile.Sheets != null && _profile.Sheets.Count > 0)
            {
                _selectedSheet = Mathf.Clamp(clip.SheetIndex, 0, _profile.Sheets.Count - 1);
                _collapsedSheets.Remove(_selectedSheet);
                _profile.SyncLegacyFromSheet(_selectedSheet);
                InvalidateSheetPixelCache();
            }
            if (refreshPreview)
            {
                SelectOnlyFrame(0);
                _previewTime = 0f;
            }
            ReleaseShortcutKeyboardFocus();
        }

        void BeginClipRename(int clipIndex)
        {
            if (clipIndex < 0 || clipIndex >= _profile.Clips.Count)
                return;

            CommitAllRenames();

            _selectedClip = clipIndex;
            _selectedClips.Clear();
            _selectedClips.Add(clipIndex);
            _renamingClip = clipIndex;
            _renameClipOriginal = _profile.Clips[clipIndex].Name;
            _renameClipValue = string.IsNullOrWhiteSpace(_renameClipOriginal)
                ? $"Clip {clipIndex + 1}"
                : _renameClipOriginal;
            _focusClipRename = true;
            Repaint();
        }

        void CommitClipRename()
        {
            if (_renamingClip < 0 || _renamingClip >= _profile.Clips.Count)
            {
                ClearClipRename();
                return;
            }

            int clipIndex = _renamingClip;
            var clip = _profile.Clips[clipIndex];
            string oldName = clip.Name;
            string newName = UniqueClipName(_renameClipValue, clipIndex);
            if (!string.Equals(oldName, newName, StringComparison.Ordinal))
            {
                // Discrete complete-object undo + seal: DrawInspector's PrepareInspectorUndo
                // runs later on the same KeyDown/MouseDown and would otherwise RecordObject
                // after the Name write, leaving Edit>Undo / Ctrl+Z unable to restore.
                SyncWorkingProfileToAsset();
                RecordDiscreteUndo("Rename Clip");
                clip.Name = newName;
                RenameHitboxClip(oldName, newName);
                SyncWorkingProfileToAsset();
                _status = $"Renamed clip to {newName}";
                SaveDirty();
                SealUndoGroup();
            }
            ClearClipRename();
        }

        void CancelClipRename()
        {
            if (_renamingClip >= 0)
                _status = $"Kept clip name {_renameClipOriginal}";
            ClearClipRename();
        }

        void ClearClipRename()
        {
            _renamingClip = -1;
            _renameClipValue = string.Empty;
            _renameClipOriginal = string.Empty;
            _focusClipRename = false;
            _hasClipRenameFieldRect = false;
            GUI.FocusControl(null);
            Repaint();
        }

        void BeginSocketNameRename(string socketName)
        {
            socketName = SpriteSocketKeys.CanonicalName(socketName);
            if (string.IsNullOrEmpty(socketName))
                return;
            CommitAllRenames();
            _selectedSocketName = socketName;
            _renamingSocketName = socketName;
            _renameSocketNameOriginal = socketName;
            _renameSocketNameValue = socketName;
            _focusSocketNameRename = true;
            Repaint();
        }

        void CommitSocketNameRename()
        {
            if (string.IsNullOrEmpty(_renamingSocketName))
            {
                ClearSocketNameRename();
                return;
            }
            string previousName = _renamingSocketName;
            string nextName = SpriteSocketKeys.CanonicalName(_renameSocketNameValue);
            if (string.IsNullOrEmpty(nextName))
                nextName = previousName;
            if (!SpriteSocketKeys.NamesEqual(nextName, previousName))
            {
                SyncWorkingProfileToAsset();
                RecordDiscreteUndo("Rename Socket");
                var clip = CurrentClip;
                if (clip != null)
                    SpriteSocketKeys.RenameIdentity(clip.Sockets, previousName, nextName);
                var motion = _profile?.FindSocketMotion(previousName);
                if (motion != null)
                    motion.SocketName = nextName;
                _selectedSockets.Remove(SpriteSocketKeys.CanonicalName(previousName));
                _selectedSockets.Add(nextName);
                _selectedSocketName = nextName;
                bool oldStillUsed = SpriteSocketKeys.NameExistsOnAnyClip(_profile?.Clips, previousName);
                _profile?.SocketCatalog?.SyncRename(previousName, nextName, oldStillUsed);
                // Keep inventory membership in sync
                if (_profile?.SocketInventories != null)
                {
                    for (int i = 0; i < _profile.SocketInventories.Count; i++)
                    {
                        var inv = _profile.SocketInventories[i];
                        if (inv?.SocketNames == null)
                            continue;
                        for (int n = 0; n < inv.SocketNames.Count; n++)
                        {
                            if (SpriteSocketKeys.NamesEqual(inv.SocketNames[n], previousName))
                                inv.SocketNames[n] = nextName;
                        }
                    }
                }
                SyncWorkingProfileToAsset();
                _status = $"Renamed socket to {nextName}";
                SaveDirty();
                SealUndoGroup();
            }
            ClearSocketNameRename();
        }

        void CancelSocketNameRename()
        {
            if (!string.IsNullOrEmpty(_renamingSocketName))
                _status = $"Kept socket name {_renameSocketNameOriginal}";
            ClearSocketNameRename();
        }

        void ClearSocketNameRename()
        {
            _renamingSocketName = null;
            _renameSocketNameValue = string.Empty;
            _renameSocketNameOriginal = string.Empty;
            _focusSocketNameRename = false;
            GUI.FocusControl(null);
            Repaint();
        }

        void BeginSocketIdRename(string socketName)
        {
            socketName = SpriteSocketKeys.CanonicalName(socketName);
            if (string.IsNullOrEmpty(socketName) || _profile == null)
                return;
            CommitAllRenames();
            _profile.EnsureSocketCatalog();
            var item = _profile.SocketCatalog.Ensure(socketName);
            _selectedSocketName = socketName;
            _renamingSocketId = socketName;
            _renameSocketIdOriginal = item.SocketId ?? string.Empty;
            _renameSocketIdValue = _renameSocketIdOriginal;
            _focusSocketIdRename = true;
            Repaint();
        }

        void CommitSocketIdRename()
        {
            if (string.IsNullOrEmpty(_renamingSocketId) || _profile == null)
            {
                ClearSocketIdRename();
                return;
            }
            _profile.EnsureSocketCatalog();
            var catalogItem = _profile.SocketCatalog.Find(_renamingSocketId);
            if (catalogItem == null)
            {
                ClearSocketIdRename();
                return;
            }
            string canonical = SpriteSocketIdUtility.Canonical(_renameSocketIdValue, _renamingSocketId);
            if (!string.Equals(canonical, catalogItem.SocketId, StringComparison.Ordinal))
            {
                if (SocketIdUsedByOther(canonical, catalogItem))
                {
                    _status = $"Socket ID '{canonical}' is already used";
                }
                else
                {
                    SyncWorkingProfileToAsset();
                    RecordDiscreteUndo("Set Socket ID");
                    catalogItem.SocketId = canonical;
                    SyncWorkingProfileToAsset();
                    _status = $"{_renamingSocketId} ID = {canonical}";
                    SaveDirty();
                    SealUndoGroup();
                }
            }
            ClearSocketIdRename();
        }

        void CancelSocketIdRename()
        {
            if (!string.IsNullOrEmpty(_renamingSocketId))
                _status = $"Kept socket ID {_renameSocketIdOriginal}";
            ClearSocketIdRename();
        }

        void ClearSocketIdRename()
        {
            _renamingSocketId = null;
            _renameSocketIdValue = string.Empty;
            _renameSocketIdOriginal = string.Empty;
            _focusSocketIdRename = false;
            GUI.FocusControl(null);
            Repaint();
        }

        void BeginInventoryRename(int inventoryIndex)
        {
            if (_profile?.SocketInventories == null ||
                inventoryIndex < 0 || inventoryIndex >= _profile.SocketInventories.Count)
                return;
            CommitAllRenames();
            var inventory = _profile.SocketInventories[inventoryIndex];
            _renamingInventoryIndex = inventoryIndex;
            _renameInventoryTargetIndex = inventoryIndex;
            _renameInventoryOriginal = inventory?.Name ?? string.Empty;
            _renameInventoryValue = string.IsNullOrWhiteSpace(_renameInventoryOriginal)
                ? $"Group {inventoryIndex + 1}"
                : _renameInventoryOriginal;
            _focusInventoryRename = true;
            Repaint();
        }

        void CommitInventoryRename()
        {
            if (_renamingInventoryIndex < 0 ||
                _profile?.SocketInventories == null ||
                _renamingInventoryIndex >= _profile.SocketInventories.Count)
            {
                ClearInventoryRename();
                return;
            }
            var inventory = _profile.SocketInventories[_renamingInventoryIndex];
            string nextName = string.IsNullOrWhiteSpace(_renameInventoryValue)
                ? (string.IsNullOrWhiteSpace(_renameInventoryOriginal)
                    ? $"Group {_renamingInventoryIndex + 1}"
                    : _renameInventoryOriginal)
                : _renameInventoryValue.Trim();
            if (inventory != null &&
                !string.Equals(nextName, inventory.Name, StringComparison.Ordinal))
            {
                SyncWorkingProfileToAsset();
                RecordDiscreteUndo("Rename Socket Group");
                inventory.Name = nextName;
                SyncWorkingProfileToAsset();
                _status = $"Renamed group to {nextName}";
                SaveDirty();
                SealUndoGroup();
            }
            ClearInventoryRename();
        }

        void CancelInventoryRename()
        {
            if (_renamingInventoryIndex >= 0)
                _status = $"Kept group name {_renameInventoryOriginal}";
            ClearInventoryRename();
        }

        void ClearInventoryRename()
        {
            _renamingInventoryIndex = -1;
            _renameInventoryValue = string.Empty;
            _renameInventoryOriginal = string.Empty;
            _focusInventoryRename = false;
            GUI.FocusControl(null);
            Repaint();
        }

        void QueueEventTypeRename(int index)
        {
            if (_profile?.Events == null || index < 0 || index >= _profile.Events.Count)
                return;
            var definition = _profile.Events[index];
            if (definition == null || definition.Id == 0)
                return;
            _pendingEventRename = true;
            _pendingEventRenameIndex = index;
            SelectEventTypeInCatalog(definition.Id);
            _eventThisClipExpanded = true;
            Repaint();
        }

        void ProcessPendingEventRename()
        {
            if (!_pendingEventRename)
                return;
            _pendingEventRename = false;
            int index = _pendingEventRenameIndex;
            _pendingEventRenameIndex = -1;
            if (_profile?.Events == null || index < 0 || index >= _profile.Events.Count)
                return;
            var definition = _profile.Events[index];
            if (definition == null || definition.Id == 0)
                return;
            SelectEventTypeInCatalog(definition.Id);
            BeginEventRename(definition.Id);
            // Keep focusing for a couple of frames after menu-close steal.
            _focusEventRename = true;
            _status = $"Renaming {EventName(definition.Id)} (F2)";
        }

        void RecordEventUndo(string name)
        {
            SyncWorkingProfileToAsset();
            RecordDiscreteUndo(name);
        }

        void SealEventUndo()
        {
            SyncWorkingProfileToAsset();
            SaveDirty();
            SealUndoGroup();
        }

        void BeginEventRename(byte eventId)
        {
            if (eventId == 0 || _profile?.Events == null)
                return;
            var definition = _profile.Events.Find(e => e != null && e.Id == eventId);
            if (definition == null)
                return;
            CommitAllRenames();
            _pendingEventRename = false;
            _pendingEventRenameIndex = -1;
            _renamingEventId = eventId;
            _renameEventOriginal = definition.Name ?? string.Empty;
            _renameEventValue = string.IsNullOrWhiteSpace(_renameEventOriginal)
                ? $"Event {eventId}"
                : _renameEventOriginal;
            _focusEventRename = true;
            Repaint();
        }

        void CommitEventRename()
        {
            if (_renamingEventId == 0 || _profile?.Events == null)
            {
                ClearEventRename();
                return;
            }
            var definition = _profile.Events.Find(e => e != null && e.Id == _renamingEventId);
            if (definition == null)
            {
                ClearEventRename();
                return;
            }
            string nextName = string.IsNullOrWhiteSpace(_renameEventValue)
                ? $"Event {_renamingEventId}"
                : _renameEventValue.Trim();
            if (!string.Equals(nextName, definition.Name, StringComparison.Ordinal))
            {
                RecordEventUndo("Rename Sprite Event");
                definition.Name = nextName;
                SealEventUndo();
                _status = $"Renamed event to {nextName}";
            }
            ClearEventRename();
        }

        void CancelEventRename()
        {
            if (_renamingEventId != 0)
                _status = $"Kept event name {_renameEventOriginal}";
            ClearEventRename();
        }

        void ClearEventRename()
        {
            _renamingEventId = 0;
            _renameEventValue = string.Empty;
            _renameEventOriginal = string.Empty;
            _focusEventRename = false;
            _pendingEventRename = false;
            _pendingEventRenameIndex = -1;
            GUI.FocusControl(null);
            Repaint();
        }

        void DrawInlineRenameField(Rect rect, string controlName, ref string value,
            ref bool focusFlag, GUIStyle style = null)
        {
            GUI.SetNextControlName(controlName);
            value = GUI.TextField(rect, value, style ?? EditorStyles.textField);
            if (focusFlag || GUI.GetNameOfFocusedControl() != controlName)
            {
                EditorGUI.FocusTextInControl(controlName);
                if (GUI.GetNameOfFocusedControl() == controlName)
                    focusFlag = false;
            }
        }

        bool DrawRenameLabelRow(string label, string display, string tip, out Rect valueRect)
        {
            var row = EditorGUILayout.GetControlRect();
            var labelRect = new Rect(row.x, row.y, EditorGUIUtility.labelWidth, row.height);
            valueRect = new Rect(row.x + EditorGUIUtility.labelWidth, row.y,
                Mathf.Max(20f, row.width - EditorGUIUtility.labelWidth), row.height);
            GUI.Label(labelRect, label);
            GUI.Label(valueRect, new GUIContent(display ?? string.Empty, tip));
            var evt = Event.current;
            if (evt.type == EventType.MouseDown && evt.button == 0 &&
                valueRect.Contains(evt.mousePosition) && evt.clickCount >= 2)
            {
                evt.Use();
                return true;
            }
            return false;
        }

        string UniqueClipName(string requestedName, int ignoredClipIndex)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName)
                ? $"Clip {_profile.Clips.Count + 1}"
                : requestedName.Trim();
            string candidate = baseName;
            int suffix = 2;
            while (ClipNameExists(candidate, ignoredClipIndex))
                candidate = $"{baseName} {suffix++}";
            return candidate;
        }

        bool ClipNameExists(string candidate, int ignoredClipIndex)
        {
            for (int i = 0; i < _profile.Clips.Count; i++)
                if (i != ignoredClipIndex &&
                    string.Equals(_profile.Clips[i].Name, candidate, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

    }
}
