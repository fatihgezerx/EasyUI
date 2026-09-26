using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;

namespace EasyUI
{
    /// <summary>
    /// Keeps <c>GameObject &gt; UI (Canvas) &gt; Easy UI</c> listing every saved panel, each one building it into the
    /// scene (see <see cref="EasyUIPanelBuilder"/>).
    /// </summary>
    /// <remarks>
    /// Unity's menus come from <c>[MenuItem]</c> attributes, so the items are code: <c>Generated/EasyUIPanelMenu.cs</c>
    /// next to this script, one item per panel, named after its asset and pointing at it by GUID. It is written
    /// again whenever a panel is saved, imported, renamed, moved or deleted, and after every script reload (so a
    /// fresh copy of Easy UI gets it back) - but only when its contents would change, since every write
    /// recompiles. With no panels saved, the file is removed.
    /// </remarks>
    internal static class EasyUIMenuGenerator
    {
        private const string AssemblyName = "EasyUI.Editor";
        private const string GeneratedFile = "Generated/EasyUIPanelMenu.cs";
        private const string MenuRoot = "GameObject/UI (Canvas)/Easy UI/";

        // Below Unity's own UI items (the last is 2083), with a separator before it.
        private const int MenuPriority = 2100;

        private static bool _queued;

        [InitializeOnLoadMethod]
        private static void OnScriptsReloaded() => Queue();

        /// <summary>Brings the menu up to date on the next editor update, once however often it is asked.</summary>
        public static void Queue()
        {
            if (_queued)
            {
                return;
            }

            _queued = true;
            EditorApplication.delayCall += Generate;
        }

        private static void Generate()
        {
            _queued = false;

            var asmdef = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(AssemblyName);
            if (string.IsNullOrEmpty(asmdef))
            {
                return;
            }

            var path = Path.GetDirectoryName(asmdef)!.Replace('\\', '/') + "/" + GeneratedFile;
            var panels = FindPanels();

            if (panels.Count == 0)
            {
                if (File.Exists(path))
                {
                    AssetDatabase.DeleteAsset(path);
                }

                var folder = Path.GetDirectoryName(path)!.Replace('\\', '/');
                if (AssetDatabase.IsValidFolder(folder) && AssetDatabase.FindAssets(string.Empty, new[] { folder }).Length == 0)
                {
                    AssetDatabase.DeleteAsset(folder);
                }

                return;
            }

            var code = Code(panels);
            if (File.Exists(path) && File.ReadAllText(path) == code)
            {
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, code);
            AssetDatabase.ImportAsset(path);
        }

        // Every saved panel with its menu label: the asset's name, or, when another panel has that name too,
        // the name and its folder (and a number, if even that is taken). Sorted by label, as the menu shows them.
        private static List<(string label, string guid)> FindPanels()
        {
            var found = new List<(string name, string folder, string guid)>();
            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(EasyUIPanel)))
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    found.Add((Path.GetFileNameWithoutExtension(assetPath), Path.GetFileName(Path.GetDirectoryName(assetPath)), guid));
                }
            }

            found.Sort((a, b) =>
            {
                var byName = string.Compare(a.name, b.name, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.CompareOrdinal(a.guid, b.guid);
            });

            var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var panel in found)
            {
                names.TryGetValue(panel.name, out var count);
                names[panel.name] = count + 1;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var panels = new List<(string label, string guid)>(found.Count);
            foreach (var panel in found)
            {
                var label = names[panel.name] > 1 ? $"{panel.name} ({panel.folder})" : panel.name;
                var unique = label;
                for (var n = 2; !used.Add(unique); n++)
                {
                    unique = $"{label} {n}";
                }

                panels.Add((unique, panel.guid));
            }

            return panels;
        }

        private static string Code(List<(string label, string guid)> panels)
        {
            var code = new StringBuilder(512 + panels.Count * 200);
            code.Append("// <auto-generated>\n");
            code.Append("// Written by Easy UI (EasyUIMenuGenerator) whenever a panel is saved, renamed, moved or deleted.\n");
            code.Append("// Don't edit it: it is written again.\n");
            code.Append("// </auto-generated>\n\n");
            code.Append("using UnityEditor;\n\n");
            code.Append("namespace EasyUI\n{\n");
            code.Append("    internal static class EasyUIPanelMenu\n    {\n");

            for (var i = 0; i < panels.Count; i++)
            {
                if (i > 0)
                {
                    code.Append('\n');
                }

                code.Append("        [MenuItem(\"").Append(Escape(MenuRoot + panels[i].label)).Append("\", false, ").Append(MenuPriority).Append(")]\n");
                code.Append("        private static void Create").Append(i).Append("(MenuCommand command) => EasyUIPanelBuilder.Create(\"")
                    .Append(panels[i].guid).Append("\", command);\n");
            }

            code.Append("    }\n}\n");
            return code.ToString();
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    // Brings the menu up to date when a panel asset is imported (saved, added), moved or renamed, or when any
    // .asset is deleted (a deleted file's type can't be told any more).
    internal sealed class EasyUIPanelPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (AnyPanel(imported) || AnyPanel(moved) || AnyAsset(deleted))
            {
                EasyUIMenuGenerator.Queue();
            }
        }

        private static bool AnyPanel(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) &&
                    AssetDatabase.GetMainAssetTypeAtPath(path) == typeof(EasyUIPanel))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool AnyAsset(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
