using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Networking;

namespace EasyUI.Setup
{
    /// <summary>
    /// Keeps the optional studio look of the Easy UI window in step with the project.
    /// </summary>
    /// <remarks>
    /// Easy UI itself needs nothing: the classic IMGUI window works on its own.
    /// This assembly references nothing, so it always compiles. Whenever scripts reload or an assembly definition
    /// appears or disappears, it looks for the optional <c>Veneer</c> system and sets or clears its scripting
    /// define symbol (<c>HAS_VENEER</c>). Easy UI's studio window code is compiled only while that symbol is set (#if), and the Editor assembly lists the kit as a reference. Until it is installed
    /// the classic IMGUI window takes over - no errors, no change in behaviour -
    /// and this guard offers to download the kit into
    /// <c>Assets/Scripts/Veneer</c> exactly as if it had been copied there by hand.
    /// It also keeps <c>HAS_EASYUI</c> set while Easy UI is in the project, so code that uses it from outside
    /// can be left out of compilation once it's removed. When Easy UI is deleted, the guard clears its own symbol and
    /// sets the shared one to what is still installed, so other systems' assemblies that need it keep compiling.
    /// </remarks>
    [InitializeOnLoad]
    internal sealed class DependencyGuard : AssetPostprocessor, IActiveBuildTargetChanged
    {
        internal const string SystemName = "Easy UI";
        private const string DeclinedKey = "EasyUI.Setup.DeclinedDependencies";
        private const string SetupAsmdefFile = "EasyUI.Setup.asmdef";
        private const string OwnDefine = "HAS_EASYUI";
        private const string Branch = "main";

        /// <summary>Everything Easy UI can use. Optional ones (with a purpose) only enable extra features.</summary>
        internal static readonly Dependency[] Dependencies =
        {
            Dependency.Repository("Veneer", "Veneer.Editor", "HAS_VENEER", "https://github.com/fatihgezerx/Veneer", "Assets/Scripts/Veneer", "for the studio-style Easy UI window"),
        };

        private static readonly List<Download> Downloads = new();

        private sealed class Download
        {
            public Dependency Dependency;
            public UnityWebRequest Request;
        }

        static DependencyGuard()
        {
            EditorApplication.delayCall += () => Refresh(true);
        }

        public int callbackOrder => 0;

        public void OnActiveBuildTargetChanged(BuildTarget previousTarget, BuildTarget newTarget) => Refresh(false);

        // An assembly definition appeared or disappeared (a folder added / deleted): update the symbols right
        // away, during this import, so the compilation that follows already uses them.
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            // Easy UI itself is being deleted: clear its own symbol. The kit's symbol is shared with other systems'
            // assemblies (their Define Constraints), so it is set to what is actually installed now - never cleared
            // blindly. Also forget an earlier "Not now", so a fresh copy asks again.
            if (ContainsFile(deleted, SetupAsmdefFile))
            {
                SessionState.EraseString(DeclinedKey);
                var symbols = new Dictionary<string, bool> { [OwnDefine] = false };
                var assemblies = FindAssemblyDefinitions();
                foreach (var dependency in Dependencies)
                {
                    symbols[dependency.Define] = assemblies.ContainsKey(dependency.Assembly);
                }

                ApplyDefines(symbols);
                return;
            }

            // Imported again (e.g. a newer copy): ask again too.
            if (ContainsFile(imported, SetupAsmdefFile))
            {
                SessionState.EraseString(DeclinedKey);
            }

            if (ContainsAsmdef(imported) || ContainsAsmdef(deleted) || ContainsAsmdef(moved))
            {
                Refresh(false);
            }
        }

