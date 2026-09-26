using System.Collections.Generic;
using UnityEngine;

namespace EasyUI
{
    // Layout components at work in the workspace, worked out the way uGUI works them out at runtime, so what is
    // drawn is what the game shows: a Content Size Fitter sizes its element to its min or preferred size, and a
    // Horizontal / Vertical / Grid Layout Group places its children (and, with Control Child Size, sizes them).
    // Run before every event: an element a layout group places snaps back when dragged, as its Rect Transform is
    // driven in Unity too.
    //
    // Sizes follow uGUI's LayoutUtility: a Layout Element's values win (priority 1), else the element's own layout
    // group's totals, an Image's sprite size or a Raw Image's texture size. A text keeps its current size - uGUI
    // measures its text, which the workspace can't.
    internal sealed partial class EasyUIWindow
    {
        private const int MinKind = 0;
        private const int PreferredKind = 1;
        private const int FlexibleKind = 2;

        // uGUI's canvas default: sprite pixels per canvas unit.
        private const float ReferencePixelsPerUnit = 100f;

        private readonly Dictionary<int, List<EasyUINode>> _layoutChildren = new();
        private readonly List<EasyUINode> _laidOut = new();
        private static readonly List<EasyUINode> NoChildren = new();

        private void UpdateLayout()
        {
            _layoutChildren.Clear();
            foreach (var node in document.nodes)
            {
                if (!_layoutChildren.TryGetValue(node.parentId, out var list))
                {
                    list = new List<EasyUINode>();
                    _layoutChildren.Add(node.parentId, list);
                }

                list.Add(node);
            }

            // Sizes bottom-up (a fitter needs its children's sizes), then places top-down (a group needs its own size).
            foreach (var node in document.nodes)
            {
                if (document.Find(node.parentId) == null)
                {
                    Fit(node, 0);
                    Arrange(node, 0);
                }
            }
        }

        private List<EasyUINode> ChildrenOf(EasyUINode node) =>
            _layoutChildren.TryGetValue(node.id, out var list) ? list : NoChildren;

        // The children a layout group lays out: all but those whose Layout Element ignores layout.
        private List<EasyUINode> LaidOutChildren(EasyUINode node)
        {
            _laidOut.Clear();
            foreach (var child in ChildrenOf(node))
            {
                if (!(child.layoutElement.enabled && child.layoutElement.ignoreLayout))
                {
                    _laidOut.Add(child);
                }
            }

            return _laidOut;
        }

        #region Content Size Fitter

        private void Fit(EasyUINode node, int depth)
        {
            if (depth > 64)
            {
                return;
            }

            foreach (var child in ChildrenOf(node))
            {
                Fit(child, depth + 1);
            }

            var fitter = node.contentSizeFitter;
            if (!fitter.enabled)
            {
                return;
            }

            var width = FittedSize(node, 0, fitter.horizontalFit);
            var height = FittedSize(node, 1, fitter.verticalFit);
            if (Mathf.Approximately(width, node.size.x) && Mathf.Approximately(height, node.size.y))
            {
                return;
            }

            // Resized around its pivot, as a Rect Transform resizes (pivot y counts up).
            var rect = node.Rect;
            var x = rect.x + (rect.width - width) * node.pivot.x;
            var y = rect.y + (rect.height - height) * (1f - node.pivot.y);
            ApplyRect(node, new Rect(x, y, width, height));
        }

        private float FittedSize(EasyUINode node, int axis, FitMode mode) => mode switch
        {
            FitMode.MinSize => Mathf.Max(LayoutValue(node, axis, MinKind), 0f),
            FitMode.PreferredSize => Mathf.Max(LayoutValue(node, axis, PreferredKind), 0f),
            _ => node.size[axis]
        };

        #endregion

        #region Sizes

