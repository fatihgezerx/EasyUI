using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// Told whenever a panel is built into the scene from <c>GameObject &gt; UI (Canvas) &gt; Easy UI</c>, after
    /// every element was made and every role's <see cref="EasyUIRole.Component"/> added: the place for a system to
    /// set its roles' objects up - add its views, wire them to each other. Any editor class implementing it (with a
    /// parameterless constructor) is found on its own; Easy UI never references the systems that handle builds.
    /// </summary>
    public interface IEasyUIBuildHandler
    {
        /// <summary>Lower goes first; handlers of the same order in no particular order. 0 is fine for most.</summary>
        int Order { get; }

        /// <summary>
        /// Sets up the objects of <paramref name="build"/>. Components added with <c>AddComponent</c> go with the
        /// objects just built; anything done to other objects (e.g. the canvas) must be recorded for undo.
        /// </summary>
        void OnBuilt(EasyUIBuild build);
    }

    /// <summary>A panel just built into the scene: its design, and the object each of its elements became.</summary>
    public sealed class EasyUIBuild
    {
        private readonly Dictionary<int, GameObject> _objects;
        private readonly List<EasyUINode> _nodes = new();
        private readonly List<GameObject> _movedOut = new();

        internal EasyUIBuild(EasyUIPanel panel, EasyUIDocument document, GameObject root, Dictionary<int, GameObject> objects)
        {
            Panel = panel;
            Document = document;
            Root = root;
            _objects = objects;
            Canvas = root.GetComponentInParent<Canvas>(true)?.rootCanvas;
        }

        /// <summary>The panel asset it was built from.</summary>
        public EasyUIPanel Panel { get; }

        /// <summary>The design as built (brought up to date, with its parts).</summary>
        public EasyUIDocument Document { get; }

        /// <summary>The root element's object: the window.</summary>
        public GameObject Root { get; }

        /// <summary>The root canvas it was built into.</summary>
        public Canvas Canvas { get; }

        /// <summary>Whether any element has the role <paramref name="roleId"/>.</summary>
        public bool Has(string roleId) => EasyUIRoles.FindNode(Document, roleId) != null;

        /// <summary>The object <paramref name="node"/> became, or null (a part Unity didn't make).</summary>
        public GameObject Get(EasyUINode node) => node != null && _objects.TryGetValue(node.id, out var go) ? go : null;

        /// <summary>The object of the (first) element with the role <paramref name="roleId"/>, or null.</summary>
        public GameObject Find(string roleId) => Get(EasyUIRoles.FindNode(Document, roleId));

        /// <summary>The objects of every element with the role <paramref name="roleId"/>, into <paramref name="found"/>.</summary>
        public void FindAll(string roleId, List<GameObject> found)
        {
            found.Clear();
            EasyUIRoles.FindNodes(Document, roleId, _nodes);
            foreach (var node in _nodes)
            {
                var go = Get(node);
                if (go != null)
                {
                    found.Add(go);
                }
            }
        }

        /// <summary>The (first) object with the role <paramref name="roleId"/> that is <paramref name="root"/> or inside it, or null.</summary>
        public GameObject FindInside(GameObject root, string roleId)
        {
            if (root == null)
            {
                return null;
            }

            EasyUIRoles.FindNodes(Document, roleId, _nodes);
            foreach (var node in _nodes)
            {
                var go = Get(node);
                if (go != null && go.transform.IsChildOf(root.transform))
                {
                    return go;
                }
            }

            return null;
        }

        /// <summary>
        /// Takes <paramref name="go"/> - built in the panel - out of it to <paramref name="parent"/>, where it
        /// stays where it is on screen: e.g. notifications that must show while the window is closed. It is then
        /// undone with the panel.
        /// </summary>
        public void MoveOut(GameObject go, Transform parent)
        {
            go.transform.SetParent(parent, true);
            _movedOut.Add(go);
        }

        internal IReadOnlyList<GameObject> MovedOut => _movedOut;
    }

    // Finds the IEasyUIBuildHandlers once per script reload and runs them, in order, on every build.
    internal static class EasyUIBuildHandlers
    {
        private static List<IEasyUIBuildHandler> _all;

        public static void Run(EasyUIBuild build)
        {
            Collect();
            foreach (var handler in _all)
            {
                try
                {
                    handler.OnBuilt(build);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception, build.Root);
                }
            }
        }

        private static void Collect()
        {
            if (_all != null)
            {
                return;
            }

            _all = new List<IEasyUIBuildHandler>();
            foreach (var type in TypeCache.GetTypesDerivedFrom<IEasyUIBuildHandler>())
            {
                if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                {
                    continue;
                }

                try
                {
                    _all.Add((IEasyUIBuildHandler)Activator.CreateInstance(type));
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            }

            _all.Sort((a, b) => a.Order.CompareTo(b.Order));
        }
    }
}