        /// <summary>Updates every symbol; returns true if anything is missing. Offers to install it when <paramref name="prompt"/>.</summary>
        internal static bool Refresh(bool prompt)
        {
            var assemblies = FindAssemblyDefinitions();

            // Easy UI was deleted, but this code is still loaded: Unity keeps the old scripts while the
            // project has compile errors. Its symbols were cleared when it was deleted, so don't set them again.
            if (!assemblies.ContainsKey(Path.GetFileNameWithoutExtension(SetupAsmdefFile)))
            {
                return false;
            }

            var symbols = new Dictionary<string, bool> { [OwnDefine] = true };
            var missing = new List<Dependency>();

            foreach (var dependency in Dependencies)
            {
                var present = assemblies.ContainsKey(dependency.Assembly);
                symbols[dependency.Define] = present;
                if (!present)
                {
                    missing.Add(dependency);
                }
            }

            ApplyDefines(symbols);

            if (missing.Count == 0)
            {
                return false;
            }

            // Asked whenever something is missing, unless "Not now" was already picked for all of it in
            // this editor session (a dependency that goes missing later is asked about again).
            if (prompt && Downloads.Count == 0 && !Application.isBatchMode && !WasDeclined(missing))
            {
                Prompt(missing);
            }

            return true;
        }

