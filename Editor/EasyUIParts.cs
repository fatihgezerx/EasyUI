using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasyUI
{
    /// <summary>
    /// The built-in pieces of the composite elements, kept as elements of their own so each one can be renamed,
    /// styled, marked with a role and given components and children:
    /// <list type="bullet">
    /// <item>Scroll View: Viewport &gt; Content, Scrollbar Horizontal &gt; Handle, Scrollbar Vertical &gt; Handle</item>
    /// <item>Button: Text</item>
    /// <item>Toggle: Background &gt; Checkmark, Label</item>
    /// <item>Dropdown: Label, Arrow</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// Parts are made with their element, laid out as Unity's <c>GameObject &gt; UI (Canvas)</c> menu lays them out,
    /// and go with it: they can't be deleted, duplicated or dragged on their own, and the layer rules don't apply
    /// to them (a scrollbar sits over its Viewport, a Content may be taller than it). Their Rect Transform is still
    /// edited in the inspector - except a scrollbar's Handle, which its Scrollbar sizes. A Scroll View's
    /// scrollbars exist while they are ticked in its settings. Panels saved before parts existed get theirs the
    /// first time they are opened or built.
    /// </remarks>
    public static class EasyUIParts
    {
        private const string SkinPath = "UI/Skin/";
        private const float ThinSize = 20f;

        // The label color of Unity's default controls.
        private static readonly Color LabelColor = new(50f / 255f, 50f / 255f, 50f / 255f, 1f);

        /// <summary>Whether elements of <paramref name="type"/> are made with parts.</summary>
        public static bool IsComposite(EasyUIElementType type) =>
            type is EasyUIElementType.ScrollView or EasyUIElementType.Button or EasyUIElementType.Toggle or EasyUIElementType.Dropdown;

        /// <summary>What the part is, for the inspector (e.g. "Horizontal Scrollbar").</summary>
        public static string DisplayName(EasyUIPart part) => part switch
        {
            EasyUIPart.ScrollbarHorizontal => "Horizontal Scrollbar",
            EasyUIPart.ScrollbarVertical => "Vertical Scrollbar",
            EasyUIPart.ScrollbarHandle => "Handle",
            EasyUIPart.ButtonText => "Text",
            EasyUIPart.ToggleBackground => "Background",
            EasyUIPart.ToggleCheckmark => "Checkmark",
            EasyUIPart.ToggleLabel or EasyUIPart.DropdownLabel => "Label",
            EasyUIPart.DropdownArrow => "Arrow",
            _ => part.ToString()
        };

        /// <summary>Whether uGUI sizes the part itself, so its Rect Transform can't be set.</summary>
        public static bool IsDriven(EasyUIPart part) => part == EasyUIPart.ScrollbarHandle;

        /// <summary>The composite element <paramref name="node"/> is a part of (itself when it isn't a part).</summary>
        public static EasyUINode OwnerOf(EasyUIDocument document, EasyUINode node)
        {
            var current = node;
            for (var guard = 0; current != null && current.IsPart && guard < 64; guard++)
            {
                current = document.Find(current.parentId);
            }

            return current ?? node;
        }

        /// <summary>Where elements added under <paramref name="node"/> go: a Scroll View's Content, otherwise the element itself.</summary>
        public static EasyUINode ChildParent(EasyUIDocument document, EasyUINode node)
        {
            if (node == null || node.type != EasyUIElementType.ScrollView || node.IsPart)
            {
                return node;
            }

            var viewport = FindPart(document, node, EasyUIPart.Viewport);
            var content = viewport != null ? FindPart(document, viewport, EasyUIPart.Content) : null;
            return content ?? node;
        }

        /// <summary>The part of kind <paramref name="part"/> directly under <paramref name="parent"/>, or null.</summary>
        public static EasyUINode FindPart(EasyUIDocument document, EasyUINode parent, EasyUIPart part)
        {
            foreach (var node in document.nodes)
            {
                if (node.parentId == parent.id && node.part == part)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Gives every composite element the parts it is missing and removes the scrollbars it no longer has.
        /// Returns whether anything changed.
        /// </summary>
        public static bool EnsureParts(EasyUIDocument document)
        {
            var changed = false;
            var owners = document.nodes.FindAll(node => !node.IsPart && IsComposite(node.type));
            foreach (var owner in owners)
            {
                changed |= EnsureParts(document, owner);
            }

            return changed;
        }

        /// <summary>Gives <paramref name="owner"/> the parts it is missing; see <see cref="EnsureParts(EasyUIDocument)"/>.</summary>
        public static bool EnsureParts(EasyUIDocument document, EasyUINode owner)
        {
            var changed = false;
            switch (owner.type)
            {
                case EasyUIElementType.ScrollView:
                {
                    var viewport = Ensure(document, owner, EasyUIPart.Viewport, owner, ref changed);
                    var hadContent = FindPart(document, viewport, EasyUIPart.Content) != null;
                    var content = Ensure(document, viewport, EasyUIPart.Content, owner, ref changed);
                    if (!hadContent)
                    {
                        MoveIntoContent(document, owner, content);
                    }

                    changed |= EnsureScrollbar(document, owner, EasyUIPart.ScrollbarHorizontal, owner.scrollView.horizontalScrollbar);
                    changed |= EnsureScrollbar(document, owner, EasyUIPart.ScrollbarVertical, owner.scrollView.verticalScrollbar);
                    break;
                }

                case EasyUIElementType.Button:
                    Ensure(document, owner, EasyUIPart.ButtonText, owner, ref changed);
                    break;

                case EasyUIElementType.Toggle:
                {
                    var background = Ensure(document, owner, EasyUIPart.ToggleBackground, owner, ref changed);
                    Ensure(document, background, EasyUIPart.ToggleCheckmark, owner, ref changed);
                    Ensure(document, owner, EasyUIPart.ToggleLabel, owner, ref changed);
                    break;
                }

                case EasyUIElementType.Dropdown:
                    Ensure(document, owner, EasyUIPart.DropdownLabel, owner, ref changed);
                    Ensure(document, owner, EasyUIPart.DropdownArrow, owner, ref changed);
                    break;
            }

            return changed;
        }

        private static EasyUINode Ensure(EasyUIDocument document, EasyUINode parent, EasyUIPart part, EasyUINode owner, ref bool changed)
        {
            var node = FindPart(document, parent, part);
            if (node != null)
            {
                return node;
            }

            changed = true;
            return NewPart(document, parent, part, owner);
        }

        // A ticked scrollbar gets its part (and Handle); an unticked one loses it, with everything under it.
        private static bool EnsureScrollbar(EasyUIDocument document, EasyUINode owner, EasyUIPart part, bool wanted)
        {
            var scrollbar = FindPart(document, owner, part);
            if (!wanted)
            {
                if (scrollbar == null)
                {
                    return false;
                }

                document.nodes.RemoveAll(node => document.IsUnder(node, scrollbar));
                return true;
            }

            var changed = false;
            scrollbar ??= Ensure(document, owner, part, owner, ref changed);
            Ensure(document, scrollbar, EasyUIPart.ScrollbarHandle, owner, ref changed);
            return changed;
        }

        // A Scroll View saved before it had parts: what was under it goes into its Content, and so do its Content
        // Size Fitter and Layout Group, which Easy UI used to build there.
        private static void MoveIntoContent(EasyUIDocument document, EasyUINode owner, EasyUINode content)
        {
            foreach (var node in document.nodes)
            {
                if (node.parentId == owner.id && !node.IsPart)
                {
                    node.parentId = content.id;
                }
            }

            if (owner.contentSizeFitter.enabled)
            {
                content.contentSizeFitter = owner.contentSizeFitter;
                owner.contentSizeFitter = new ContentSizeFitterSettings();
            }

            if (owner.layoutGroup.enabled)
            {
                content.layoutGroup = owner.layoutGroup;
                owner.layoutGroup = new LayoutGroupSettings();
            }
        }

        // A part laid out in `parent` as Unity lays it out, looking as Unity makes it.
        private static EasyUINode NewPart(EasyUIDocument document, EasyUINode parent, EasyUIPart part, EasyUINode owner)
        {
            var r = parent.Rect;
            var node = new EasyUINode
            {
                id = document.nextId++,
                parentId = parent.id,
                part = part,
                name = DefaultName(part)
            };

            switch (part)
            {
                case EasyUIPart.Viewport:
                    Layout(node, EasyUIElementType.Image, r, HorizontalAnchor.Stretch, VerticalAnchor.Stretch, new Vector2(0f, 1f));
                    SetImage(node, "UIMask", Image.Type.Sliced);
                    node.mask.enabled = true;
                    node.mask.showMaskGraphic = false;
                    break;

                case EasyUIPart.Content:
                    // As tall as the view, so what is designed in the view lands where it was drawn.
                    Layout(node, EasyUIElementType.EmptyObject, r, HorizontalAnchor.Stretch, VerticalAnchor.Top, new Vector2(0f, 1f));
                    break;

                case EasyUIPart.ScrollbarHorizontal:
                    Layout(node, EasyUIElementType.Image, new Rect(r.x, r.yMax - ThinSize, r.width, ThinSize),
                        HorizontalAnchor.Stretch, VerticalAnchor.Bottom, Vector2.zero);
                    SetImage(node, "Background", Image.Type.Sliced);
                    break;

                case EasyUIPart.ScrollbarVertical:
                    Layout(node, EasyUIElementType.Image, new Rect(r.xMax - ThinSize, r.y, ThinSize, r.height),
                        HorizontalAnchor.Right, VerticalAnchor.Stretch, Vector2.one);
                    SetImage(node, "Background", Image.Type.Sliced);
                    break;

                case EasyUIPart.ScrollbarHandle:
                    Layout(node, EasyUIElementType.Image, r, HorizontalAnchor.Stretch, VerticalAnchor.Stretch, new Vector2(0.5f, 0.5f));
                    SetImage(node, "UISprite", Image.Type.Sliced);
                    break;

                case EasyUIPart.ButtonText:
                    Layout(node, EasyUIElementType.Text, r, HorizontalAnchor.Stretch, VerticalAnchor.Stretch, new Vector2(0.5f, 0.5f));
                    SetText(node, string.IsNullOrEmpty(owner.button.label) ? "Button" : owner.button.label, 24f, TextAlignmentOptions.Center);
                    break;

                case EasyUIPart.ToggleBackground:
                    Layout(node, EasyUIElementType.Image, new Rect(r.x, r.y, ThinSize, ThinSize),
                        HorizontalAnchor.Left, VerticalAnchor.Top, new Vector2(0.5f, 0.5f));
                    SetImage(node, "UISprite", Image.Type.Sliced);
                    break;

                case EasyUIPart.ToggleCheckmark:
                    Layout(node, EasyUIElementType.Image, r, HorizontalAnchor.Center, VerticalAnchor.Middle, new Vector2(0.5f, 0.5f));
                    SetImage(node, "Checkmark", Image.Type.Simple);
                    break;

                case EasyUIPart.ToggleLabel:
                    Layout(node, EasyUIElementType.Text, Inset(r, 23f, 2f, 5f, 1f),
                        HorizontalAnchor.Stretch, VerticalAnchor.Stretch, new Vector2(0.5f, 0.5f));
                    SetText(node, string.IsNullOrEmpty(owner.toggle.label) ? "Toggle" : owner.toggle.label, 14f, TextAlignmentOptions.TopLeft);
                    break;

                case EasyUIPart.DropdownLabel:
                    Layout(node, EasyUIElementType.Text, Inset(r, 10f, 7f, 25f, 6f),
                        HorizontalAnchor.Stretch, VerticalAnchor.Stretch, new Vector2(0.5f, 0.5f));
                    var options = owner.dropdown.options;
                    SetText(node, options.Count > 0 ? options[Mathf.Clamp(owner.dropdown.value, 0, options.Count - 1)] : "Option A",
                        14f, TextAlignmentOptions.Left);
                    break;

                case EasyUIPart.DropdownArrow:
                    Layout(node, EasyUIElementType.Image, new Rect(r.xMax - 25f, r.center.y - ThinSize * 0.5f, ThinSize, ThinSize),
                        HorizontalAnchor.Right, VerticalAnchor.Middle, new Vector2(0.5f, 0.5f));
                    SetImage(node, "DropdownArrow", Image.Type.Simple);
                    break;
            }

            node.editStamp = ++document.editCounter;
            document.nodes.Add(node);
            return node;
        }

        /// <summary>The name a part gets, as Unity names that object.</summary>
        public static string DefaultName(EasyUIPart part) => part switch
        {
            EasyUIPart.ScrollbarHorizontal => "Scrollbar Horizontal",
            EasyUIPart.ScrollbarVertical => "Scrollbar Vertical",
            EasyUIPart.ScrollbarHandle => "Handle",
            EasyUIPart.ButtonText => "Text (TMP)",
            EasyUIPart.ToggleBackground => "Background",
            EasyUIPart.ToggleCheckmark => "Checkmark",
            EasyUIPart.ToggleLabel or EasyUIPart.DropdownLabel => "Label",
            EasyUIPart.DropdownArrow => "Arrow",
            _ => part.ToString()
        };

        private static void Layout(EasyUINode node, EasyUIElementType type, Rect rect, HorizontalAnchor anchorH, VerticalAnchor anchorV, Vector2 pivot)
        {
            node.type = type;
            node.position = rect.position;
            node.size = Vector2.Max(rect.size, Vector2.one * 8f);
            node.anchorH = anchorH;
            node.anchorV = anchorV;
            node.pivot = pivot;
        }

        // `rect` moved in by the given distances from its left, top, right and bottom edges.
        private static Rect Inset(Rect rect, float left, float top, float right, float bottom) =>
            Rect.MinMaxRect(rect.xMin + left, rect.yMin + top, rect.xMax - right, rect.yMax - bottom);

        private static void SetImage(EasyUINode node, string sprite, Image.Type type)
        {
            node.image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(SkinPath + sprite + ".psd");
            node.image.imageType = type;
        }

        private static void SetText(EasyUINode node, string text, float size, TextAlignmentOptions alignment)
        {
            node.text.text = text;
            node.text.fontSize = size;
            node.text.color = LabelColor;
            node.text.alignment = alignment;
        }
    }
}
