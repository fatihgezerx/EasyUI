using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// What an element is to another system - e.g. an inventory's slot template - so that system can find it in a
    /// built panel without depending on names. Offered by an <see cref="IEasyUIRoleProvider"/>.
    /// </summary>
    public sealed class EasyUIRole
    {
        /// <param name="id">Kept in the panel; never change it once panels use it (e.g. "inventory.slot-template").</param>
        /// <param name="menuPath">Where it is listed in the Role menu, with "/" for submenus (e.g. "Inventory/Slot Template").</param>
        /// <param name="description">Shown under the Role while it is set.</param>
        /// <param name="types">The element types it fits; none for every type.</param>
        public EasyUIRole(string id, string menuPath, string description, params EasyUIElementType[] types)
            : this(id, menuPath, description, true, types)
        {
        }

        /// <param name="unique">
        /// False for a role several elements can have at once, their system telling them apart by where they are
        /// (e.g. an item icon in a slot, in a notification, or on its own).
        /// </param>
        public EasyUIRole(string id, string menuPath, string description, bool unique, params EasyUIElementType[] types)
        {
            Id = id;
            MenuPath = menuPath;
            Description = description;
            Unique = unique;
            Types = types ?? Array.Empty<EasyUIElementType>();
        }

        public string Id { get; }
        public string MenuPath { get; }
        public string Description { get; }
        public IReadOnlyList<EasyUIElementType> Types { get; }

        /// <summary>Whether one element of a panel at most has it; giving it to another then takes it from the first.</summary>
        public bool Unique { get; }

        /// <summary>The last step of <see cref="MenuPath"/> (e.g. "Slot Template").</summary>
        public string Label
        {
            get
            {
                var slash = MenuPath.LastIndexOf('/');
                return slash >= 0 ? MenuPath.Substring(slash + 1) : MenuPath;
            }
        }

        /// <summary>Whether an element of <paramref name="type"/> can have this role.</summary>
        public bool Fits(EasyUIElementType type)
        {
            if (Types.Count == 0)
            {
                return true;
            }

            foreach (var fitting in Types)
            {
                if (fitting == type)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Offers roles to the Easy UI inspector. Any editor class implementing it (with a parameterless constructor)
    /// is found on its own; Easy UI never references the systems that offer roles.
    /// </summary>
    public interface IEasyUIRoleProvider
    {
        IEnumerable<EasyUIRole> GetRoles();
    }

    /// <summary>
    /// Every role the project's <see cref="IEasyUIRoleProvider"/>s offer, found once per script reload. A unique
    /// role is held by one element of a panel at most: giving it to another takes it from the first.
    /// </summary>
    public static class EasyUIRoles
    {
        private static List<EasyUIRole> _all;
        private static Dictionary<string, EasyUIRole> _byId;

        public static IReadOnlyList<EasyUIRole> All
        {
            get
            {
                Collect();
                return _all;
            }
        }

        /// <summary>The role with <paramref name="id"/>, or null (none, or its system is no longer in the project).</summary>
        public static EasyUIRole Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            Collect();
            return _byId.TryGetValue(id, out var role) ? role : null;
        }

        /// <summary>The (first) element of <paramref name="document"/> that has the role <paramref name="id"/>, or null.</summary>
        public static EasyUINode FindNode(EasyUIDocument document, string id)
        {
            foreach (var node in document.nodes)
            {
                if (node.role == id)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>Every element of <paramref name="document"/> that has the role <paramref name="id"/>, into <paramref name="found"/>.</summary>
        public static void FindNodes(EasyUIDocument document, string id, List<EasyUINode> found)
        {
            found.Clear();
            foreach (var node in document.nodes)
            {
                if (node.role == id)
                {
                    found.Add(node);
                }
            }
        }

        /// <summary>Makes the next use find the roles again - e.g. after a role of your own was added or removed.</summary>
        public static void Invalidate()
        {
            _all = null;
            _byId = null;
        }

        private static void Collect()
        {
            if (_all != null)
            {
                return;
            }

            _all = new List<EasyUIRole>();
            _byId = new Dictionary<string, EasyUIRole>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IEasyUIRoleProvider>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                try
                {
                    var provider = (IEasyUIRoleProvider)Activator.CreateInstance(type);
                    foreach (var role in provider.GetRoles())
                    {
                        if (role != null && !string.IsNullOrEmpty(role.Id) && _byId.TryAdd(role.Id, role))
                        {
                            _all.Add(role);
                        }
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }
        }
    }
}