        /// <summary>Whether every dependency, optional ones included, is installed.</summary>
        internal static bool AllPresent()
        {
            var assemblies = FindAssemblyDefinitions();
            foreach (var dependency in Dependencies)
            {
                if (!assemblies.ContainsKey(dependency.Assembly))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Every assembly definition in Assets and Packages: file name (the assembly name, by convention) -> path.</summary>
        internal static Dictionary<string, string> FindAssemblyDefinitions()
        {
            var result = new Dictionary<string, string>();
            foreach (var guid in AssetDatabase.FindAssets("t:AssemblyDefinitionAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                result[Path.GetFileNameWithoutExtension(path)] = path;
            }

            return result;
        }

        private static void Prompt(List<Dependency> missing)
        {
            var message = new StringBuilder();
            message.AppendLine("Optional:").AppendLine();
            foreach (var dependency in missing)
            {
                message.Append("• ").Append(dependency.Name).Append(" (into ").Append(dependency.TargetFolder).Append(") - ")
                    .AppendLine(dependency.Purpose);
            }

            message.AppendLine().Append("Until it is installed, ").Append(SystemName)
                .Append(" uses its classic IMGUI window; everything else works the same.");

            if (EditorUtility.DisplayDialog(SystemName, message.ToString(), "Install", "Not now"))
            {
                Install(missing);
            }
            else
            {
                Decline(missing);
            }
        }

        // Not asked about these again in this editor session (see WasDeclined).
        private static void Decline(List<Dependency> dependencies)
        {
            var names = new List<string>();
            foreach (var dependency in dependencies)
            {
                names.Add(dependency.Name);
            }

            SessionState.SetString(DeclinedKey, string.Join("\n", names));
        }

        private static bool WasDeclined(List<Dependency> missing)
        {
            var declined = new HashSet<string>(SessionState.GetString(DeclinedKey, string.Empty).Split('\n'));
            foreach (var dependency in missing)
            {
                if (!declined.Contains(dependency.Name))
                {
                    return false;
                }
            }

            return true;
        }

        private static void Install(List<Dependency> dependencies)
        {
            foreach (var dependency in dependencies)
            {
                StartDownload(dependency);
            }

            EditorApplication.update -= WaitForInstall;
            EditorApplication.update += WaitForInstall;
        }

        // A repository is downloaded as a zip of its main branch and unpacked into its target folder -
        // .meta files included, so the result is identical to copying the repository by hand.
        private static void StartDownload(Dependency dependency)
        {
            var url = dependency.InstallId.TrimEnd('/') + "/archive/refs/heads/" + Branch + ".zip";
            Debug.Log($"[{SystemName}] Downloading {dependency.Name} into {dependency.TargetFolder}");
            var request = UnityWebRequest.Get(url);
            request.SendWebRequest();
            Downloads.Add(new Download { Dependency = dependency, Request = request });
        }

        private static void WaitForInstall()
        {
            var extracted = false;
            for (var i = Downloads.Count - 1; i >= 0; i--)
            {
                var download = Downloads[i];
                if (!download.Request.isDone)
                {
                    continue;
                }

                if (download.Request.result == UnityWebRequest.Result.Success)
                {
                    extracted |= Extract(download.Request.downloadHandler.data, download.Dependency);
                }
                else
                {
                    Debug.LogError($"[{SystemName}] Couldn't download {download.Dependency.Name}: {download.Request.error}\n" +
                                   $"Download {download.Dependency.InstallId} yourself and copy it into {download.Dependency.TargetFolder}.");
                }

                download.Request.Dispose();
                Downloads.RemoveAt(i);
            }

            if (extracted)
            {
                AssetDatabase.Refresh();
            }

            if (Downloads.Count == 0)
            {
                EditorApplication.update -= WaitForInstall;
            }
        }

        // Unpacks the zip's top folder (e.g. "Veneer-main/") into the target folder. Files that already
        // exist are kept, never overwritten; Git's own files (.gitignore, .github/...) are skipped.
        private static bool Extract(byte[] zip, Dependency dependency)
        {
            var written = 0;
            using (var archive = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    var slash = entry.FullName.IndexOf('/');
                    var relative = slash >= 0 ? entry.FullName.Substring(slash + 1) : string.Empty;
                    if (relative.Length == 0 || relative.EndsWith("/") || IsGitFile(relative))
                    {
                        continue;
                    }

                    var path = Path.Combine(dependency.TargetFolder, relative);
                    if (File.Exists(path))
                    {
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    using (var input = entry.Open())
                    using (var output = File.Create(path))
                    {
                        input.CopyTo(output);
                    }

                    written++;
                }
            }

            Debug.Log($"[{SystemName}] {dependency.Name}: {written} file(s) added to {dependency.TargetFolder}.");
            return written > 0;
        }

        private static bool IsGitFile(string relativePath)
        {
            foreach (var part in relativePath.Split('/'))
            {
                if (part.StartsWith(".git"))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ApplyDefines(Dictionary<string, bool> symbols)
        {
            if (symbols.Count == 0)
            {
                return;
            }

            var target = NamedBuildTarget.FromBuildTargetGroup(BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            PlayerSettings.GetScriptingDefineSymbols(target, out var current);

            var defines = new List<string>(current);
            var changed = false;
            foreach (var pair in symbols)
            {
                var has = defines.Contains(pair.Key);
                if (pair.Value && !has)
                {
                    defines.Add(pair.Key);
                    changed = true;
                }
                else if (!pair.Value && has)
                {
                    defines.Remove(pair.Key);
                    changed = true;
                }
            }

            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, defines.ToArray());
            }
        }

        private static bool ContainsFile(string[] paths, string fileName)
        {
            foreach (var path in paths)
            {
                if (Path.GetFileName(path) == fileName)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAsmdef(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".asmdef"))
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>One optional system Easy UI can use.</summary>
    internal readonly struct Dependency
    {
        /// <summary>Shown to the user.</summary>
        public readonly string Name;

        /// <summary>Its assembly definition's name; the dependency counts as installed when it exists.</summary>
        public readonly string Assembly;

        /// <summary>Scripting define symbol set while it is installed.</summary>
        public readonly string Define;

        /// <summary>The GitHub repository URL.</summary>
        public readonly string InstallId;

        /// <summary>The folder the repository is unpacked into.</summary>
        public readonly string TargetFolder;

        /// <summary>What the dependency enables.</summary>
        public readonly string Purpose;

        private Dependency(string name, string assembly, string define, string installId, string targetFolder, string purpose)
        {
            Name = name;
            Assembly = assembly;
            Define = define;
            InstallId = installId;
            TargetFolder = targetFolder;
            Purpose = purpose;
        }

        /// <summary>A GitHub repository, downloaded into <paramref name="targetFolder"/> like a manual copy.</summary>
        public static Dependency Repository(string name, string assembly, string define, string repositoryUrl,
            string targetFolder, string purpose) =>
            new(name, assembly, define, repositoryUrl, targetFolder, purpose);
    }
}
