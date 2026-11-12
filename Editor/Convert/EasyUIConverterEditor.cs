using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace EasyUI
{
    /// <summary>
    /// The Inspector of <see cref="EasyUIConverter"/>: the Easy UI panel the panel is converted into (empty: it asks
    /// where to save) and the Convert to Easy UI Data button, which reads the object and everything under it
    /// (<see cref="EasyUISceneConverter"/>) and saves it as an <see cref="EasyUIPanel"/> asset - the way the window's
    /// Save does, so the panel can be opened and edited in Easy UI from then on.
    /// </summary>
    [CustomEditor(typeof(EasyUIConverter))]
    internal sealed class EasyUIConverterEditor : Editor
    {
        private const string Title = "Convert to Easy UI Data";

        public override void OnInspectorGUI()
        {
            var converter = (EasyUIConverter)target;
            var panel = LoadPanel(converter.PanelGuid);

            EditorGUI.BeginChangeCheck();
            var picked = (EasyUIPanel)EditorGUILayout.ObjectField(
                new GUIContent("Easy UI Panel", "The panel asset the conversion replaces. Empty: you choose where to save."),
                panel, typeof(EasyUIPanel), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(converter, "Set Easy UI Panel");
                converter.PanelGuid = picked != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked)) : string.Empty;
                EditorUtility.SetDirty(converter);
            }

            EditorGUILayout.HelpBox("Saves this object and everything under it as an Easy UI panel. This object becomes the panel's root.", MessageType.None);
            if (GUILayout.Button(new GUIContent(Title, "Read this panel into Easy UI data and save it as a panel asset."), GUILayout.Height(28f)))
            {
                Convert(converter);
            }
        }

        private static EasyUIPanel LoadPanel(string guid)
        {
            var path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<EasyUIPanel>(path);
        }

        private static void Convert(EasyUIConverter converter)
        {
            if (converter.transform is not RectTransform root)
            {
                EditorUtility.DisplayDialog(Title, "This object has no Rect Transform. Put the component on a UI object under a Canvas.", "OK");
                return;
            }

            var document = EasyUISceneConverter.Convert(root, out var notes);
            if (document == null)
            {
                EditorUtility.DisplayDialog(Title, notes[0], "OK");
                return;
            }

            var path = ChoosePath(converter, root.name);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            var panel = AssetDatabase.LoadAssetAtPath<EasyUIPanel>(path);
            var previous = panel != null ? panel.Document : null;
            if (panel == null)
            {
                if (AssetDatabase.LoadMainAssetAtPath(path) != null)
                {
                    EditorUtility.DisplayDialog(Title, $"\"{path}\" is not an Easy UI panel, so it wasn't replaced. Choose another name.", "OK");
                    return;
                }

                panel = ScriptableObject.CreateInstance<EasyUIPanel>();
                AssetDatabase.CreateAsset(panel, path);
            }

            // The root is called after the panel unless it has a name of its own.
            var name = Path.GetFileNameWithoutExtension(path);
            document.panelName = name;
            var rootNode = document.RootNode;
            if (IsPanelName(rootNode.name, name))
            {
                rootNode.name = string.Empty;
            }

            // Roles without a component of their own can't be read from the scene: the old design's go back by name and place.
            var roles = EasyUIRoleCarry.Carry(previous, document, notes);

            panel.Store(document);
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            EditorUserSettings.SetConfigValue(EasyUIWindow.SaveFolderKey, Path.GetDirectoryName(path)?.Replace('\\', '/'));
            EasyUIMenuGenerator.Queue();

            Undo.RecordObject(converter, "Convert to Easy UI Data");
            converter.PanelGuid = AssetDatabase.AssetPathToGUID(path);
            EditorUtility.SetDirty(converter);
            EditorGUIUtility.PingObject(panel);

            Report(path, document.nodes.Count, roles, notes);
        }

        // The panel's own name, or with the suffixes Unity gives a duplicate ("Panel (Copy) (1)", "Panel (Clone)"): then the root
        // goes without a name of its own, called after the panel as it is built.
        private static bool IsPanelName(string rootName, string panelName) =>
            rootName == panelName || (rootName.StartsWith(panelName) &&
                Regex.IsMatch(rootName.Substring(panelName.Length), @"^( ?\((Copy|Clone|\d+)\))+$"));

        // The panel set on the component (after a question: its design is replaced), else wherever the save dialog says.
        private static string ChoosePath(EasyUIConverter converter, string rootName)
        {
            var existing = LoadPanel(converter.PanelGuid);
            if (existing != null)
            {
                return EditorUtility.DisplayDialog(Title, $"Replace the design of \"{existing.name}\" with this panel? What was changed in Easy UI is lost.", "Replace", "Cancel")
                    ? AssetDatabase.GetAssetPath(existing)
                    : null;
            }

            var name = rootName.Trim();
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(invalid.ToString(), string.Empty);
            }

            if (name.Length == 0)
            {
                name = "New Panel";
            }

            var last = EditorUserSettings.GetConfigValue(EasyUIWindow.SaveFolderKey);
            var folder = !string.IsNullOrEmpty(last) && AssetDatabase.IsValidFolder(last) ? last : "Assets";
            return EditorUtility.SaveFilePanelInProject(Title, name, "asset", "Choose where to save this panel.", folder);
        }

        private static void Report(string path, int elements, int roles, List<string> notes)
        {
            var message = new StringBuilder($"[EasyUI] Converted into {path}: {elements} elements" + (roles > 0 ? $", {roles} roles kept from the old design." : "."));
            if (notes.Count > 0)
            {
                message.Append(" Not kept:");
                foreach (var note in notes)
                {
                    message.Append("\n - ").Append(note);
                }

                Debug.LogWarning(message.ToString());
                return;
            }

            Debug.Log(message.ToString());
        }
    }

    /// <summary>Takes <see cref="EasyUIConverter"/> out of every scene in a build: it only matters in the editor.</summary>
    internal sealed class EasyUIConverterBuildStrip : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (!BuildPipeline.isBuildingPlayer)
            {
                return;
            }

            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var converter in root.GetComponentsInChildren<EasyUIConverter>(true))
                {
                    Object.DestroyImmediate(converter);
                }
            }
        }
    }
}
