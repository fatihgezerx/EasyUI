using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // The top bar: Panel or Popup, the design's name, Open (a saved design, or a new one), Clear (after asking) and
    // Save. Save asks where to keep the design, as an EasyUIPanel asset from which Open brings it back to keep
    // editing. The save dialog starts in the folder of the design being edited, or else in the folder saved to last.
    //
    // A Panel is the window itself: stretched over the canvas, its elements in it. A Popup starts with an Empty
    // named Popup that holds every other element and is itself the window: it is what gets built, where it was
    // drawn on the canvas. It stays while the design is a Popup - it can be moved, resized and renamed, but not
    // deleted or copied. Switching between the two starts a new, empty design of that kind.
    //
    // Whatever replaces the design being edited - switching Panel / Popup, or a new or saved design from Open -
    // asks first when it has changes that aren't saved, and goes ahead at once when it hasn't.
    internal sealed partial class EasyUIWindow
    {
        // The folder saved to last, kept per project (in its UserSettings), so the dialog starts there next time.
        private const string SaveFolderKey = "EasyUI.SaveFolder";

        private const string PopupName = "Popup";

        // A new, empty Popup's size, in canvas units; it starts in the middle of the canvas.
        private static readonly Vector2 DefaultPopupSize = new(600f, 400f);

        // For a design saved before the canvas's size was kept, when there is no canvas to measure either.
        private static readonly Vector2 FallbackCanvasSize = new(1920f, 1080f);

        [SerializeField] private EasyUIPanel openedAsset;

        // The design as last saved or opened - or as a new one starts - without its foldouts: what unsaved changes
        // are measured against. Kept with the window, so a script reload doesn't lose it.
        [SerializeField] private string savedDesign;

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var kind = (EasyUIDocumentKind)EditorGUILayout.EnumPopup(document.kind, EditorStyles.toolbarDropDown, GUILayout.Width(62f));
                if (kind != document.kind)
                {
                    SetKind(kind);
                }

                document.panelName = EditorGUILayout.TextField(document.panelName, EditorStyles.toolbarTextField, GUILayout.Width(200f));
                GUILayout.Space(6f);

                if (EditorGUILayout.DropdownButton(new GUIContent("Open"), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(56f)))
                {
                    // A name being typed is part of the design the menu may replace.
                    ConfirmRename();
                    ShowOpenMenu();
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Clear", "Remove every element from this panel"), EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    ClearPanel();
                }

                if (GUILayout.Button(new GUIContent("Save", "Choose where to save this panel"), EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    SavePanel();

                    // The save dialog ran inside this event: end it here, before the layout it interrupted.
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void ShowOpenMenu()
        {
            var menu = new GenericMenu();
            var kind = document.kind;
            menu.AddItem(new GUIContent("New " + kind), false, () =>
            {
                if (AskToDiscard("New " + kind, $"A new {kind} starts empty, so they will be lost."))
                {
                    NewDesign(kind);
                }
            });
            menu.AddSeparator(string.Empty);

            var guids = AssetDatabase.FindAssets("t:" + nameof(EasyUIPanel));
            if (guids.Length == 0)
            {
                menu.AddDisabledItem(new GUIContent("No saved panels yet"));
            }

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var panel = AssetDatabase.LoadAssetAtPath<EasyUIPanel>(path);
                if (panel != null)
                {
                    var name = Path.GetFileNameWithoutExtension(path);
                    menu.AddItem(new GUIContent(name), panel == openedAsset, () =>
                    {
                        if (AskToDiscard("Open " + name, $"Opening \"{name}\" replaces them, so they will be lost."))
                        {
                            OpenPanel(panel);
                        }
                    });
                }
            }

            menu.ShowAsContext();
        }

        // An empty design of `kind`: a Popup's has only its Popup element.
        private void NewDesign(EasyUIDocumentKind kind)
        {
            document = new EasyUIDocument
            {
                kind = kind,
                panelName = "New " + kind
            };

            EnsurePopup();
            openedAsset = null;
            ClearHistory();
            ResetSelection();
            MarkSaved();
        }

        private void OpenPanel(EasyUIPanel panel)
        {
            document = panel.Document.Clone();
            EasyUIParts.EnsureParts(document);
            EnsurePopup();
            openedAsset = panel;
            ClearHistory();
            ResetSelection();
            MarkSaved();
        }

        // Every element but a Popup's Popup element, which stays.
        private void ClearPanel()
        {
            if (document.nodes.Count == 0 || (document.nodes.Count == 1 && document.PopupNode != null))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Clear Panel",
                    $"Remove every element from \"{document.panelName}\"? Ctrl + Z brings them back.", "Clear", "Cancel"))
            {
                return;
            }

            document.nodes.RemoveAll(node => !document.IsPopupNode(node));
            ResetSelection();
        }

        // Starts an empty design of `kind`: at once when nothing would be lost, otherwise once accepted.
        private void SetKind(EasyUIDocumentKind kind)
        {
            ConfirmRename();
            if (!HasUnsavedChanges())
            {
                NewDesign(kind);
                return;
            }

            if (AskToDiscard($"Switch to {kind}", $"A {kind} starts empty, so they will be lost."))
            {
                NewDesign(kind);
            }

            // The dialog ran inside this event: end it here, before the layout it interrupted.
            GUIUtility.ExitGUI();
        }

        // Whether the design being edited may be replaced: at once when it has no unsaved changes, otherwise only
        // once accepted. `outcome` says what happens to the changes.
        private bool AskToDiscard(string title, string outcome) =>
            !HasUnsavedChanges() || EditorUtility.DisplayDialog(title,
                $"\"{document.panelName}\" has changes that aren't saved. {outcome}", "Accept", "Cancel");

        private void MarkSaved() => savedDesign = WithoutFoldouts(EditorJsonUtility.ToJson(document));

        private bool HasUnsavedChanges() => WithoutFoldouts(EditorJsonUtility.ToJson(document)) != savedDesign;

        // A Popup without its Popup element - a new one - gets one in the middle of the canvas. Should a saved Popup
        // have lost it, the new one goes around the elements left, and they go in it.
        private void EnsurePopup()
        {
            if (document.kind != EasyUIDocumentKind.Popup || document.PopupNode != null)
            {
                return;
            }

            var any = false;
            var bounds = default(Rect);
            foreach (var node in document.nodes)
            {
                if (document.Find(node.parentId) == null)
                {
                    bounds = any ? Rect.MinMaxRect(Mathf.Min(bounds.xMin, node.Rect.xMin), Mathf.Min(bounds.yMin, node.Rect.yMin),
                        Mathf.Max(bounds.xMax, node.Rect.xMax), Mathf.Max(bounds.yMax, node.Rect.yMax)) : node.Rect;
                    any = true;
                }
            }

            if (!any)
            {
                var canvas = _canvasSize != Vector2.zero ? _canvasSize
                    : document.canvasSize.x > 0f && document.canvasSize.y > 0f ? document.canvasSize
                    : FallbackCanvasSize;
                var size = Vector2.Min(DefaultPopupSize, canvas);
                bounds = new Rect((canvas - size) * 0.5f, size);
            }

            var popup = new EasyUINode
            {
                id = document.nextId++,
                type = EasyUIElementType.EmptyObject,
                name = PopupName,
                position = bounds.position,
                size = bounds.size
            };

            foreach (var node in document.nodes)
            {
                if (document.Find(node.parentId) == null)
                {
                    node.parentId = popup.id;
                }
            }

            // First, so it is drawn under - and built before - everything in it.
            document.nodes.Insert(0, popup);
            document.popupId = popup.id;
            Touch(popup);
        }

        private void ResetSelection()
        {
            ClearSelection();
            _renamingId = 0;
            _drag = Drag.None;
            EndTextEditing();
            Repaint();
        }

        // Asks where to save, offering the panel's name, then saves into that asset - the one it was opened from,
        // another saved panel (the dialog has asked before replacing it), or a new one. The panel takes the file's
        // name, as Open lists it.
        private void SavePanel()
        {
            ConfirmRename();

            var name = document.panelName.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid.ToString(), string.Empty);
            }

            if (name.Length == 0)
            {
                name = "New Panel";
            }

            var path = EditorUtility.SaveFilePanelInProject("Save Panel", name, "asset",
                "Choose where to save this panel.", SaveFolder());
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var panel = AssetDatabase.LoadAssetAtPath<EasyUIPanel>(path);
            if (panel == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    EditorUtility.DisplayDialog("Save Panel",
                        $"\"{path}\" is not an Easy UI panel, so it wasn't replaced. Choose another name.", "OK");
                    return;
                }

                panel = CreateInstance<EasyUIPanel>();
                AssetDatabase.CreateAsset(panel, path);
            }

            EditorUserSettings.SetConfigValue(SaveFolderKey, Path.GetDirectoryName(path)?.Replace('\\', '/'));

            name = Path.GetFileNameWithoutExtension(path);
            document.panelName = name;
            if (_canvasSize != Vector2.zero)
            {
                document.canvasSize = _canvasSize;
            }

            panel.Store(document);
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            openedAsset = panel;
            MarkSaved();
            EasyUIMenuGenerator.Queue();
            ShowNotification(new GUIContent($"Saved {name}"));
        }

        // Where the save dialog starts: the folder of the panel being edited, the one saved to last, or Assets.
        private string SaveFolder()
        {
            if (openedAsset != null)
            {
                var folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(openedAsset))?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(folder) && AssetDatabase.IsValidFolder(folder))
                {
                    return folder;
                }
            }

            var last = EditorUserSettings.GetConfigValue(SaveFolderKey);
            return !string.IsNullOrEmpty(last) && AssetDatabase.IsValidFolder(last) ? last : "Assets";
        }
    }
}