        // uGUI's LayoutUtility.GetMin / Preferred / FlexibleSize for the element.
        private float LayoutValue(EasyUINode node, int axis, int kind)
        {
            var value = OwnLayoutValue(node, axis, kind);

            var element = node.layoutElement;
            if (element.enabled)
            {
                var set = kind switch
                {
                    MinKind => axis == 0 ? (element.useMinWidth ? element.minWidth : -1f) : (element.useMinHeight ? element.minHeight : -1f),
                    PreferredKind => axis == 0 ? (element.usePreferredWidth ? element.preferredWidth : -1f) : (element.usePreferredHeight ? element.preferredHeight : -1f),
                    _ => axis == 0 ? (element.useFlexibleWidth ? element.flexibleWidth : -1f) : (element.useFlexibleHeight ? element.flexibleHeight : -1f)
                };

                // A Layout Element above priority 0 overrides the rest; at 0 (or below) the larger value wins.
                if (set >= 0f)
                {
                    value = element.layoutPriority > 0 ? set : Mathf.Max(value, set);
                }
            }

            return kind == PreferredKind ? Mathf.Max(value, LayoutValue(node, axis, MinKind)) : value;
        }

        // What the element's own components say, at priority 0.
        private float OwnLayoutValue(EasyUINode node, int axis, int kind)
        {
            if (node.layoutGroup.enabled)
            {
                return GroupTotal(node, axis, kind);
            }

            if (kind != PreferredKind)
            {
                return 0f;
            }

            switch (node.type)
            {
                case EasyUIElementType.Text:
                    return node.size[axis];

                case EasyUIElementType.Image:
                case EasyUIElementType.Button:
                case EasyUIElementType.Dropdown:
                case EasyUIElementType.InputField:
                    return SpriteSize(node.image, axis);

                case EasyUIElementType.RawImage:
                    var texture = node.rawImage.texture;
                    return texture != null ? (axis == 0 ? texture.width * node.rawImage.uvRect.width : texture.height * node.rawImage.uvRect.height) : 0f;

                default:
                    return 0f;
            }
        }

        // An Image's preferred size: its sprite's size in canvas units, or its borders for a sliced or tiled one.
        private static float SpriteSize(ImageSettings image, int axis)
        {
            var sprite = image.sprite;
            if (sprite == null)
            {
                return 0f;
            }

            var pixelsPerUnit = sprite.pixelsPerUnit / ReferencePixelsPerUnit * Mathf.Max(image.pixelsPerUnitMultiplier, 0.01f);
            var sliced = image.imageType is UnityEngine.UI.Image.Type.Sliced or UnityEngine.UI.Image.Type.Tiled;
            var pixels = sliced
                ? (axis == 0 ? sprite.border.x + sprite.border.z : sprite.border.y + sprite.border.w)
                : sprite.rect.size[axis];
            return pixels / pixelsPerUnit;
        }

        // uGUI's CalcAlongAxis: a group's min, preferred or flexible size.
        private float GroupTotal(EasyUINode node, int axis, int kind)
        {
            var group = node.layoutGroup;
            var children = LaidOutChildren(node);
            if (group.kind == LayoutKind.Grid)
            {
                return kind == FlexibleKind ? 0f : GridTotal(node, axis, kind, children.Count);
            }

            var vertical = group.kind == LayoutKind.Vertical;
            var padding = axis == 0 ? group.paddingLeft + group.paddingRight : group.paddingTop + group.paddingBottom;
            var alongOtherAxis = vertical ^ (axis == 1);
            float totalMin = padding, totalPreferred = padding, totalFlexible = 0f;

            // A copy: sizing a child may lay out its own children through the same list.
            var list = children.ToArray();
            foreach (var child in list)
            {
                ChildSizes(node, child, axis, out var min, out var preferred, out var flexible);
                if (alongOtherAxis)
                {
                    totalMin = Mathf.Max(min + padding, totalMin);
                    totalPreferred = Mathf.Max(preferred + padding, totalPreferred);
                    totalFlexible = Mathf.Max(flexible, totalFlexible);
                }
                else
                {
                    totalMin += min + group.spacing;
                    totalPreferred += preferred + group.spacing;
                    totalFlexible += flexible;
                }
            }

            if (!alongOtherAxis && list.Length > 0)
            {
                totalMin -= group.spacing;
                totalPreferred -= group.spacing;
            }

            totalPreferred = Mathf.Max(totalMin, totalPreferred);
            return kind switch
            {
                MinKind => totalMin,
                PreferredKind => totalPreferred,
                _ => totalFlexible
            };
        }

