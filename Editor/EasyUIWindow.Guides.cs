using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // Smart guides, like a design tool's: while an element is dragged or resized, a thin blue line shows wherever
    // one of its edges or its middle lines up with an edge or the middle of another element or of the canvas,
    // spanning the two. Coming close to such a line (a few pixels) pulls the element onto it, so things are easy
    // to line up exactly. With Ctrl held, the canvas grid snap applies instead.
    internal sealed partial class EasyUIWindow
    {
        // How close (window pixels) an edge must come to a line to be pulled onto it.
        private const float AlignPull = 6f;

        // How close (canvas units) two lines must be to count as lined up.
        private const float AlignTolerance = 0.5f;

        private static readonly Color GuideColor = new(0.3f, 0.65f, 1f, 1f);

        private readonly List<Guide> _guides = new();
        private readonly List<float> _alignLines = new();

        private readonly struct Guide
        {
            public readonly bool Vertical;
            public readonly float At;
            public readonly float From;
            public readonly float To;

            public Guide(bool vertical, float at, float from, float to)
            {
                Vertical = vertical;
                At = at;
                From = from;
                To = to;
            }
        }

        // Whether `other` stays put while `node` moves: not the element itself nor anything under it, nor (in a
        // move of the selection) anything moving along.
        private bool IsStill(EasyUINode node, EasyUINode other) =>
            !document.IsUnder(other, node) && !(_drag == Drag.Move && IsUnderSelection(other));

        // The lines the element can line up with along one axis: the edges and middles of the canvas and of
        // every element that stays put.
        private List<float> AlignLines(EasyUINode node, bool vertical)
        {
            _alignLines.Clear();
            var canvasLength = vertical ? _canvasSize.x : _canvasSize.y;
            _alignLines.Add(0f);
            _alignLines.Add(canvasLength * 0.5f);
            _alignLines.Add(canvasLength);

            foreach (var other in document.nodes)
            {
                if (!IsStill(node, other))
                {
                    continue;
                }

                var rect = other.Rect;
                _alignLines.Add(vertical ? rect.xMin : rect.yMin);
                _alignLines.Add(vertical ? rect.center.x : rect.center.y);
                _alignLines.Add(vertical ? rect.xMax : rect.yMax);
            }

            return _alignLines;
        }

        // Pulls a dragged element (top-left `position`, `size`) onto the nearest line its edges or middle come
        // close to, on each axis.
        private Vector2 PullToLines(EasyUINode node, Vector2 position, Vector2 size)
        {
            position.x = PullSpan(position.x, size.x, AlignLines(node, true));
            position.y = PullSpan(position.y, size.y, AlignLines(node, false));
            return position;
        }

        // A span from `start` of `length`: whichever of its start, middle and end is nearest a line (within the
        // pull) moves onto it, taking the span along.
        private float PullSpan(float start, float length, List<float> lines)
        {
            var best = AlignPull / zoom;
            var result = start;
            foreach (var line in lines)
            {
                for (var part = 0; part < 3; part++)
                {
                    var offset = length * 0.5f * part;
                    var distance = Mathf.Abs(start + offset - line);
                    if (distance < best)
                    {
                        best = distance;
                        result = line - offset;
                    }
                }
            }

            return result;
        }

        // A single dragged edge, pulled onto the nearest line within reach.
        private float PullEdge(float edge, List<float> lines)
        {
            var best = AlignPull / zoom;
            var result = edge;
            foreach (var line in lines)
            {
                var distance = Mathf.Abs(edge - line);
                if (distance < best)
                {
                    best = distance;
                    result = line;
                }
            }

            return result;
        }

        // Pulls the dragged edges of a resize onto lines, but never below the smallest size.
        private Rect PullEdgesToLines(EasyUINode node, Rect rect, Edges edges)
        {
            var min = rect.min;
            var max = rect.max;

            var xLines = AlignLines(node, true);
            if ((edges & Edges.Left) != 0)
            {
                min.x = Mathf.Min(PullEdge(min.x, xLines), max.x - MinElementSize);
            }

            if ((edges & Edges.Right) != 0)
            {
                max.x = Mathf.Max(PullEdge(max.x, xLines), min.x + MinElementSize);
            }

            var yLines = AlignLines(node, false);
            if ((edges & Edges.Top) != 0)
            {
                min.y = Mathf.Min(PullEdge(min.y, yLines), max.y - MinElementSize);
            }

            if ((edges & Edges.Bottom) != 0)
            {
                max.y = Mathf.Max(PullEdge(max.y, yLines), min.y + MinElementSize);
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        // Finds the guides for where the element now is: every edge or middle of it that lines up with one of
        // another element or of the canvas, as a line spanning both.
        private void UpdateGuides(EasyUINode node)
        {
            _guides.Clear();
            var rect = node.Rect;
            AddGuides(rect, CanvasRect);
            foreach (var other in document.nodes)
            {
                if (IsStill(node, other))
                {
                    AddGuides(rect, other.Rect);
                }
            }
        }

        private void AddGuides(Rect moved, Rect other)
        {
            for (var i = 0; i < 3; i++)
            {
                var movedX = moved.xMin + moved.width * 0.5f * i;
                var movedY = moved.yMin + moved.height * 0.5f * i;
                for (var j = 0; j < 3; j++)
                {
                    var otherX = other.xMin + other.width * 0.5f * j;
                    if (Mathf.Abs(movedX - otherX) < AlignTolerance)
                    {
                        _guides.Add(new Guide(true, otherX, Mathf.Min(moved.yMin, other.yMin), Mathf.Max(moved.yMax, other.yMax)));
                    }

                    var otherY = other.yMin + other.height * 0.5f * j;
                    if (Mathf.Abs(movedY - otherY) < AlignTolerance)
                    {
                        _guides.Add(new Guide(false, otherY, Mathf.Min(moved.xMin, other.xMin), Mathf.Max(moved.xMax, other.xMax)));
                    }
                }
            }
        }

        private void DrawGuides()
        {
            foreach (var guide in _guides)
            {
                if (guide.Vertical)
                {
                    var top = ToScreen(new Vector2(guide.At, guide.From));
                    var bottom = ToScreen(new Vector2(guide.At, guide.To));
                    EditorGUI.DrawRect(new Rect(Mathf.Round(top.x), top.y, 1f, bottom.y - top.y), GuideColor);
                }
                else
                {
                    var left = ToScreen(new Vector2(guide.From, guide.At));
                    var right = ToScreen(new Vector2(guide.To, guide.At));
                    EditorGUI.DrawRect(new Rect(left.x, Mathf.Round(left.y), right.x - left.x, 1f), GuideColor);
                }
            }
        }
    }
}
