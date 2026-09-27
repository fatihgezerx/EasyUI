using System.Collections.Generic;
using UnityEngine;

namespace EasyUI
{
    // Layers: an element's layer is how deep it sits - 1 for the panel's own children, 2 for theirs, and so on,
    // with no limit - shown as the number at the start of its label. Elements on different layers may overlap
    // freely (a child sits inside its parent). On the same layer they may not: a drag or a resize stops where it
    // would run into another one, and both show yellow edges while it presses against it. An element that
    // overlaps one on its layer anyway (e.g. placed there from the Rect Transform fields) turns red: the one
    // changed last. Parts, and elements whose Layout Element ignores layout, are out of this rule: they may lie
    // over others on their layer (e.g. a background cell under a grid's slots).
    internal sealed partial class EasyUIWindow
    {
        // How far two rects must reach into each other to count as overlapping: touching edges don't.
        private const float OverlapTolerance = 0.01f;

        // While a drag or a resize is held back by elements on its layer: those elements' ids.
        private readonly HashSet<int> _blockers = new();

        // Out of the overlap rule: a part (Unity lays it out), an element whose Layout Element ignores layout - it is
        // placed by hand over the others on purpose, e.g. a background cell under the slots of a grid - or one hidden
        // in the workspace (its eye in the hierarchy), which is out of sight and so never in the way.
        private bool IsFreeToOverlap(EasyUINode node) =>
            node.IsPart || (node.layoutElement.enabled && node.layoutElement.ignoreLayout) || IsHidden(node);

        // Marks the element as changed just now.
        private void Touch(EasyUINode node) => node.editStamp = ++document.editCounter;

        private static bool Overlaps(Rect a, Rect b) =>
            Spans(a.xMin, a.xMax, b.xMin, b.xMax) && Spans(a.yMin, a.yMax, b.yMin, b.yMax);

        private static bool Spans(float aMin, float aMax, float bMin, float bMax) =>
            aMin < bMax - OverlapTolerance && bMin < aMax - OverlapTolerance;

        /// <summary>
        /// Whether the element overlaps another on its layer that was changed before it (or at the same time) -
        /// then it is the one out of place, drawn red. <paramref name="with"/> is that other element.
        /// </summary>
        private bool IsInConflict(EasyUINode node, out EasyUINode with)
        {
            with = null;
            if (IsFreeToOverlap(node))
            {
                return false;
            }

            var depth = document.DepthOf(node);
            foreach (var other in document.nodes)
            {
                if (other == node || IsFreeToOverlap(other) || other.editStamp > node.editStamp || document.DepthOf(other) != depth)
                {
                    continue;
                }

                if (Overlaps(node.Rect, other.Rect))
                {
                    with = other;
                    return true;
                }
            }

            with = null;
            return false;
        }

        // Another element on the same layer that `from` doesn't already overlap - so one caught overlapping can
        // still be dragged out - and that isn't moving along with it. Elements free to overlap (parts, those
        // ignoring layout) never block, nor are blocked.
        private bool IsObstacle(EasyUINode node, int depth, Rect from, EasyUINode other)
        {
            return other != node && !IsFreeToOverlap(node) && !IsFreeToOverlap(other) && document.DepthOf(other) == depth
                   && !Overlaps(from, other.Rect)
                   && !(_drag == Drag.Move && IsSelected(other));
        }

        /// <summary>
        /// Moves <paramref name="from"/> towards <paramref name="to"/> (its new top-left corner), stopping at the
        /// first element on its layer in the way: across first, then up or down, so it slides along what it
        /// meets. Remembers what stopped it, for the yellow edges.
        /// </summary>
        private Vector2 BlockMove(EasyUINode node, Rect from, Vector2 to)
        {
            _blockers.Clear();
            var depth = document.DepthOf(node);
            var size = from.size;

            var x = to.x;
            foreach (var other in document.nodes)
            {
                if (!IsObstacle(node, depth, from, other))
                {
                    continue;
                }

                var obstacle = other.Rect;
                if (!Spans(from.yMin, from.yMax, obstacle.yMin, obstacle.yMax))
                {
                    continue;
                }

                if (x > from.x && from.xMax <= obstacle.xMin + OverlapTolerance && x + size.x > obstacle.xMin)
                {
                    x = obstacle.xMin - size.x;
                    _blockers.Add(other.id);
                }
                else if (x < from.x && from.xMin >= obstacle.xMax - OverlapTolerance && x < obstacle.xMax)
                {
                    x = obstacle.xMax;
                    _blockers.Add(other.id);
                }
            }

            var y = to.y;
            foreach (var other in document.nodes)
            {
                if (!IsObstacle(node, depth, from, other))
                {
                    continue;
                }

                var obstacle = other.Rect;
                if (!Spans(x, x + size.x, obstacle.xMin, obstacle.xMax))
                {
                    continue;
                }

                if (y > from.y && from.yMax <= obstacle.yMin + OverlapTolerance && y + size.y > obstacle.yMin)
                {
                    y = obstacle.yMin - size.y;
                    _blockers.Add(other.id);
                }
                else if (y < from.y && from.yMin >= obstacle.yMax - OverlapTolerance && y < obstacle.yMax)
                {
                    y = obstacle.yMax;
                    _blockers.Add(other.id);
                }
            }

            return new Vector2(x, y);
        }

        /// <summary>
        /// Resizes <paramref name="from"/> towards <paramref name="to"/> by its dragged <paramref name="edges"/>,
        /// each edge stopping at the first element on its layer in the way. Remembers what stopped it.
        /// </summary>
        private Rect BlockResize(EasyUINode node, Rect from, Rect to, Edges edges)
        {
            _blockers.Clear();
            var depth = document.DepthOf(node);
            var min = to.min;
            var max = to.max;

            foreach (var other in document.nodes)
            {
                if (!IsObstacle(node, depth, from, other))
                {
                    continue;
                }

                var obstacle = other.Rect;
                if (!Spans(from.yMin, from.yMax, obstacle.yMin, obstacle.yMax))
                {
                    continue;
                }

                if ((edges & Edges.Right) != 0 && from.xMax <= obstacle.xMin + OverlapTolerance && max.x > obstacle.xMin)
                {
                    max.x = obstacle.xMin;
                    _blockers.Add(other.id);
                }

                if ((edges & Edges.Left) != 0 && from.xMin >= obstacle.xMax - OverlapTolerance && min.x < obstacle.xMax)
                {
                    min.x = obstacle.xMax;
                    _blockers.Add(other.id);
                }
            }

            foreach (var other in document.nodes)
            {
                if (!IsObstacle(node, depth, from, other))
                {
                    continue;
                }

                var obstacle = other.Rect;
                if (!Spans(min.x, max.x, obstacle.xMin, obstacle.xMax))
                {
                    continue;
                }

                if ((edges & Edges.Bottom) != 0 && from.yMax <= obstacle.yMin + OverlapTolerance && max.y > obstacle.yMin)
                {
                    max.y = obstacle.yMin;
                    _blockers.Add(other.id);
                }

                if ((edges & Edges.Top) != 0 && from.yMin >= obstacle.yMax - OverlapTolerance && min.y < obstacle.yMax)
                {
                    min.y = obstacle.yMax;
                    _blockers.Add(other.id);
                }
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // Yellow edges: the dragged element and whatever it presses against, while it does.
        private bool IsHeldBack(EasyUINode node) =>
            _blockers.Count > 0 && (_drag == Drag.Move || _drag == Drag.Resize) && (node.id == _leadId || _blockers.Contains(node.id));
    }
}