        // uGUI's GetChildSizes: a child not sized by the group counts at its own size.
        private void ChildSizes(EasyUINode node, EasyUINode child, int axis, out float min, out float preferred, out float flexible)
        {
            var group = node.layoutGroup;
            var control = axis == 0 ? group.controlChildWidth : group.controlChildHeight;
            var expand = axis == 0 ? group.childForceExpandWidth : group.childForceExpandHeight;
            if (!control)
            {
                min = preferred = child.size[axis];
                flexible = 0f;
            }
            else
            {
                min = LayoutValue(child, axis, MinKind);
                preferred = LayoutValue(child, axis, PreferredKind);
                flexible = LayoutValue(child, axis, FlexibleKind);
            }

            if (expand)
            {
                flexible = Mathf.Max(flexible, 1f);
            }
        }

        // uGUI's GridLayoutGroup.CalculateLayoutInput: min and preferred size for its cells.
        private static float GridTotal(EasyUINode node, int axis, int kind, int count)
        {
            var group = node.layoutGroup;
            var cell = group.cellSize;
            var spacing = group.gridSpacing;
            if (axis == 0)
            {
                int minColumns, preferredColumns;
                if (group.constraint == GridConstraint.FixedColumnCount)
                {
                    minColumns = preferredColumns = group.constraintCount;
                }
                else if (group.constraint == GridConstraint.FixedRowCount)
                {
                    minColumns = preferredColumns = Mathf.CeilToInt(count / (float)group.constraintCount - 0.001f);
                }
                else
                {
                    minColumns = 1;
                    preferredColumns = Mathf.CeilToInt(Mathf.Sqrt(count));
                }

                var columns = kind == MinKind ? minColumns : preferredColumns;
                return group.paddingLeft + group.paddingRight + (cell.x + spacing.x) * columns - spacing.x;
            }

            int rows;
            if (group.constraint == GridConstraint.FixedColumnCount)
            {
                rows = Mathf.CeilToInt(count / (float)group.constraintCount - 0.001f);
            }
            else if (group.constraint == GridConstraint.FixedRowCount)
            {
                rows = group.constraintCount;
            }
            else
            {
                var perRow = Mathf.Max(1, Mathf.FloorToInt((node.size.x - group.paddingLeft - group.paddingRight + spacing.x + 0.001f) / (cell.x + spacing.x)));
                rows = Mathf.CeilToInt(count / (float)perRow);
            }

            return group.paddingTop + group.paddingBottom + (cell.y + spacing.y) * rows - spacing.y;
        }

        #endregion

        #region Layout groups

        private void Arrange(EasyUINode node, int depth)
        {
            if (depth > 64)
            {
                return;
            }

            if (node.layoutGroup.enabled)
            {
                var children = LaidOutChildren(node).ToArray();
                var rects = new Rect[children.Length];
                for (var i = 0; i < children.Length; i++)
                {
                    rects[i] = children[i].Rect;
                }

                if (node.layoutGroup.kind == LayoutKind.Grid)
                {
                    ArrangeGrid(node, children, rects);
                }
                else
                {
                    var vertical = node.layoutGroup.kind == LayoutKind.Vertical;
                    ArrangeAlongAxis(node, children, rects, 0, vertical);
                    ArrangeAlongAxis(node, children, rects, 1, vertical);
                }

                for (var i = 0; i < children.Length; i++)
                {
                    if (!Approximately(rects[i], children[i].Rect))
                    {
                        ApplyRect(children[i], rects[i]);
                    }
                }
            }

            foreach (var child in ChildrenOf(node).ToArray())
            {
                Arrange(child, depth + 1);
            }
        }

