using System;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// The small window of Role &gt; Add Role...: pick a script and, if you like, a group - typed, or picked from
    /// the groups there are - then Apply makes it a role (see <see cref="EasyUICustomRoles"/>) and gives it to the
    /// element; Cancel closes it. A group typed like an existing one, whatever its case, is that group. It also
    /// lists the roles made so far, each with Remove.
    /// </summary>
    internal sealed class EasyUIAddRoleWindow : EditorWindow
    {
        private const float Width = 360f;
        private const float BaseHeight = 190f;
        private const float RowHeight = 20f;

        private MonoScript _script;
        private string _group = string.Empty;
        private Action<string> _apply;
        private Vector2 _scroll;

        /// <param name="below">Where it opens under, in screen pixels.</param>
        /// <param name="apply">Called with the new role's id when Apply is pressed.</param>
        public static void Open(Rect below, Action<string> apply)
        {
            var window = CreateInstance<EasyUIAddRoleWindow>();
            window.titleContent = new GUIContent("Add Role");
            window._apply = apply;
            var height = BaseHeight + Mathf.Min(EasyUICustomRoles.instance.Roles.Count, 5) * RowHeight;
            window.position = new Rect(below.x, below.yMax + 2f, Width, height);
            window.minSize = new Vector2(Width, height);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Pick a script: it becomes a role any element can have, and is added to the element " +
                                       "when the panel is built.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(4f);

            _script = (MonoScript)EditorGUILayout.ObjectField("Script", _script, typeof(MonoScript), false);
            var usable = _script != null && EasyUICustomRoles.IsUsable(_script.GetClass());
            if (_script != null && !usable)
            {
                EditorGUILayout.HelpBox("It can't be added to an object: its class must be a MonoBehaviour, not abstract, " +
                                        "and named like its file.", MessageType.Warning);
            }

            DrawGroupField();

            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80f)))
                {
                    Close();
                }

                using (new EditorGUI.DisabledScope(!usable))
                {
                    if (GUILayout.Button("Apply", GUILayout.Width(80f)))
                    {
                        var role = EasyUICustomRoles.instance.Add(_script, _group);
                        _apply?.Invoke(role);
                        Close();
                    }
                }
            }

            DrawExistingRoles();
        }

        // The group: typed, or picked from the ones there are; under it, where the role will be listed.
        private void DrawGroupField()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _group = EditorGUILayout.TextField(new GUIContent("Group", "Optional: the Role menu's submenu it goes in. " +
                                                                           "An existing group's name, whatever its case, joins that group."), _group);
                var button = GUILayoutUtility.GetRect(new GUIContent("▾"), EditorStyles.miniButton, GUILayout.Width(22f));
                if (GUI.Button(button, new GUIContent("▾", "Pick an existing group"), EditorStyles.miniButton))
                {
                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("(none)"), string.IsNullOrEmpty(_group), () => SetGroup(string.Empty));
                    foreach (var group in EasyUICustomRoles.ExistingGroups())
                    {
                        var picked = group;
                        menu.AddItem(new GUIContent(group), string.Equals(group, _group.Trim(), StringComparison.OrdinalIgnoreCase),
                            () => SetGroup(picked));
                    }

                    menu.DropDown(button);
                }
            }

            var canonical = EasyUICustomRoles.CanonicalGroup(_group);
            var name = _script != null && _script.GetClass() != null ? ObjectNames.NicifyVariableName(_script.GetClass().Name) : "<script>";
            var where = canonical.Length > 0 ? $"Role > {canonical} > {name}" : $"Role > {name}";
            EditorGUILayout.LabelField("Listed as", where, EditorStyles.miniLabel);
        }

        private void SetGroup(string group)
        {
            _group = group;
            Repaint();
        }

        // The roles made so far, to remove the ones no longer wanted.
        private void DrawExistingRoles()
        {
            var roles = EasyUICustomRoles.instance.Roles;
            if (roles.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Your roles", EditorStyles.boldLabel);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (var i = 0; i < roles.Count; i++)
            {
                var entry = roles[i];
                var script = EasyUICustomRoles.LoadScript(entry.guid);
                var name = script != null ? script.name : $"Missing script ({entry.guid})";
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(string.IsNullOrEmpty(entry.group) ? name : $"{entry.group} / {name}");
                    if (GUILayout.Button("Remove", EditorStyles.miniButton, GUILayout.Width(60f)))
                    {
                        EasyUICustomRoles.instance.Remove(entry.guid);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
