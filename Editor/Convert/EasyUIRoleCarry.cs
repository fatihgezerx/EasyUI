using System.Collections.Generic;

namespace EasyUI
{
    /// <summary>
    /// Keeps the roles of a panel through a new conversion. A role without a component of its own (an inventory's slot
    /// template, a dialogue's line...) cannot be read back from the scene, so when a panel is converted into an existing
    /// panel the roles of its old design are given back to the elements of the new one that have the same place and
    /// name.
    /// </summary>
    internal static class EasyUIRoleCarry
    {
        /// <summary>
        /// Adds the roles of <paramref name="from"/>'s elements to the elements of <paramref name="to"/> with the same
        /// name under the same parents (the root counts as the same whatever it is called; of several siblings with one
        /// name, the first matches the first...). A role the element already has (e.g. read from its component) is not
        /// added twice, and a unique role that another element of <paramref name="to"/> already holds is left there.
        /// Roles that found no element, and roles that could not be kept, are listed in <paramref name="notes"/>.
        /// Returns how many roles were given back.
        /// </summary>
        public static int Carry(EasyUIDocument from, EasyUIDocument to, List<string> notes)
        {
            if (from == null || to == null || from.RootNode == null || to.RootNode == null)
            {
                return 0;
            }

            var target = new Dictionary<string, EasyUINode>();
            Index(to, target);

            var source = new Dictionary<string, EasyUINode>();
            Index(from, source);

            var carried = 0;
            var lost = new List<string>();
            foreach (var pair in source)
            {
                var old = pair.Value;
                if (old.roles.Count == 0)
                {
                    continue;
                }

                if (!target.TryGetValue(pair.Key, out var node))
                {
                    lost.Add(Describe(pair.Key, old.roles));
                    continue;
                }

                foreach (var roleId in old.roles)
                {
                    if (node.HasRole(roleId))
                    {
                        continue;
                    }

                    var role = EasyUIRoles.Find(roleId);
                    if (role != null && role.Unique && EasyUIRoles.FindNode(to, roleId) != null)
                    {
                        continue;
                    }

                    node.roles.Add(roleId);
                    carried++;
                }
            }

            if (lost.Count > 0)
            {
                notes.Add("Roles of the old design that found no element with the same name and place: " + string.Join("; ", lost));
            }

            return carried;
        }

        // Every element under its place: the names of its parents from the root down, each with how many same-named
        // siblings came before it ("Content/Row#0", "Content/Row#1").
        private static void Index(EasyUIDocument document, Dictionary<string, EasyUINode> keys)
        {
            var children = new Dictionary<int, List<EasyUINode>>();
            foreach (var node in document.nodes)
            {
                if (!children.TryGetValue(node.parentId, out var list))
                {
                    list = new List<EasyUINode>();
                    children.Add(node.parentId, list);
                }

                list.Add(node);
            }

            var root = document.RootNode;
            keys[string.Empty] = root;
            Walk(root, string.Empty, children, keys);
        }

        private static void Walk(EasyUINode parent, string path, Dictionary<int, List<EasyUINode>> children, Dictionary<string, EasyUINode> keys)
        {
            if (!children.TryGetValue(parent.id, out var list))
            {
                return;
            }

            var seen = new Dictionary<string, int>();
            foreach (var child in list)
            {
                seen.TryGetValue(child.name, out var before);
                seen[child.name] = before + 1;

                var key = path + "/" + child.name + "#" + before;
                keys[key] = child;
                Walk(child, key, children, keys);
            }
        }

        private static string Describe(string key, List<string> roles) =>
            (key.Length == 0 ? "(root)" : key.Substring(1)) + " [" + string.Join(", ", roles) + "]";
    }
}
