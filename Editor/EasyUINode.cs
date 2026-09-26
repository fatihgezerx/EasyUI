using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

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

        public Rect Rect => new(position, size);

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
        public string panelName = "New Panel";

        // The canvas's size when the panel was last saved: the space element positions are measured in, so a
        // build can anchor them to a parent of any size. Zero for panels saved before it was kept.
        public Vector2 canvasSize;

        public List<EasyUINode> nodes = new();
        public int nextId = 1;
        public int editCounter;

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

        /// <summary>A deep copy, through Unity's editor serializer, which keeps references to sprites, fonts and the like.</summary>
        public EasyUIDocument Clone()
        {
            var copy = new EasyUIDocument();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(this), copy);
            return copy;
        }
    }
}
