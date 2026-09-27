using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // The hierarchy panel on the left, always open: every element of the panel as a tree, like Unity's Hierarchy -
    // each level one step in from its parent, with a foldout arrow on those that hold children. The panel is as wide
    // as you drag it (its right edge is a splitter) and scrolls both ways, so a deep tree is never cut off.
    //
    // The eye at the start of a row hides the element - and everything in it - in the workspace: it isn't drawn, can't
    // be clicked or boxed in, and the layer rules and guides ignore it, so it never stands in the way of what is
    // designed on the same layer (e.g. several popups laid over the same area, worked on one at a time). It only
    // changes what is seen here: a hidden element is still saved and built. Which are hidden or folded belongs to
    // the window, not to the design, so it makes no undo step and no unsaved change.
    //
    // Clicking a row selects the element (Ctrl + click adds or removes it), a double click frames it. Selecting
    // something in the workspace unfolds the rows above it.
    internal sealed partial class EasyUIWindow
    {
        private const float HierarchyMinWidth = 150f;
        private const float HierarchyMinView = 240f;
        private const float HierarchyHeader = 22f;
        private const float RowHeight = 20f;
        private const float EyeColumn = 24f;
        private const float IndentStep = 14f;
        private const float ArrowColumn = 14f;
        private const float SplitterWidth = 4f;

        private static readonly Color HierarchyColor = new(0.2f, 0.2f, 0.2f);
        private static readonly Color HierarchyHeaderColor = new(0.25f, 0.25f, 0.25f);
        private static readonly Color HierarchySplitterColor = new(0.1f, 0.1f, 0.1f);
        private static readonly Color HierarchySelectedColor = new(0.17f, 0.36f, 0.53f);

        [SerializeField] private float hierarchyWidth = 240f;

        // Hidden by their own eye, and folded, by element id. The ones actually out of sight - the hidden and
        // everything in them - are in _hidden, worked out once per event.
        [SerializeField] private List<int> hiddenIds = new();
        [SerializeField] private List<int> foldedIds = new();

        private readonly HashSet<int> _hidden = new();
        private readonly HashSet<int> _parents = new();
        private readonly List<EasyUINode> _fullOrder = new();
        private readonly List<EasyUINode> _rows = new();

        private Rect _hierarchyRect;
        private Vector2 _hierarchyScroll;
        private bool _resizingHierarchy;
        private int _revealedFor;

        private GUIStyle _rowLabelStyle;
        private GUIStyle _rowHiddenLabelStyle;
        private GUIStyle _headerStyle;

        private GUIStyle RowLabelStyle => _rowLabelStyle ??= new GUIStyle(EditorStyles.label)
        {
            alignment = TextAnchor.MiddleLeft,
            clipping = TextClipping.Overflow,
            wordWrap = false
        };

        private GUIStyle RowHiddenLabelStyle => _rowHiddenLabelStyle ??= new GUIStyle(RowLabelStyle)
        {
            normal = { textColor = new Color(1f, 1f, 1f, 0.4f) }
        };

        private GUIStyle HeaderStyle => _headerStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleLeft
        };

        // Whether the element is out of sight in the workspace: hidden itself, or inside a hidden one.
        private bool IsHidden(EasyUINode node) => _hidden.Contains(node.id);

        #region View

        // Splits the area under the top bar: the hierarchy on the left, the workspace on the right.
        private void SplitView(Rect full)
        {
            var most = Mathf.Max(HierarchyMinWidth, full.width - HierarchyMinView - SplitterWidth);
            var width = Mathf.Clamp(hierarchyWidth, HierarchyMinWidth, most);
            _hierarchyRect = new Rect(full.x, full.y, width, full.height);
            _view = new Rect(full.x + width + SplitterWidth, full.y, Mathf.Max(full.width - width - SplitterWidth, 0f), full.height);
        }

        // Which elements are out of sight; hidden and folded ids of elements that are gone are dropped.
        private void UpdateHidden()
        {
            hiddenIds.RemoveAll(id => document.Find(id) == null);
            foldedIds.RemoveAll(id => document.Find(id) == null);

            _hidden.Clear();
            if (hiddenIds.Count == 0)
            {
                return;
            }

            foreach (var node in document.nodes)
            {
                var current = node;
                for (var guard = 0; current != null && guard < 64; guard++)
                {
                    if (hiddenIds.Contains(current.id))
                    {
                        _hidden.Add(node.id);
                        break;
                    }

                    current = document.Find(current.parentId);
                }
            }
        }

        // A new or opened design starts with everything shown and unfolded.
        private void ResetHierarchy()
        {
            hiddenIds.Clear();
            foldedIds.Clear();
            _hidden.Clear();
            _hierarchyScroll = Vector2.zero;
            _revealedFor = 0;
        }

        #endregion

        #region Drawing

        private void DrawHierarchy()
        {
            var e = Event.current;
            var splitterId = GUIUtility.GetControlID(FocusType.Passive);
            var area = _hierarchyRect;
            if (area.width <= 0f || area.height <= 0f)
            {
                return;
            }

            HandleSplitter(e, splitterId, area);

            // Selecting in the workspace shows the row: everything above it unfolds.
            if (_selectedId != _revealedFor)
            {
                _revealedFor = _selectedId;
                Unfold(document.Find(_selectedId));
            }

            BuildRows();

            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, HierarchyColor);
                EditorGUI.DrawRect(new Rect(area.xMax, area.y, SplitterWidth, area.height), HierarchySplitterColor);
            }

            DrawHierarchyHeader(area, e);

            var scrollArea = new Rect(area.x, area.y + HierarchyHeader, area.width, Mathf.Max(area.height - HierarchyHeader, 0f));
            var hasPointer = scrollArea.Contains(e.mousePosition);
            var content = new Rect(0f, 0f, Mathf.Max(ContentWidth(), scrollArea.width - 16f), _rows.Count * RowHeight);

            _hierarchyScroll = GUI.BeginScrollView(scrollArea, _hierarchyScroll, content);

            var first = Mathf.Clamp(Mathf.FloorToInt(_hierarchyScroll.y / RowHeight), 0, Mathf.Max(_rows.Count - 1, 0));
            var last = Mathf.Min(_rows.Count - 1, first + Mathf.CeilToInt(scrollArea.height / RowHeight) + 1);
            for (var i = first; i <= last; i++)
            {
                DrawRow(_rows[i], i, content.width, e, hasPointer);
            }

            GUI.EndScrollView();
        }

        private void DrawHierarchyHeader(Rect area, Event e)
        {
            var header = new Rect(area.x, area.y, area.width, HierarchyHeader);
            if (e.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(header, HierarchyHeaderColor);
            }

            GUI.Label(new Rect(header.x + 6f, header.y, header.width - 6f, header.height), "Hierarchy", HeaderStyle);

            if (hiddenIds.Count > 0)
            {
                var button = new Rect(header.xMax - 76f, header.y + 2f, 72f, header.height - 4f);
                if (GUI.Button(button, new GUIContent("Show All", "Show every hidden element"), EditorStyles.miniButton))
                {
                    hiddenIds.Clear();
                    UpdateHidden();
                    Repaint();
                }
            }
        }

        private void DrawRow(EasyUINode node, int index, float width, Event e, bool hasPointer)
        {
            var y = index * RowHeight;
            var row = new Rect(0f, y, width, RowHeight);
            var selected = IsSelected(node);
            var hidden = IsHidden(node);
            var indent = (document.DepthOf(node) - 1) * IndentStep;

            var eye = new Rect(3f, y + 2f, EyeColumn - 6f, RowHeight - 4f);
            var arrow = new Rect(EyeColumn + indent, y + 2f, ArrowColumn, RowHeight - 4f);
            var label = new Rect(arrow.xMax, y, Mathf.Max(width - arrow.xMax, 0f), RowHeight);
            var hasChildren = _parents.Contains(node.id);

            if (e.type == EventType.Repaint)
            {
                if (selected)
                {
                    EditorGUI.DrawRect(row, HierarchySelectedColor);
                }

                if (!node.IsPart)
                {
                    DrawEye(eye, hiddenIds.Contains(node.id), hidden);
                }

                if (hasChildren)
                {
                    EditorStyles.foldout.Draw(arrow, GUIContent.none, false, false, !foldedIds.Contains(node.id), false);
                }

                GUI.Label(label, NameOf(node), hidden ? RowHiddenLabelStyle : RowLabelStyle);
                return;
            }

            if (e.type != EventType.MouseDown || e.button != 0 || !hasPointer || !row.Contains(e.mousePosition))
            {
                return;
            }

            ConfirmRename();
            EndTextEditing();

            if (!node.IsPart && eye.Contains(e.mousePosition))
            {
                ToggleHidden(node);
            }
            else if (hasChildren && arrow.Contains(e.mousePosition))
            {
                ToggleFolded(node);
            }
            else
            {
                if (e.control || e.command)
                {
                    ToggleSelected(node.id);
                }
                else
                {
                    SelectOnly(node.id);
                }

                if (e.clickCount == 2)
                {
                    _frameRequested = true;
                }
            }

            // Seen from here on: the tree doesn't follow the click it just made until the next event.
            _revealedFor = _selectedId;
            e.Use();
            Repaint();
        }

        // An open eye, or a slashed one when the element itself is hidden; dimmed while it is only inside a hidden one.
        private static void DrawEye(Rect rect, bool hiddenItself, bool hidden)
        {
            var icon = EditorGUIUtility.IconContent(hiddenItself ? "scenevis_hidden_hover" : "scenevis_visible_hover");
            var previous = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, hidden && !hiddenItself ? 0.4f : 1f);
            if (icon != null && icon.image != null)
            {
                GUI.DrawTexture(rect, icon.image, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Label(rect, hiddenItself ? "-" : "o");
            }

            GUI.color = previous;
        }

        #endregion

        #region Rows

        // The rows to show, top to bottom: every element in tree order, except those under a folded one.
        private void BuildRows()
        {
            _rows.Clear();
            _parents.Clear();
            foreach (var node in document.nodes)
            {
                if (node.parentId != 0)
                {
                    _parents.Add(node.parentId);
                }
            }

            foreach (var node in FullOrder())
            {
                if (!IsUnderFolded(node))
                {
                    _rows.Add(node);
                }
            }
        }

        // How wide the longest row needs, so the panel scrolls sideways instead of cutting a deep name off.
        private float ContentWidth()
        {
            var widest = 0f;
            foreach (var node in _rows)
            {
                var indent = (document.DepthOf(node) - 1) * IndentStep;
                widest = Mathf.Max(widest, EyeColumn + indent + ArrowColumn + NameWidth(NameOf(node), RowLabelStyle) + 8f);
            }

            return widest;
        }

        private bool IsUnderFolded(EasyUINode node)
        {
            if (foldedIds.Count == 0)
            {
                return false;
            }

            var parent = document.Find(node.parentId);
            for (var guard = 0; parent != null && guard < 64; guard++)
            {
                if (foldedIds.Contains(parent.id))
                {
                    return true;
                }

                parent = document.Find(parent.parentId);
            }

            return false;
        }

        // Unfolds every element above `node`.
        private void Unfold(EasyUINode node)
        {
            var parent = node != null ? document.Find(node.parentId) : null;
            for (var guard = 0; parent != null && guard < 64; guard++)
            {
                foldedIds.Remove(parent.id);
                parent = document.Find(parent.parentId);
            }
        }

        private void ToggleFolded(EasyUINode node)
        {
            if (!foldedIds.Remove(node.id))
            {
                foldedIds.Add(node.id);
            }
        }

        private void ToggleHidden(EasyUINode node)
        {
            if (!hiddenIds.Remove(node.id))
            {
                hiddenIds.Add(node.id);
            }

            UpdateHidden();
            _drag = Drag.None;
            _blockers.Clear();
            _guides.Clear();
        }

        #endregion

        #region Splitter

        // The strip between the hierarchy and the workspace is dragged to widen or narrow the hierarchy.
        private void HandleSplitter(Event e, int id, Rect area)
        {
            var grab = new Rect(area.xMax - 2f, area.y, SplitterWidth + 4f, area.height);
            EditorGUIUtility.AddCursorRect(grab, MouseCursor.ResizeHorizontal);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && grab.Contains(e.mousePosition):
                    _resizingHierarchy = true;
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;

                case EventType.MouseDrag when _resizingHierarchy && GUIUtility.hotControl == id:
                    hierarchyWidth = Mathf.Max(HierarchyMinWidth, _hierarchyRect.width + e.delta.x);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when _resizingHierarchy && GUIUtility.hotControl == id:
                    _resizingHierarchy = false;
                    GUIUtility.hotControl = 0;
                    e.Use();
                    Repaint();
                    break;
            }
        }

        #endregion
    }
}
