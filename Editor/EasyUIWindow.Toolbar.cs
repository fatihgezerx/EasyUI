using System.IO;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // The top bar: the panel's name, Open (a saved panel, or a new one), Templates, Clear (after asking) and Save.
    // Save keeps the panel as an EasyUIPanel asset named after it, in the Panels folder, from which Open brings
    // it back to keep editing.
    internal sealed partial class EasyUIWindow
    {
        private const string PanelFolder = "Assets/EasyUI Panels";

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

                if (GUILayout.Button(new GUIContent("Save", $"Save this panel in {PanelFolder}"), EditorStyles.toolbarButton, GUILayout.Width(50f)))
                {
                    SavePanel();
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

        // Into the asset it was opened from (renamed along with the panel), or a new one named after the panel.
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
                EditorUtility.DisplayDialog("Save Panel", "Give the panel a name first.", "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder(PanelFolder))
            {
                AssetDatabase.CreateFolder("Assets", Path.GetFileName(PanelFolder));
            }

            var path = $"{PanelFolder}/{name}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<EasyUIPanel>(path);
            var panel = openedAsset;

            if (panel != null && existing != panel)
            {
                if (existing == null)
                {
                    // The panel was renamed: its asset follows.
                    AssetDatabase.MoveAsset(AssetDatabase.GetAssetPath(panel), path);
                }
                else
                {
                    panel = null;
                }
            }

            if (panel == null)
            {
                if (existing != null && !EditorUtility.DisplayDialog("Save Panel",
                        $"A panel named \"{name}\" already exists. Replace it?", "Replace", "Cancel"))
                {
                    return;
                }

                panel = existing;
                if (panel == null)
                {
                    panel = CreateInstance<EasyUIPanel>();
                    AssetDatabase.CreateAsset(panel, path);
                }
            }

            document.panelName = name;
            panel.Store(document);
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            openedAsset = panel;
            ShowNotification(new GUIContent($"Saved {name}"));
        }
    }
}
