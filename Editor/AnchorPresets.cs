using System;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    /// <summary>The anchor preset icons and fractions shared by the Easy UI window and its preset popup.</summary>
    internal static class AnchorPresets
    {
        private static readonly Color FrameColor = new(0.45f, 0.45f, 0.45f, 1f);
        private static readonly Color BoxColor = new(0.85f, 0.85f, 0.85f, 1f);
        private static readonly Color AnchorLineColor = new(0.85f, 0.25f, 0.25f, 1f);
        private static readonly Color AnchorPointColor = new(0.95f, 0.65f, 0.15f, 1f);
        private static readonly Color StretchColor = new(0.3f, 0.65f, 1f, 1f);

        /// <summary>The anchor's place across the parent: 0 left, 0.5 center, 1 right.</summary>
        public static float Fraction(HorizontalAnchor anchor) => anchor switch
        {
            HorizontalAnchor.Left => 0f,
            HorizontalAnchor.Right => 1f,
            _ => 0.5f
        };

        /// <summary>The anchor's place up the parent, as Unity counts it: 0 bottom, 0.5 middle, 1 top.</summary>
        public static float Fraction(VerticalAnchor anchor) => anchor switch
        {
            VerticalAnchor.Bottom => 0f,
            VerticalAnchor.Top => 1f,
            _ => 0.5f
        };

        public static string Name(HorizontalAnchor anchor) => anchor.ToString().ToLowerInvariant();

        public static string Name(VerticalAnchor anchor) => anchor.ToString().ToLowerInvariant();

        /// <summary>
        /// Draws a preset icon like Unity's: a frame (the parent) with a box (the object) in it, red lines where
        /// it is anchored, orange anchor points, and blue arrows along a stretched axis. A null axis isn't
        /// drawn - the popup's row and column headers show one axis only.
        /// </summary>
        public static void DrawIcon(Rect rect, HorizontalAnchor? horizontal, VerticalAnchor? vertical, bool dimmed)
        {
            if (Event.current.type != EventType.Repaint)
            {
                return;
            }

            var alpha = dimmed ? 0.3f : 1f;
            var frame = new Rect(Mathf.Round(rect.x + rect.width * 0.2f), Mathf.Round(rect.y + rect.height * 0.2f),
                Mathf.Round(rect.width * 0.6f), Mathf.Round(rect.height * 0.6f));
            DrawOutline(frame, Fade(FrameColor, alpha));

            var box = new Rect(frame.center.x - frame.width * 0.3f, frame.center.y - frame.height * 0.3f, frame.width * 0.6f, frame.height * 0.6f);
            DrawOutline(box, Fade(BoxColor, alpha));

            var lineColor = Fade(AnchorLineColor, alpha);
            if (horizontal.HasValue)
            {
                foreach (var x in AnchorXs(horizontal.Value, frame))
                {
                    EditorGUI.DrawRect(new Rect(x, frame.y, 1f, frame.height), lineColor);
                }
            }

            if (vertical.HasValue)
            {
                foreach (var y in AnchorYs(vertical.Value, frame))
                {
                    EditorGUI.DrawRect(new Rect(frame.x, y, frame.width, 1f), lineColor);
                }
            }

            if (horizontal.HasValue && vertical.HasValue)
            {
                var pointColor = Fade(AnchorPointColor, alpha);
                foreach (var x in AnchorXs(horizontal.Value, frame))
                {
                    foreach (var y in AnchorYs(vertical.Value, frame))
                    {
                        EditorGUI.DrawRect(new Rect(x - 1f, y - 1f, 3f, 3f), pointColor);
                    }
                }
            }

            var stretchColor = Fade(StretchColor, alpha);
            if (horizontal == HorizontalAnchor.Stretch)
            {
                DrawArrow(new Vector2(box.xMin + 2f, box.center.y), new Vector2(box.xMax - 2f, box.center.y), stretchColor);
            }

            if (vertical == VerticalAnchor.Stretch)
            {
                DrawArrow(new Vector2(box.center.x, box.yMin + 2f), new Vector2(box.center.x, box.yMax - 2f), stretchColor);
            }
        }

        // Where the anchor lines go, on whole pixels over the frame's own 1-pixel edges: a line between two
        // pixels would blur away.
        private static float[] AnchorXs(HorizontalAnchor anchor, Rect frame)
        {
            var left = Mathf.Round(frame.xMin);
            var right = Mathf.Round(frame.xMax) - 1f;
            return anchor switch
            {
                HorizontalAnchor.Left => new[] { left },
                HorizontalAnchor.Right => new[] { right },
                HorizontalAnchor.Stretch => new[] { left, right },
                _ => new[] { Mathf.Round(frame.center.x) }
            };
        }

        private static float[] AnchorYs(VerticalAnchor anchor, Rect frame)
        {
            var top = Mathf.Round(frame.yMin);
            var bottom = Mathf.Round(frame.yMax) - 1f;
            return anchor switch
            {
                VerticalAnchor.Top => new[] { top },
                VerticalAnchor.Bottom => new[] { bottom },
                VerticalAnchor.Stretch => new[] { top, bottom },
                _ => new[] { Mathf.Round(frame.center.y) }
            };
        }

        // A line with a small head at both ends, horizontal or vertical.
        private static void DrawArrow(Vector2 from, Vector2 to, Color color)
        {
            if (Mathf.Approximately(from.y, to.y))
            {
                EditorGUI.DrawRect(new Rect(from.x, from.y - 0.5f, to.x - from.x, 1f), color);
                EditorGUI.DrawRect(new Rect(from.x, from.y - 1.5f, 1f, 3f), color);
                EditorGUI.DrawRect(new Rect(to.x - 1f, to.y - 1.5f, 1f, 3f), color);
            }
            else
            {
                EditorGUI.DrawRect(new Rect(from.x - 0.5f, from.y, 1f, to.y - from.y), color);
                EditorGUI.DrawRect(new Rect(from.x - 1.5f, from.y, 3f, 1f), color);
                EditorGUI.DrawRect(new Rect(to.x - 1.5f, to.y - 1f, 3f, 1f), color);
            }
        }

        public static void DrawOutline(Rect rect, Color color)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), color);
        }

        private static Color Fade(Color color, float alpha) => new(color.r, color.g, color.b, color.a * alpha);
    }

    /// <summary>
    /// The Anchor Presets popup, laid out like Unity's: columns left / center / right / stretch, rows top /
    /// middle / bottom / stretch, and headers that set one axis only. Shift also sets the pivot, Alt also moves
    /// the element to the anchor.
    /// </summary>
    internal sealed class AnchorPresetPopup : PopupWindowContent
    {
        private const float Cell = 52f;
        private const float HeaderSize = 52f;
        private const float TitleHeight = 44f;
        private const float LabelSize = 16f;

        private static readonly Color BackgroundColor = new(0.22f, 0.22f, 0.22f, 1f);
        private static readonly Color SeparatorColor = new(0.12f, 0.12f, 0.12f, 1f);
        private static readonly Color HighlightColor = new(1f, 1f, 1f, 0.9f);
        private static readonly Color HoverColor = new(1f, 1f, 1f, 0.08f);

        private readonly HorizontalAnchor _horizontal;
        private readonly VerticalAnchor _vertical;
        private readonly Action<HorizontalAnchor?, VerticalAnchor?, bool, bool> _picked;
        private GUIStyle _labelStyle;

        /// <param name="picked">Called with the chosen axes (null: unchanged), and whether Shift / Alt were held.</param>
        public AnchorPresetPopup(HorizontalAnchor horizontal, VerticalAnchor vertical, Action<HorizontalAnchor?, VerticalAnchor?, bool, bool> picked)
        {
            _horizontal = horizontal;
            _vertical = vertical;
            _picked = picked;
        }

        private GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(1f, 1f, 1f, 0.6f) }
        };

        public override Vector2 GetWindowSize() =>
            new(LabelSize + HeaderSize + Cell * 4f + 12f, TitleHeight + LabelSize + HeaderSize + Cell * 4f + 12f);

        public override void OnOpen() => editorWindow.wantsMouseMove = true;

        public override void OnGUI(Rect rect)
        {
            EditorGUI.DrawRect(rect, BackgroundColor);
            GUI.Label(new Rect(8f, 4f, rect.width, 18f), "Anchor Presets", EditorStyles.boldLabel);
            GUI.Label(new Rect(8f, 22f, rect.width, 16f), "Shift: Also set pivot     Alt: Also set position", EditorStyles.miniLabel);

            var gridLeft = 6f + LabelSize + HeaderSize;
            var gridTop = TitleHeight + LabelSize + HeaderSize;
            EditorGUI.DrawRect(new Rect(6f, gridTop - 2f, rect.width - 12f, 1f), SeparatorColor);
            EditorGUI.DrawRect(new Rect(gridLeft - 2f, TitleHeight, 1f, rect.height - TitleHeight - 6f), SeparatorColor);

            for (var column = 0; column < 4; column++)
            {
                var horizontal = (HorizontalAnchor)column;
                var x = gridLeft + column * Cell;
                GUI.Label(new Rect(x, TitleHeight, Cell, LabelSize), AnchorPresets.Name(horizontal), LabelStyle);
                Button(new Rect(x, TitleHeight + LabelSize, Cell, HeaderSize), horizontal, null, horizontal == _horizontal);
            }

            for (var row = 0; row < 4; row++)
            {
                var vertical = (VerticalAnchor)row;
                var y = gridTop + row * Cell;
                RotatedLabel(new Rect(6f, y, LabelSize, Cell), AnchorPresets.Name(vertical));
                Button(new Rect(6f + LabelSize, y, HeaderSize, Cell), null, vertical, vertical == _vertical);

                for (var column = 0; column < 4; column++)
                {
                    var horizontal = (HorizontalAnchor)column;
                    Button(new Rect(gridLeft + column * Cell, y, Cell, Cell), horizontal, vertical,
                        horizontal == _horizontal && vertical == _vertical);
                }
            }

            if (Event.current.type == EventType.MouseMove)
            {
                editorWindow.Repaint();
            }
        }

        private void Button(Rect rect, HorizontalAnchor? horizontal, VerticalAnchor? vertical, bool current)
        {
            var icon = new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, rect.height - 8f);
            if (icon.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(icon, HoverColor);
            }

            AnchorPresets.DrawIcon(icon, horizontal, vertical, false);
            if (current)
            {
                AnchorPresets.DrawOutline(icon, HighlightColor);
            }

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && icon.Contains(e.mousePosition))
            {
                _picked(horizontal, vertical, e.shift, e.alt);
                e.Use();
                editorWindow.Close();
            }
        }

        private void RotatedLabel(Rect rect, string text)
        {
            var matrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-90f, rect.center);
            GUI.Label(new Rect(rect.center.x - rect.height * 0.5f, rect.center.y - rect.width * 0.5f, rect.height, rect.width), text, LabelStyle);
            GUI.matrix = matrix;
        }
    }
}
