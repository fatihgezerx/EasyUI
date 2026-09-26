using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // Selection and hierarchy geometry.
    //
    // Selecting: a click selects one element; Ctrl (Cmd on a Mac) + click adds or removes one; dragging from an
    // empty spot draws a box that selects every element it touches (with Ctrl held, adding to the selection).
    // Dragging a selected element moves the whole selection; the element clicked last is the one the panel on
    // the right shows, and the one resized by its edges.
    //
    // Parents and children: a child always stays inside its parent. Moving or resizing an element takes its
    // children along the way a RectTransform's anchors do - an anchored side keeps its distance from its anchor,
    // a stretched side keeps its distance from the parent's edge - so a child stretched across its parent
    // narrows with it. A drag never takes an element out of its parent (or, for the panel's own elements, the
    // canvas); one that ends up out anyway - its parent shrunk, or its Rect Transform fields say so - turns red.
    internal sealed partial class EasyUIWindow
    {
        private static readonly Color MarqueeFillColor = new(0.35f, 0.6f, 1f, 0.12f);
        private static readonly Color MarqueeBorderColor = new(0.35f, 0.6f, 1f, 0.8f);

        // Every selected element; _selectedId is the one clicked last, shown in the panel.
        private readonly HashSet<int> _selected = new();

        // A box being dragged to select: its corners (canvas units), and what was selected before it, which it
        // adds to with Ctrl held.
        private Vector2 _marqueeStart;
        private Vector2 _marqueeEnd;
        private readonly HashSet<int> _marqueeBase = new();

        // A move of the selection: the elements that move themselves (selected, with no selected parent), where
        // each started, the one whose position the move follows, the one pressed, and whether it has moved yet.
        private readonly Dictionary<int, Rect> _moveStarts = new();
        private int _leadId;
        private int _pressedId;
        private bool _moved;

        #region Selection

        private bool IsSelected(EasyUINode node) => _selected.Contains(node.id);

        private void SelectOnly(int id)
        {
            _selected.Clear();
            if (id != 0)
            {
                _selected.Add(id);
            }

            _selectedId = id;
        }

        private void ClearSelection() => SelectOnly(0);

        // Ctrl + click: in or out of the selection. The panel shows the element added, or another selected one.
        private void ToggleSelected(int id)
        {
            if (_selected.Remove(id))
            {
                if (_selectedId == id)
                {
                    _selectedId = 0;
                    foreach (var other in _selected)
                    {
                        _selectedId = other;
                    }
                }

                return;
            }

            _selected.Add(id);
            _selectedId = id;
        }

        // Drops what no longer exists from the selection (after a delete, an Open, a Clear).
        private void PruneSelection()
        {
            _selected.RemoveWhere(id => document.Find(id) == null);
            if (!_selected.Contains(_selectedId))
            {
                _selectedId = 0;
                foreach (var other in _selected)
                {
                    _selectedId = other;
                }
            }
        }

        // Whether the element is selected or somewhere under a selected one.
        private bool IsUnderSelection(EasyUINode node)
        {
            foreach (var id in _selected)
            {
                var selected = document.Find(id);
                if (selected != null && document.IsUnder(node, selected))
                {
                    return true;
                }
            }

            return false;
        }

        // The selected elements whose parents aren't selected: the ones a move or a delete acts on (their
        // children come along).
        private IEnumerable<EasyUINode> SelectionRoots()
        {
            foreach (var node in document.nodes)
            {
                if (!IsSelected(node))
                {
                    continue;
                }

                var parent = document.Find(node.parentId);
                if (parent == null || !IsUnderSelection(parent))
                {
                    yield return node;
                }
            }
        }

        // Everything selected, as one rect (canvas units); false with nothing selected.
        private bool SelectionBounds(out Rect bounds)
        {
            bounds = default;
            var any = false;
            foreach (var id in _selected)
            {
                var node = document.Find(id);
                if (node == null)
                {
                    continue;
                }

                bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, node.Rect.xMin), Mathf.Min(bounds.yMin, node.Rect.yMin),
                    Mathf.Max(bounds.xMax, node.Rect.xMax), Mathf.Max(bounds.yMax, node.Rect.yMax)) : node.Rect;
                any = true;
            }

            return any;
        }

        #endregion

        #region Marquee

        private void StartMarquee(Vector2 pointer, bool additive)
        {
            _marqueeStart = _marqueeEnd = ToCanvas(pointer);
            _marqueeBase.Clear();
            if (additive)
            {
                _marqueeBase.UnionWith(_selected);
            }

            _drag = Drag.Marquee;
        }

        // Selects what was selected before (with Ctrl) plus every element the box touches.
        private void UpdateMarquee(Vector2 pointer)
        {
            _marqueeEnd = ToCanvas(pointer);
            var box = MarqueeRect;

            _selected.Clear();
            _selected.UnionWith(_marqueeBase);
            foreach (var node in document.nodes)
            {
                if (Overlaps(box, node.Rect))
                {
                    _selected.Add(node.id);
                }
            }

            if (!_selected.Contains(_selectedId))
            {
                _selectedId = 0;
                foreach (var node in DrawOrder())
                {
                    if (IsSelected(node))
                    {
                        _selectedId = node.id;
                    }
                }
            }
        }

        private Rect MarqueeRect => Rect.MinMaxRect(Mathf.Min(_marqueeStart.x, _marqueeEnd.x), Mathf.Min(_marqueeStart.y, _marqueeEnd.y),
            Mathf.Max(_marqueeStart.x, _marqueeEnd.x), Mathf.Max(_marqueeStart.y, _marqueeEnd.y));

        private void DrawMarquee()
        {
            var box = ToScreen(MarqueeRect);
            EditorGUI.DrawRect(box, MarqueeFillColor);
            DrawBorder(box, MarqueeBorderColor, 1f);
        }

        #endregion

        #region Moving the selection

        // Starts moving the selection; `lead` is the element pressed, or the selected one it sits under.
        private void StartMove(EasyUINode pressed)
        {
            _moveStarts.Clear();
            _leadId = pressed.id;
            foreach (var root in SelectionRoots())
            {
                _moveStarts[root.id] = root.Rect;
                if (document.IsUnder(pressed, root))
                {
                    _leadId = root.id;
                }
            }

            _pressedId = pressed.id;
            _moved = false;
            _drag = Drag.Move;
        }

        // The lead goes to where the pointer takes it - snapped with Ctrl, pulled onto lined-up edges otherwise,
        // stopped by elements on its layer - and every other moving element by as much; but no further than keeps
        // each of them inside its parent (or the canvas).
        private void MoveSelection(Vector2 delta, bool snap)
        {
            var lead = document.Find(_leadId);
            if (lead == null || !_moveStarts.TryGetValue(_leadId, out var leadStart))
            {
                return;
            }

            var target = leadStart.position + delta;
            target = snap ? Snap(target) : PullToLines(lead, target, leadStart.size);
            var move = BlockMove(lead, lead.Rect, target) - leadStart.position;

            foreach (var pair in _moveStarts)
            {
                var node = document.Find(pair.Key);
                if (node == null)
                {
                    continue;
                }

                var start = pair.Value;
                var bounds = ParentRect(node);
                move.x = KeepWithin(move.x, bounds.xMin - start.xMin, bounds.xMax - start.xMax);
                move.y = KeepWithin(move.y, bounds.yMin - start.yMin, bounds.yMax - start.yMax);
            }

            foreach (var pair in _moveStarts)
            {
                var node = document.Find(pair.Key);
                if (node != null)
                {
                    SetRect(node, new Rect(pair.Value.position + move, pair.Value.size));
                }
            }

            _moved |= move != Vector2.zero;
            UpdateGuides(lead);
        }

        // An offset kept between `low` and `high`; an element larger than its bounds keeps to their start.
        private static float KeepWithin(float value, float low, float high) => low <= high ? Mathf.Clamp(value, low, high) : low;

        #endregion

        #region Duplicating

        private static readonly Regex NumberedName = new(@"^(.*) \((\d+)\)$");

        // Ctrl + D: a copy of every selected element, with everything under it, one grid step right of and below
        // it - or left of / above it where that would take it out of its parent (or the canvas); level with it on
        // an axis where neither fits. The copies are named like Unity's ("Button (1)") and become the selection.
        private void DuplicateSelection()
        {
            var roots = new List<EasyUINode>(SelectionRoots());
            if (roots.Count == 0)
            {
                return;
            }

            var primary = document.Find(_selectedId);
            var copies = new List<int>();
            var primaryCopy = 0;
            foreach (var root in roots)
            {
                var copy = CopySubtree(root, root.parentId, DuplicateOffset(root));
                copy.name = CopyName(root);
                copies.Add(copy.id);
                if (primary != null && document.IsUnder(primary, root))
                {
                    primaryCopy = copy.id;
                }
            }

            _selected.Clear();
            _selected.UnionWith(copies);
            _selectedId = primaryCopy != 0 ? primaryCopy : copies[copies.Count - 1];
            Repaint();
        }

        // One grid step on each axis: towards the right and the bottom where there is room inside the parent,
        // otherwise towards the left and the top, otherwise none.
        private Vector2 DuplicateOffset(EasyUINode node)
        {
            const float tolerance = 0.01f;
            var step = _minorStep;
            var bounds = ParentRect(node);
            var rect = node.Rect;

            var x = rect.xMax + step <= bounds.xMax + tolerance ? step
                : rect.xMin - step >= bounds.xMin - tolerance ? -step
                : 0f;
            var y = rect.yMax + step <= bounds.yMax + tolerance ? step
                : rect.yMin - step >= bounds.yMin - tolerance ? -step
                : 0f;
            return new Vector2(x, y);
        }

        // Copies `node` - every setting - under `parentId`, shifted by `offset`, and its children under the copy.
        private EasyUINode CopySubtree(EasyUINode node, int parentId, Vector2 offset)
        {
            var copy = new EasyUINode();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(node), copy);
            copy.id = document.nextId++;
            copy.parentId = parentId;
            copy.position += offset;

            var children = document.nodes.FindAll(other => other.parentId == node.id);
            document.nodes.Add(copy);
            Touch(copy);

            foreach (var child in children)
            {
                CopySubtree(child, copy.id, offset);
            }

            return copy;
        }

        // "Name (n)" with the first n no sibling has yet.
        private string CopyName(EasyUINode node)
        {
            var name = NameOf(node);
            var match = NumberedName.Match(name);
            var stem = match.Success ? match.Groups[1].Value : name;

            for (var number = 1; ; number++)
            {
                var candidate = $"{stem} ({number})";
                var taken = false;
                foreach (var other in document.nodes)
                {
                    if (other.parentId == node.parentId && other.name == candidate)
                    {
                        taken = true;
                        break;
                    }
                }

                if (!taken)
                {
                    return candidate;
                }
            }
        }

        #endregion

        #region Parents and children

        // Places the element exactly at `rect` and takes its children along, as their anchors say.
        private void ApplyRect(EasyUINode node, Rect rect)
        {
            var old = node.Rect;
            node.position = rect.position;
            node.size = rect.size;
            if (old == rect)
            {
                return;
            }

            foreach (var child in document.nodes)
            {
                if (child.parentId == node.id && child != node)
                {
                    FollowParent(child, old, rect);
                }
            }
        }

        // A child's new rect after its parent went from `from` to `to`: on an anchored axis it keeps its distance
        // from the anchor; on a stretched one, from both the parent's edges.
        private void FollowParent(EasyUINode child, Rect from, Rect to)
        {
            var rect = child.Rect;
            float xMin, xMax, yMin, yMax;

            if (child.anchorH == HorizontalAnchor.Stretch)
            {
                xMin = to.xMin + (rect.xMin - from.xMin);
                xMax = Mathf.Max(to.xMax - (from.xMax - rect.xMax), xMin + MinElementSize);
            }
            else
            {
                var fraction = AnchorPresets.Fraction(child.anchorH);
                var shift = to.x + fraction * to.width - (from.x + fraction * from.width);
                xMin = rect.xMin + shift;
                xMax = rect.xMax + shift;
            }

            // Anchors count up from the parent's bottom edge.
            if (child.anchorV == VerticalAnchor.Stretch)
            {
                yMin = to.yMin + (rect.yMin - from.yMin);
                yMax = Mathf.Max(to.yMax - (from.yMax - rect.yMax), yMin + MinElementSize);
            }
            else
            {
                var fraction = AnchorPresets.Fraction(child.anchorV);
                var shift = to.yMax - fraction * to.height - (from.yMax - fraction * from.height);
                yMin = rect.yMin + shift;
                yMax = rect.yMax + shift;
            }

            ApplyRect(child, Rect.MinMaxRect(xMin, yMin, xMax, yMax));
        }

        private static bool IsInside(Rect rect, Rect bounds)
        {
            const float tolerance = 0.01f;
            return rect.xMin >= bounds.xMin - tolerance && rect.yMin >= bounds.yMin - tolerance
                   && rect.xMax <= bounds.xMax + tolerance && rect.yMax <= bounds.yMax + tolerance;
        }

        #endregion
    }
}