        // uGUI's HorizontalOrVerticalLayoutGroup.SetChildrenAlongAxis, into `rects` (canvas units, y down).
        private void ArrangeAlongAxis(EasyUINode node, EasyUINode[] children, Rect[] rects, int axis, bool vertical)
        {
            var group = node.layoutGroup;
            var size = node.size[axis];
            var control = axis == 0 ? group.controlChildWidth : group.controlChildHeight;
            var alignment = AlignmentOnAxis(group.childAlignment, axis);
            var alongOtherAxis = vertical ^ (axis == 1);
            var padding = axis == 0 ? group.paddingLeft + group.paddingRight : group.paddingTop + group.paddingBottom;
            var origin = axis == 0 ? node.position.x : node.position.y;

            if (alongOtherAxis)
            {
                var innerSize = size - padding;
                for (var i = 0; i < children.Length; i++)
                {
                    ChildSizes(node, children[i], axis, out var min, out var preferred, out var flexible);
                    var required = Mathf.Clamp(innerSize, min, flexible > 0f ? size : preferred);
                    var start = StartOffset(node, axis, required);
                    if (control)
                    {
                        Set(ref rects[i], axis, origin + start, required);
                    }
                    else
                    {
                        var own = children[i].size[axis];
                        Set(ref rects[i], axis, origin + start + (required - own) * alignment, own);
                    }
                }

                return;
            }

            var totalMin = GroupTotal(node, axis, MinKind);
            var totalPreferred = GroupTotal(node, axis, PreferredKind);
            var totalFlexible = GroupTotal(node, axis, FlexibleKind);

            var position = axis == 0 ? group.paddingLeft : (float)group.paddingTop;
            var flexibleMultiplier = 0f;
            var surplus = size - totalPreferred;
            if (surplus > 0f)
            {
                if (totalFlexible == 0f)
                {
                    position = StartOffset(node, axis, totalPreferred - padding);
                }
                else
                {
                    flexibleMultiplier = surplus / totalFlexible;
                }
            }

            var minMaxLerp = !Mathf.Approximately(totalMin, totalPreferred)
                ? Mathf.Clamp01((size - totalMin) / (totalPreferred - totalMin))
                : 0f;

            for (var n = 0; n < children.Length; n++)
            {
                var i = group.reverseArrangement ? children.Length - 1 - n : n;
                ChildSizes(node, children[i], axis, out var min, out var preferred, out var flexible);
                var childSize = Mathf.Lerp(min, preferred, minMaxLerp) + flexible * flexibleMultiplier;
                if (control)
                {
                    Set(ref rects[i], axis, origin + position, childSize);
                }
                else
                {
                    var own = children[i].size[axis];
                    Set(ref rects[i], axis, origin + position + (childSize - own) * alignment, own);
                }

                position += childSize + group.spacing;
            }
        }

