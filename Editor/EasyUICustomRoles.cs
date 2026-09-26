using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// Roles you make yourself from a script (Role &gt; Add Role...): an element with one gets that script as a
    /// component when the panel is built - so a system of your own can mark its elements without any code for
    /// Easy UI. Each can go in a group, a submenu of the Role menu shared with any system's roles of that name
    /// (e.g. Inventory). Kept per project, in <c>ProjectSettings/EasyUIRoles.asset</c>, so every panel of the
    /// project offers them. They fit every element type, and several elements can share one.
    /// </summary>
    [FilePath("ProjectSettings/EasyUIRoles.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class EasyUICustomRoles : ScriptableSingleton<EasyUICustomRoles>
    {
        private const string Prefix = "script:";

        /// <summary>One role of your own: its script, and the group it is listed under (empty for none).</summary>
        [Serializable]
        internal sealed class Entry
        {
            public string guid;
            public string group = string.Empty;
        }

        [SerializeField] private List<Entry> roles = new();

        // Roles made before groups existed: only their scripts. Moved into `roles` when loaded.
        [SerializeField] private List<string> scriptGuids = new();

        /// <summary>The roles, in the order they were added.</summary>
        public IReadOnlyList<Entry> Roles
        {
            get
            {
                Migrate();
                return roles;
            }
        }

        public static string RoleId(string guid) => Prefix + guid;

        public static bool IsCustom(string roleId) => roleId != null && roleId.StartsWith(Prefix, StringComparison.Ordinal);

        public static MonoScript LoadScript(string guid) =>
            AssetDatabase.LoadAssetAtPath<MonoScript>(AssetDatabase.GUIDToAssetPath(guid));

        /// <summary>Whether a script's class can be added to an element: a concrete MonoBehaviour.</summary>
        public static bool IsUsable(Type type) =>
            type != null && typeof(MonoBehaviour).IsAssignableFrom(type) && !type.IsAbstract && !type.IsGenericTypeDefinition;

        /// <summary>The component a custom role adds, or null (not a custom role, or its script is gone).</summary>
        public static Type ComponentOf(string roleId)
        {
            if (!IsCustom(roleId))
            {
                return null;
            }

            var script = LoadScript(roleId.Substring(Prefix.Length));
            var type = script != null ? script.GetClass() : null;
            return IsUsable(type) ? type : null;
        }

        /// <summary>Every group the Role menu has - the systems' and yours - once each, as first written.</summary>
        public static List<string> ExistingGroups()
        {
            var groups = new List<string>();
            foreach (var role in EasyUIRoles.All)
            {
                var slash = role.MenuPath.IndexOf('/');
                if (slash > 0)
                {
                    AddGroup(groups, role.MenuPath.Substring(0, slash));
                }
            }

            foreach (var entry in instance.Roles)
            {
                AddGroup(groups, entry.group);
            }

            return groups;
        }

        /// <summary>
        /// <paramref name="group"/> as it is to be kept: trimmed, without slashes (they would make submenus), and
        /// written like an existing group of the same name, whatever its case - so there is never a second one.
        /// </summary>
        public static string CanonicalGroup(string group)
        {
            group = (group ?? string.Empty).Replace('/', ' ').Trim();
            if (group.Length == 0)
            {
                return string.Empty;
            }

            foreach (var existing in ExistingGroups())
            {
                if (string.Equals(existing, group, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }

            return group;
        }

        /// <summary>Makes <paramref name="script"/> a role in <paramref name="group"/> (moving it there if it is one) and returns its id.</summary>
        public string Add(MonoScript script, string group)
        {
            var guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(script));
            group = CanonicalGroup(group);

            Migrate();
            var entry = roles.Find(role => role.guid == guid);
            if (entry == null)
            {
                roles.Add(new Entry { guid = guid, group = group });
            }
            else
            {
                entry.group = group;
            }

            Save(true);
            EasyUIRoles.Invalidate();
            return RoleId(guid);
        }

        /// <summary>Removes the role of the script with <paramref name="guid"/>. Elements that had it show it as missing.</summary>
        public void Remove(string guid)
        {
            Migrate();
            if (roles.RemoveAll(role => role.guid == guid) > 0)
            {
                Save(true);
                EasyUIRoles.Invalidate();
            }
        }

        private void Migrate()
        {
            if (scriptGuids.Count == 0)
            {
                return;
            }

            foreach (var guid in scriptGuids)
            {
                if (roles.Find(role => role.guid == guid) == null)
                {
                    roles.Add(new Entry { guid = guid });
                }
            }

            scriptGuids.Clear();
            Save(true);
        }

        private static void AddGroup(List<string> groups, string group)
        {
            if (string.IsNullOrEmpty(group))
            {
                return;
            }

            foreach (var existing in groups)
            {
                if (string.Equals(existing, group, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            groups.Add(group);
        }
    }

    // Lists the custom roles in the Role menu, named after their scripts' classes, in their groups.
    internal sealed class EasyUICustomRoleProvider : IEasyUIRoleProvider
    {
        public IEnumerable<EasyUIRole> GetRoles()
        {
            foreach (var entry in EasyUICustomRoles.instance.Roles)
            {
                var script = EasyUICustomRoles.LoadScript(entry.guid);
                var type = script != null ? script.GetClass() : null;
                if (!EasyUICustomRoles.IsUsable(type))
                {
                    continue;
                }

                var name = ObjectNames.NicifyVariableName(type.Name);
                var path = string.IsNullOrEmpty(entry.group) ? name : entry.group + "/" + name;
                yield return new EasyUIRole(EasyUICustomRoles.RoleId(entry.guid), path,
                    $"Your role: a {type.Name} is added to this element when the panel is built.", false);
            }
        }
    }
}
