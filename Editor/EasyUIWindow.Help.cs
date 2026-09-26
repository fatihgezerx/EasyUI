using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // The round info button in the bottom-right corner: a click opens a small panel above it listing every mouse
    // and key action of the workspace; another click on it, or a click anywhere else, closes it.
    internal sealed partial class EasyUIWindow
    {
        private const float InfoButtonSize = 28f;
        private const float InfoMargin = 12f;
        private const float InfoPanelWidth = 330f;
        private const float InfoLineHeight = 18f;

        private static readonly Color InfoPanelColor = new(0.19f, 0.19f, 0.19f, 0.97f);
        private static readonly Color InfoPanelBorderColor = new(0.09f, 0.09f, 0.09f, 1f);

        // What each action is, then how to do it.
        private static readonly (string action, string input)[] InfoLines =
        {
            ("Add an element", "Right-click"),
            ("Select", "Click"),
            ("Add to / remove from the selection", "Ctrl + click"),
            ("Select with a box", "Drag on an empty spot"),
            ("Move the selection", "Drag a selected element"),
            ("Resize", "Drag an edge or a corner"),
            ("Snap to the grid", "Hold Shift while dragging"),
            ("Duplicate", "Ctrl + D"),
            ("Delete", "Delete"),
            ("Rename", "Click the pencil"),
            ("Frame the selection (or the canvas)", "F"),
            ("Pan", "Middle drag, or Alt + drag"),
            ("Zoom", "Scroll wheel")
        };

        private bool _infoOpen;
        private bool _hoverInfo;
        private Texture2D _infoButtonTexture;
        private Texture2D _infoButtonHoverTexture;
        private GUIStyle _infoActionStyle;
        private GUIStyle _infoInputStyle;
        private Texture _infoIcon;

        private Texture InfoIcon => _infoIcon ??= EditorGUIUtility.IconContent("UnityEditor.InspectorWindow").image;

        private GUIStyle InfoActionStyle => _infoActionStyle ??= new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = new Color(1f, 1f, 1f, 0.8f) }
        };

        private GUIStyle InfoInputStyle => _infoInputStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleRight,
            normal = { textColor = new Color(1f, 1f, 1f, 0.9f) }
        };

        // In the view's bottom-right corner; window or clip pixels, whichever `view` is in.
        private static Rect InfoButtonRect(Rect view) =>
            new(view.xMax - InfoMargin - InfoButtonSize, view.yMax - InfoMargin - InfoButtonSize, InfoButtonSize, InfoButtonSize);

        private static Rect InfoPanelRect(Rect view)
        {
            var button = InfoButtonRect(view);
            var height = InfoLines.Length * InfoLineHeight + 16f;
            return new Rect(button.xMax - InfoPanelWidth, button.y - 8f - height, InfoPanelWidth, height);
        }

        private bool IsOverInfo(Vector2 pointer) =>
            InfoButtonRect(_view).Contains(pointer) || (_infoOpen && InfoPanelRect(_view).Contains(pointer));

        // A press on the button opens or closes the panel; a press on the open panel does nothing; a press anywhere
        // else closes it (and goes on to do what it does there). Returns whether the press was the info's.
        private bool HandleInfoPress(Vector2 pointer)
        {
            if (InfoButtonRect(_view).Contains(pointer))
            {
                _infoOpen = !_infoOpen;
                Repaint();
                return true;
            }

            if (_infoOpen && InfoPanelRect(_view).Contains(pointer))
            {
                return true;
            }

            if (_infoOpen)
            {
                _infoOpen = false;
                Repaint();
            }

            return false;
        }

        private void DestroyInfoTextures()
        {
            DestroyTexture(ref _infoButtonTexture);
            DestroyTexture(ref _infoButtonHoverTexture);
        }

        // Drawn inside the workspace's clip: `view` starts at 0, 0.
        private void DrawInfo(Rect view)
        {
            if (_infoButtonTexture == null || _infoButtonHoverTexture == null)
            {
                _infoButtonTexture = CircleTexture(new Color(0.26f, 0.26f, 0.26f, 1f));
                _infoButtonHoverTexture = CircleTexture(new Color(0.36f, 0.36f, 0.36f, 1f));
            }

            var button = InfoButtonRect(view);
            GUI.DrawTexture(button, _hoverInfo || _infoOpen ? _infoButtonHoverTexture : _infoButtonTexture);
            var icon = new Rect(button.center.x - 8f, button.center.y - 8f, 16f, 16f);
            if (InfoIcon != null)
            {
                GUI.DrawTexture(icon, InfoIcon, ScaleMode.ScaleToFit);
            }
            else
            {
                GUI.Label(button, "i", CenteredMiniLabel);
            }

            if (!_infoOpen)
            {
                return;
            }

            var panel = InfoPanelRect(view);
            EditorGUI.DrawRect(panel, InfoPanelColor);
            DrawBorder(panel, InfoPanelBorderColor, 1f);
            for (var i = 0; i < InfoLines.Length; i++)
            {
                var line = new Rect(panel.x + 10f, panel.y + 8f + i * InfoLineHeight, panel.width - 20f, InfoLineHeight);
                GUI.Label(line, InfoLines[i].action, InfoActionStyle);
                GUI.Label(line, InfoLines[i].input, InfoInputStyle);
            }
        }
    }
}