        // uGUI's GridLayoutGroup.SetCellsAlongAxis for both axes: every child sized to a cell and put in its place.
        private static void ArrangeGrid(EasyUINode node, EasyUINode[] children, Rect[] rects)
        {
            var group = node.layoutGroup;
            var count = children.Length;
            if (count == 0)
            {
                return;
            }

            var cell = group.cellSize;
            var spacing = group.gridSpacing;
            var width = node.size.x;
            var height = node.size.y;

            int cellsX, cellsY;
            if (group.constraint == GridConstraint.FixedColumnCount)
            {
                cellsX = group.constraintCount;
                cellsY = count > cellsX ? count / cellsX + (count % cellsX > 0 ? 1 : 0) : 1;
            }
            else if (group.constraint == GridConstraint.FixedRowCount)
            {
                cellsY = group.constraintCount;
                cellsX = count > cellsY ? count / cellsY + (count % cellsY > 0 ? 1 : 0) : 1;
            }
            else
            {
                cellsX = cell.x + spacing.x <= 0f ? int.MaxValue
                    : Mathf.Max(1, Mathf.FloorToInt((width - group.paddingLeft - group.paddingRight + spacing.x + 0.001f) / (cell.x + spacing.x)));
                cellsY = cell.y + spacing.y <= 0f ? int.MaxValue
                    : Mathf.Max(1, Mathf.FloorToInt((height - group.paddingTop - group.paddingBottom + spacing.y + 0.001f) / (cell.y + spacing.y)));
            }

            var cornerX = (int)group.startCorner % 2;
            var cornerY = (int)group.startCorner / 2;

            int perMainAxis, actualX, actualY;
            if (group.startAxis == GridStartAxis.Horizontal)
            {
                perMainAxis = cellsX;
                actualX = Mathf.Clamp(cellsX, 1, count);
                actualY = Mathf.Clamp(cellsY, 1, Mathf.CeilToInt(count / (float)perMainAxis));
            }
            else
            {
                perMainAxis = cellsY;
                actualY = Mathf.Clamp(cellsY, 1, count);
                actualX = Mathf.Clamp(cellsX, 1, Mathf.CeilToInt(count / (float)perMainAxis));
            }

            var requiredX = actualX * cell.x + (actualX - 1) * spacing.x;
            var requiredY = actualY * cell.y + (actualY - 1) * spacing.y;
            var startX = StartOffset(node, 0, requiredX);
            var startY = StartOffset(node, 1, requiredY);

            for (var i = 0; i < count; i++)
            {
                int x, y;
                if (group.startAxis == GridStartAxis.Horizontal)
                {
                    x = i % perMainAxis;
                    y = i / perMainAxis;
                }
                else
                {
                    x = i / perMainAxis;
                    y = i % perMainAxis;
                }

                if (cornerX == 1)
                {
                    x = actualX - 1 - x;
                }

                if (cornerY == 1)
                {
                    y = actualY - 1 - y;
                }

                rects[i] = new Rect(node.position.x + startX + (cell.x + spacing.x) * x,
                    node.position.y + startY + (cell.y + spacing.y) * y, cell.x, cell.y);
            }
        }

        // uGUI's GetStartOffset: where content of `required` size starts inside the element, by the group's alignment.
        private static float StartOffset(EasyUINode node, int axis, float required)
        {
            var group = node.layoutGroup;
            var padding = axis == 0 ? group.paddingLeft + group.paddingRight : group.paddingTop + group.paddingBottom;
            var surplus = node.size[axis] - (required + padding);
            return (axis == 0 ? group.paddingLeft : group.paddingTop) + surplus * AlignmentOnAxis(group.childAlignment, axis);
        }

        // 0 at the start (left / top), 0.5 in the middle, 1 at the end - as uGUI reads a TextAnchor.
        private static float AlignmentOnAxis(TextAnchor alignment, int axis) =>
            axis == 0 ? (int)alignment % 3 * 0.5f : (int)alignment / 3 * 0.5f;

        private static void Set(ref Rect rect, int axis, float start, float size)
        {
            if (axis == 0)
            {
                rect.x = start;
                rect.width = size;
            }
            else
            {
                rect.y = start;
                rect.height = size;
            }
        }

        private static bool Approximately(Rect a, Rect b) =>
            Mathf.Abs(a.x - b.x) < 0.001f && Mathf.Abs(a.y - b.y) < 0.001f
            && Mathf.Abs(a.width - b.width) < 0.001f && Mathf.Abs(a.height - b.height) < 0.001f;

        #endregion
    }
}
