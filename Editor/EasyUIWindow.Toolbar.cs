using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // The top bar: the panel's name, Open (a saved panel, or a new one), Templates, Clear (after asking) and Save.
    // Save asks where to keep the panel, as an EasyUIPanel asset from which Open brings it back to keep editing.
    // The save dialog starts in the folder of the panel being edited, or else in the folder saved to last.
    internal sealed partial class EasyUIWindow
    {
        // The folder saved to last, kept per project (in its UserSettings), so the dialog starts there next time.
        private const string SaveFolderKey = "EasyUI.SaveFolder";

        [SerializeField] private EasyUIPanel openedAsset;

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label("Panel", EditorStyles.miniLabel, GUILayout.Width(36f));
                document.panelName = EditorGUILayout.TextField(document.panelName, EditorStyles.toolbarTextField, GUILayout.Width(200f));
                GUILayout.Space(6f);

                if (EditorGUILayout.DropdownButton(new GUIContent("Open"), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(56f)))
                {
                    ShowOpenMenu();
                }

                if (EditorGUILayout.DropdownButton(new GUIContent("Templates"), FocusType.Passive, EditorStyles.toolbarDropDown, GUILayout.Width(82f)))
                {
                    ShowTemplatesMenu();
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
            menu.AddItem(new GUIContent("New Panel"), false, NewPanel);
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
                    menu.AddItem(new GUIContent(Path.GetFileNameWithoutExtension(path)), panel == openedAsset, () => OpenPanel(panel));
                }
            }

            menu.ShowAsContext();
        }

        // Ready-made panels to start from; none ship yet.
        private static void ShowTemplatesMenu()
        {
            var menu = new GenericMenu();
            menu.AddDisabledItem(new GUIContent("No templates yet"));
            menu.ShowAsContext();
        }

        private void NewPanel()
        {
            document = new EasyUIDocument();
            openedAsset = null;
            ResetSelection();
        }

        private void OpenPanel(EasyUIPanel panel)
        {
            document = panel.Document.Clone();
            openedAsset = panel;
            ResetSelection();
        }

        private void ClearPanel()
        {
            if (document.nodes.Count == 0)
            {
                return;
            }

            if (!EditorUtility.DisplayDialog("Clear Panel",
                    $"Remove every element from \"{document.panelName}\"? This can't be undone.", "Clear", "Cancel"))
            {
                return;
            }

            document.nodes.Clear();
            ResetSelection();
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
