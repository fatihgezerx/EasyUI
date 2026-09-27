using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace EasyUI
{
    /// <summary>
    /// One element of a panel: what it is, its parent (0: the panel itself), its name, where it sits and how
    /// big it is (canvas units, top-left corner, y down), how its RectTransform is anchored and pivoted in its
    /// parent, the settings of the components its kind is built with, and the components added to it.
    /// </summary>
    [Serializable]
    public sealed class EasyUINode
    {
        public int id;
        public int parentId;
        public string name = string.Empty;
        public EasyUIElementType type;

        // A built-in piece of its composite parent (Viewport, Content, a Toggle's Label...), made and kept by
        // EasyUIParts; None for an element added by hand.
        public EasyUIPart part;

        // What the element is to other systems (e.g. an inventory's slot template), from EasyUIRoles: any number of
        // role ids, in the order they were given.
        public List<string> roles = new();

        // The one role an element had before it could have several; moved into `roles` by EasyUIDocument.Upgrade.
        [SerializeField] private string role = string.Empty;

        public Vector2 position;
        public Vector2 size;
        public Vector2 pivot = new(0.5f, 0.5f);
        public HorizontalAnchor anchorH = HorizontalAnchor.Center;
        public VerticalAnchor anchorV = VerticalAnchor.Middle;

        // When the element was last changed, so of two overlapping elements the one changed last can be told apart.
        public int editStamp;

        public bool rectTransformOpen = true;

        // The components of its kind; each kind uses only its own (see EasyUIWindow.Elements.cs).
        public TextSettings text = new();
        public ImageSettings image = new();
        public RawImageSettings rawImage = new();
        public SelectableSettings selectable = new();
        public ButtonSettings button = new();
        public ToggleSettings toggle = new();
        public SliderSettings slider = new();
        public DropdownSettings dropdown = new();
        public InputFieldSettings inputField = new();
        public ScrollViewSettings scrollView = new();

        // Components that can be added to any kind.
        public ContentSizeFitterSettings contentSizeFitter = new();
        public CanvasGroupSettings canvasGroup = new();
        public LayoutGroupSettings layoutGroup = new();
        public LayoutElementSettings layoutElement = new();
        public MaskSettings mask = new();
        public RectMask2DSettings rectMask2D = new();

        public Rect Rect => new(position, size);

        public bool IsPart => part != EasyUIPart.None;

        /// <summary>Whether the element has the role <paramref name="id"/>.</summary>
        public bool HasRole(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return false;
            }

            foreach (var held in roles)
            {
                if (held == id)
                {
                    return true;
                }
            }

            return false;
        }

        // Moves the single role of an element saved before it could have several into `roles`.
        internal void UpgradeRole()
        {
            if (string.IsNullOrEmpty(role))
            {
                return;
            }

            if (!HasRole(role))
            {
                roles.Add(role);
            }

            role = string.Empty;
        }

        /// <summary>uGUI's default size for a new <paramref name="type"/>, as its GameObject > UI menu makes it.</summary>
        public static Vector2 DefaultSize(EasyUIElementType type) => type switch
        {
            EasyUIElementType.Text => new Vector2(200f, 50f),
            EasyUIElementType.Button => new Vector2(160f, 30f),
            EasyUIElementType.Toggle => new Vector2(160f, 20f),
            EasyUIElementType.Slider => new Vector2(160f, 20f),
            EasyUIElementType.Dropdown => new Vector2(160f, 30f),
            EasyUIElementType.InputField => new Vector2(160f, 30f),
            EasyUIElementType.ScrollView => new Vector2(200f, 200f),
            _ => new Vector2(100f, 100f)
        };

        // Every text in Easy UI is TextMeshPro, so the names don't say so.
        public static string DisplayName(EasyUIElementType type) => type switch
        {
            EasyUIElementType.EmptyObject => "Empty",
            EasyUIElementType.RawImage => "Raw Image",
            EasyUIElementType.InputField => "Input Field",
            EasyUIElementType.ScrollView => "Scroll View",
            _ => type.ToString()
        };

        /// <summary>The name used when none is typed.</summary>
        public static string DefaultName(EasyUIElementType type) => type switch
        {
            EasyUIElementType.EmptyObject => "GameObject",
            EasyUIElementType.RawImage => "RawImage",
            EasyUIElementType.InputField => "InputField",
            EasyUIElementType.ScrollView => "Scroll View",
            _ => type.ToString()
        };
    }

    /// <summary>
    /// A panel being designed: its name and every element in it, as a flat list where each one names its parent.
    /// Held by the <see cref="EasyUIWindow"/> while it is edited, and by an <see cref="EasyUIPanel"/> once saved.
    /// </summary>
    [Serializable]
    public sealed class EasyUIDocument
    {
        // For a design saved before the canvas's size was kept, when there is no canvas to measure either.
        internal static readonly Vector2 FallbackCanvasSize = new(1920f, 1080f);

        public string panelName = "New Panel";

        // The canvas's size the elements' positions are measured in - the canvas's as it was last drawn in the
        // editor (which moves the root along when it changes) - so a build can anchor them to a parent of any
        // size. Zero for panels saved before it was kept.
        public Vector2 canvasSize;

        // The root element: an Empty holding every other element, which is the window itself - what gets built.
        // Without a name of its own, it is called after the panel.
        [FormerlySerializedAs("popupId")] public int rootId;

        public List<EasyUINode> nodes = new();
        public int nextId = 1;
        public int editCounter;

        /// <summary>The root element: the window, which holds every other element. Null only before <see cref="Upgrade"/>.</summary>
        public EasyUINode RootNode => Find(rootId);

        /// <summary>Whether <paramref name="node"/> is the root element, which stays: it can't be deleted or copied.</summary>
        public bool IsRootNode(EasyUINode node) => node != null && rootId != 0 && node.id == rootId;

        public EasyUINode Find(int id)
        {
            if (id == 0)
            {
                return null;
            }

            foreach (var node in nodes)
            {
                if (node.id == id)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>How deep the element sits: 1 for the panel's own children, 2 for theirs... Its layer.</summary>
        public int DepthOf(EasyUINode node)
        {
            var depth = 1;
            var parent = Find(node.parentId);
            while (parent != null && depth < 64)
            {
                depth++;
                parent = Find(parent.parentId);
            }

            return depth;
        }

        /// <summary>Whether <paramref name="node"/> is <paramref name="ancestor"/> or somewhere under it.</summary>
        public bool IsUnder(EasyUINode node, EasyUINode ancestor)
        {
            var current = node;
            for (var guard = 0; current != null && guard < 64; guard++)
            {
                if (current == ancestor)
                {
                    return true;
                }

                current = Find(current.parentId);
            }

            return false;
        }

        /// <summary>
        /// Brings a design saved by an older Easy UI up to date: an element's single role goes into its roles, and a
        /// design without a root element - a Panel of the time, whose elements were straight on the canvas - gets
        /// one stretched over the whole canvas (<paramref name="canvas"/> units), unnamed so it is called after the
        /// panel, with those elements put in it. A

        /// Popup of the time keeps its Popup element as the root. Returns whether anything changed.
        /// </summary>
        public bool Upgrade(Vector2 canvas)
        {
            var changed = false;
            foreach (var node in nodes)
            {
                if (node.roles == null)
                {
                    node.roles = new List<string>();
                    changed = true;
                }

                var count = node.roles.Count;
                node.UpgradeRole();
                changed |= node.roles.Count != count;
            }

            if (RootNode != null)
            {
                return changed;
            }

            var root = new EasyUINode
            {
                id = nextId++,
                type = EasyUIElementType.EmptyObject,

                position = Vector2.zero,
                size = canvas,
                anchorH = HorizontalAnchor.Stretch,
                anchorV = VerticalAnchor.Stretch,
                editStamp = ++editCounter
            };

            foreach (var node in nodes)
            {
                if (Find(node.parentId) == null)
                {
                    node.parentId = root.id;
                }
            }

            // First, so it is drawn under - and built before - everything in it.
            nodes.Insert(0, root);
            rootId = root.id;
            return true;
        }

        /// <summary>A deep copy, through Unity's editor serializer, which keeps references to sprites, fonts and the like.</summary>
        public EasyUIDocument Clone()
        {
            var copy = new EasyUIDocument();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(this), copy);
            return copy;
        }
    }
}
