using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // Element labels: above each element (or just inside it, where the canvas's own label is), its layer in a
    // small rounded badge, then its name, then a pencil that edits it. The label is never wider than the element:
    // a longer name fades out at its end and shows in full once the element is wide enough, and the pencil stays
    // within the element too. A new element asks for its name right away; Enter, the check button, or a click
    // anywhere else (starting a drag, for one) confirms it, Escape leaves it as it was.
    internal sealed partial class EasyUIWindow
    {
        private const string RenameControl = "EasyUIRename";
        private const float LabelHeight = 20f;
        private const float BadgeSize = 18f;
        private const float BadgeGap = 5f;
        private const float PencilSize = 16f;
        private const float ConfirmSize = 18f;
        private const float FadeWidth = 28f;
        private const float MinRenameWidth = 140f;

        private int _renamingId;
        private string _renameText = string.Empty;
        private bool _focusRename;
        private int _hoverPencilId;

        private readonly GUIContent _measure = new();
        private static readonly string[] LayerTexts = new string[64];

        private Texture2D _badgeTexture;
        private Texture2D _fadeTexture;
        private GUIStyle _badgeStyle;
        private Texture _pencilIcon;

        // A rounded square (no hard corners) with the layer in it.
        private GUIStyle BadgeStyle
        {
            get
            {
                if (_badgeTexture == null)
                {
                    _badgeTexture = RoundedTexture(new Color(0.24f, 0.24f, 0.26f, 1f), new Color(1f, 1f, 1f, 0.35f));
                    _badgeStyle = null;
                }

                return _badgeStyle ??= new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    fontSize = 11,
                    border = new RectOffset(6, 6, 6, 6),
                    normal = { background = _badgeTexture, textColor = new Color(1f, 1f, 1f, 0.9f) }
                };
            }
        }

        // From clear to the canvas's color, laid over the end of a name that doesn't fit, so it fades out.
        private Texture2D FadeTexture
        {
            get
            {
                if (_fadeTexture != null)
                {
                    return _fadeTexture;
                }

                const int width = 32;
                _fadeTexture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp
                };

                for (var x = 0; x < width; x++)
                {
                    _fadeTexture.SetPixel(x, 0, new Color(CanvasColor.r, CanvasColor.g, CanvasColor.b, x / (width - 1f)));
                }

                _fadeTexture.Apply();
                return _fadeTexture;
            }
        }

        private Texture PencilIcon => _pencilIcon ??= EditorGUIUtility.IconContent("editicon.sml").image;

        // 16 x 16, corners rounded by 5 pixels, a 1-pixel rim; drawn 9-sliced, so it keeps its corners at any size.
        private static Texture2D RoundedTexture(Color fill, Color rim)
        {
            const int size = 16;
            const float radius = 5f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Distance outside the rounded rect, from its edge (negative inside).
                    var dx = Mathf.Max(Mathf.Max(radius - (x + 0.5f), x + 0.5f - (size - radius)), 0f);
                    var dy = Mathf.Max(Mathf.Max(radius - (y + 0.5f), y + 0.5f - (size - radius)), 0f);
                    var outside = Mathf.Sqrt(dx * dx + dy * dy) - radius;
                    var alpha = Mathf.Clamp01(0.5f - outside);
                    var color = outside > -1.2f ? rim : fill;
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * alpha));
                }
            }

            texture.Apply();
            return texture;
        }

        private void DestroyLabelTextures()
        {
            DestroyTexture(ref _badgeTexture);
            DestroyTexture(ref _fadeTexture);
        }

        private static string LayerText(int depth)
        {
            if (depth >= LayerTexts.Length)
            {
                return depth.ToString();
            }

            return LayerTexts[depth] ??= depth.ToString();
        }

        #region Layout

        // The label's row, as wide as the element (with room for the badge and the pencil at least): above its
        // top edge, or just inside it when that is where the canvas's own label is. Window or clip pixels,
        // whichever `screen` is in.
        private Rect LabelRect(Rect screen)
        {
            var canvasTop = ToScreen(Vector2.zero).y;
            var y = Mathf.Abs(screen.y - canvasTop) < 22f ? screen.y + 4f : screen.y - 22f;
            return new Rect(screen.x, y, Mathf.Max(screen.width, BadgeSize + BadgeGap + PencilSize + 4f), LabelHeight);
        }

        private static Rect BadgeRect(Rect label) => new(label.x, label.y + 1f, BadgeSize, BadgeSize);

        // Between the badge and the pencil's furthest place.
        private static Rect NameRect(Rect label)
        {
            var x = label.x + BadgeSize + BadgeGap;
            return new Rect(x, label.y, Mathf.Max(label.xMax - PencilSize - 2f - x, 0f), label.height);
        }

        private float NameWidth(string text, GUIStyle style)
        {
            _measure.text = text;
            return style.CalcSize(_measure).x;
        }

        // Right after the name, but never past the element's right edge.
        private Rect PencilRect(EasyUINode node)
        {
            var label = LabelRect(ToScreen(node.Rect));
            var name = NameRect(label);
            var x = Mathf.Min(name.x + NameWidth(LabelText(node, Problem(node)), LabelStyle) + 4f, label.xMax - PencilSize);
            return new Rect(x, label.y + 2f, PencilSize, PencilSize);
        }

        // The element whose pencil is under the pointer (window pixels), topmost first; none while it is renamed.
        private EasyUINode PencilAt(Vector2 pointer)
        {
            var order = DrawOrder();
            for (var i = order.Count - 1; i >= 0; i--)
            {
                if (order[i].id != _renamingId && PencilRect(order[i]).Contains(pointer))
                {
                    return order[i];
                }
            }

            return null;
        }

        private string LabelText(EasyUINode node, string problem) =>
            problem != null ? $"{NameOf(node)}  -  {problem}" : NameOf(node);

        #endregion

        #region Drawing

        private void DrawNodeLabel(EasyUINode node, Rect screen, string problem)
        {
            var label = LabelRect(screen);
            _measure.text = LayerText(document.DepthOf(node));
            BadgeStyle.Draw(BadgeRect(label), _measure, false, false, false, false);

            // Renamed in the text field drawn over it (DrawRenameField).
            if (node.id == _renamingId)
            {
                return;
            }

            var style = problem != null ? InvalidLabelStyle : LabelStyle;
            var text = LabelText(node, problem);
            var width = NameWidth(text, style);
            var name = NameRect(label);

            GUI.BeginClip(name);
            GUI.Label(new Rect(0f, 0f, width + 4f, name.height), text, style);
            GUI.EndClip();

            if (width > name.width)
            {
                GUI.DrawTexture(new Rect(name.xMax - FadeWidth, name.y, FadeWidth, name.height), FadeTexture);
            }

            var pencil = new Rect(Mathf.Min(name.x + width + 4f, label.xMax - PencilSize), label.y + 2f, PencilSize, PencilSize);
            var color = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, node.id == _hoverPencilId ? 1f : 0.55f);
            if (PencilIcon != null)
            {
                GUI.DrawTexture(pencil, PencilIcon, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Label(pencil, "✎", LabelStyle);
            }

            GUI.color = color;
        }

        #endregion

        #region Renaming

        private void StartRename(EasyUINode node)
        {
            _renamingId = node.id;
            _renameText = node.name;
            _focusRename = true;
        }

        // Keeps the typed name - or, left empty, the element type's own name (e.g. "Image"). Does nothing while
        // no name is being typed.
        private void ConfirmRename()
        {
            if (_renamingId == 0)
            {
                return;
            }

            var node = document.Find(_renamingId);
            _renamingId = 0;
            if (node != null)
            {
                var name = _renameText.Trim();
                node.name = name.Length > 0 ? name : EasyUINode.DefaultName(node.type);
            }

            EndTextEditing();
            Repaint();
        }

        // Leaves the name as it was (a new element gets its type's name).
        private void CancelRename()
        {
            var node = document.Find(_renamingId);
            _renamingId = 0;
            if (node != null && string.IsNullOrEmpty(node.name))
            {
                node.name = EasyUINode.DefaultName(node.type);
            }

            EndTextEditing();
            Repaint();
        }

        // Lets go of the name field for good. Only unfocusing it would leave Unity's text editor active - the
        // field isn't drawn any more - and with it "a text is being edited", which would keep the window's keys
        // (Delete, F) and clicks from working until focus left the window and came back.
        private static void EndTextEditing()
        {
            GUI.FocusControl(null);
            GUIUtility.keyboardControl = 0;
            EditorGUIUtility.editingTextField = false;
        }

        // Where the name is typed, and the check button after it; window pixels.
        private Rect RenameFieldRect(EasyUINode node)
        {
            var label = LabelRect(ToScreen(node.Rect));
            var x = label.x + BadgeSize + BadgeGap;
            var width = Mathf.Max(label.xMax - ConfirmSize - 2f - x, MinRenameWidth);
            return new Rect(x, label.y + 1f, width, BadgeSize);
        }

        private bool IsOverRenameField(Vector2 pointer)
        {
            var node = document.Find(_renamingId);
            if (node == null)
            {
                return false;
            }

            var field = RenameFieldRect(node);
            field.width += ConfirmSize + 2f;
            return field.Contains(pointer);
        }

        // Drawn on every event, over the workspace, while an element is being named.
        private void DrawRenameField()
        {
            var node = document.Find(_renamingId);
            if (node == null)
            {
                _renamingId = 0;
                return;
            }

            var e = Event.current;
            if (e.type == EventType.KeyDown && GUI.GetNameOfFocusedControl() == RenameControl)
            {
                if (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter)
                {
                    ConfirmRename();
                    e.Use();
                    return;
                }

                if (e.keyCode == KeyCode.Escape)
                {
                    CancelRename();
                    e.Use();
                    return;
                }
            }

            var field = RenameFieldRect(node);
            GUI.SetNextControlName(RenameControl);
            _renameText = EditorGUI.TextField(field, _renameText);
            if (_focusRename)
            {
                EditorGUI.FocusTextInControl(RenameControl);
                _focusRename = false;
            }

            if (GUI.Button(new Rect(field.xMax + 2f, field.y, ConfirmSize, ConfirmSize), new GUIContent("✓", "Confirm the name"), EditorStyles.miniButton))
            {
                ConfirmRename();
            }
        }

        #endregion
    }
}
