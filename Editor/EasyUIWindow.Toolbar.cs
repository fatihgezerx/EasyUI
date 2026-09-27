using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // The top bar: the design's name, Open (a saved design, or a new one), Clear (after asking) and Save. Save asks
    // where to keep the design, as an EasyUIPanel asset from which Open brings it back to keep editing. The save
    // dialog starts in the folder of the design being edited, or else in the folder saved to last.
    //
    // Every design starts with its root element: an Empty stretched over the whole canvas, holding every other
    // element, which is the window itself - what gets built, and what a system's window role (e.g. the inventory's)
    // goes on. It stays: it can be moved, resized, renamed and given roles, but not deleted or copied. Without a
    // name of its own, it is called after the panel. When the canvas changes size, it follows as its anchors say.
    //
    // Whatever replaces the design being edited - a new or saved design from Open - asks first when it has
    // changes that aren't saved, and goes ahead at once when it hasn't.
    internal sealed partial class EasyUIWindow
    {
        // The folder saved to last, kept per project (in its UserSettings), so the dialog starts there next time.
        private const string SaveFolderKey = "EasyUI.SaveFolder";

        [SerializeField] private EasyUIPanel openedAsset;

        // The design as last saved or opened - or as a new one starts - without its foldouts: what unsaved changes
        // are measured against. Kept with the window, so a script reload doesn't lose it.
        [SerializeField] private string savedDesign;

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
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
            menu.AddItem(new GUIContent("New Panel"), false, () =>
            {
                if (AskToDiscard("New Panel", "A new panel starts empty, so they will be lost."))
                {
                    NewDesign();
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

        // An empty design: only its root element.
        private void NewDesign()
        {
            document = new EasyUIDocument();
            EnsureRoot();
            openedAsset = null;
            ResetHierarchy();
            ClearHistory();
            ResetSelection();
            MarkSaved();
        }

        private void OpenPanel(EasyUIPanel panel)
        {
            document = panel.Document.Clone();
            EnsureRoot();
            EasyUIParts.EnsureParts(document);
            openedAsset = panel;
            ResetHierarchy();
            ClearHistory();
            ResetSelection();
            MarkSaved();
        }

        // Every element but the root element, which stays.
        private void ClearPanel()
        {
            if (document.nodes.Count == 0 || (document.nodes.Count == 1 && document.RootNode != null))
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Clear Panel",
                    $"Remove every element from \"{document.panelName}\"? Ctrl + Z brings them back.", "Clear", "Cancel"))
            {
                return;
            }

            document.nodes.RemoveAll(node => !document.IsRootNode(node));
            ResetSelection();
        }

        // Whether the design being edited may be replaced: at once when it has no unsaved changes, otherwise only
        // once accepted. `outcome` says what happens to the changes.
        private bool AskToDiscard(string title, string outcome) =>
            !HasUnsavedChanges() || EditorUtility.DisplayDialog(title,
                $"\"{document.panelName}\" has changes that aren't saved. {outcome}", "Accept", "Cancel");

        private void MarkSaved() => savedDesign = WithoutFoldouts(EditorJsonUtility.ToJson(document));

        private bool HasUnsavedChanges() => WithoutFoldouts(EditorJsonUtility.ToJson(document)) != savedDesign;

        // A design without its root element - a new one, or one saved by an older Easy UI - gets one stretched over
        // the whole canvas, with the elements it has put in it.
        private void EnsureRoot()
        {
            if (document.RootNode != null)
            {
                document.Upgrade(Vector2.zero);
                return;
            }

            var canvas = _canvasSize != Vector2.zero ? _canvasSize
                : document.canvasSize.x > 0f && document.canvasSize.y > 0f ? document.canvasSize
                : EasyUIDocument.FallbackCanvasSize;
            document.canvasSize = canvas;
            document.Upgrade(canvas);
        }

        // The elements' positions are measured on the canvas: when it changes size (e.g. the Game view's), the root
        // follows it as its anchors say - stretched, it keeps covering it - and everything in it follows the root.
        private void FollowCanvas()
        {
            var from = document.canvasSize;
            if (_canvasSize == Vector2.zero || from == _canvasSize)
            {
                return;
            }

            document.canvasSize = _canvasSize;
            var root = document.RootNode;
            if (root != null && from.x > 0f && from.y > 0f)
            {
                FollowParent(root, new Rect(Vector2.zero, from), CanvasRect);
            }
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
