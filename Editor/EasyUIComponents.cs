using System;
using UnityEngine;

namespace EasyUI
{
    /// <summary>What an element is: the uGUI object it becomes.</summary>
    public enum EasyUIElementType
    {
        EmptyObject,
        Text,
        Image,
        RawImage,
        Button,
        Toggle,
        Slider,
        Dropdown,
        InputField,
        ScrollView
    }

    /// <summary>
    /// Which built-in piece of a Scroll View, Button, Toggle or Dropdown an element is (see EasyUIParts.cs), or
    /// <see cref="None"/> for an element of its own.
    /// </summary>
    public enum EasyUIPart
    {
        None,
        Viewport,
        Content,
        ScrollbarHorizontal,
        ScrollbarVertical,
        ScrollbarHandle,
        ButtonText,
        ToggleBackground,
        ToggleCheckmark,
        ToggleLabel,
        DropdownLabel,
        DropdownArrow
    }

    /// <summary>Where a RectTransform is anchored horizontally in its parent, like the columns of Unity's Anchor Presets.</summary>
    public enum HorizontalAnchor
    {
        Left,
        Center,
        Right,
        Stretch
    }

    /// <summary>Where a RectTransform is anchored vertically in its parent, like the rows of Unity's Anchor Presets.</summary>
    public enum VerticalAnchor
    {
        Top,
        Middle,
        Bottom,
        Stretch
    }

    /// <summary>Which uGUI layout group an element's Layout Group component is.</summary>
    public enum LayoutKind
    {
        Horizontal,
        Vertical,
        Grid
    }

    // Mirrors of uGUI's enums, in the same order, so this editor needs no reference to uGUI.
    public enum GridStartCorner
    {
        UpperLeft,
        UpperRight,
        LowerLeft,
        LowerRight
    }

    public enum GridStartAxis
    {
        Horizontal,
        Vertical
    }

    public enum GridConstraint
    {
        Flexible,
        FixedColumnCount,
        FixedRowCount
    }

    public enum FitMode
    {
        Unconstrained,
        MinSize,
        PreferredSize
    }

    public enum ScrollMovementType
    {
        Unrestricted,
        Elastic,
        Clamped
    }

    public enum ScrollbarVisibility
    {
        Permanent,
        AutoHide,
        AutoHideAndExpandViewport
    }

    /// <summary>
    /// The settings of a Scroll View element: its Scroll Rect's own fields, with the defaults of Unity's
    /// GameObject > UI > Scroll View. Its viewport, content and scrollbars are built from them, so they aren't
    /// set here.
    /// </summary>
    [Serializable]
    public sealed class ScrollViewSettings
    {
        public bool open = true;
        public bool horizontal = true;
        public bool vertical = true;
        public ScrollMovementType movementType = ScrollMovementType.Elastic;
        public float elasticity = 0.1f;
        public bool inertia = true;
        public float decelerationRate = 0.135f;
        public float scrollSensitivity = 1f;

        public bool horizontalScrollbar = true;
        public ScrollbarVisibility horizontalScrollbarVisibility = ScrollbarVisibility.AutoHideAndExpandViewport;
        public float horizontalScrollbarSpacing = -3f;
        public bool verticalScrollbar = true;
        public ScrollbarVisibility verticalScrollbarVisibility = ScrollbarVisibility.AutoHideAndExpandViewport;
        public float verticalScrollbarSpacing = -3f;
    }

    /// <summary>An optional Content Size Fitter component.</summary>
    [Serializable]
    public sealed class ContentSizeFitterSettings
    {
        public bool enabled;
        public bool open = true;
        public FitMode horizontalFit;
        public FitMode verticalFit;
    }

    /// <summary>An optional Canvas Group component.</summary>
    [Serializable]
    public sealed class CanvasGroupSettings
    {
        public bool enabled;
        public bool open = true;
        [Range(0f, 1f)] public float alpha = 1f;
        public bool interactable = true;
        public bool blocksRaycasts = true;
        public bool ignoreParentGroups;
    }

    /// <summary>
    /// An optional layout group component - Horizontal, Vertical or Grid; a GameObject holds one at most. Its
    /// fields are uGUI's, with uGUI's defaults.
    /// </summary>
    [Serializable]
    public sealed class LayoutGroupSettings
    {
        public bool enabled;
        public bool open = true;
        public LayoutKind kind;

        public bool paddingOpen;
        public int paddingLeft;
        public int paddingRight;
        public int paddingTop;
        public int paddingBottom;
        public TextAnchor childAlignment = TextAnchor.UpperLeft;

        // Horizontal / Vertical Layout Group.
        public float spacing;
        public bool reverseArrangement;
        public bool controlChildWidth;
        public bool controlChildHeight;
        public bool useChildScaleWidth;
        public bool useChildScaleHeight;
        public bool childForceExpandWidth = true;
        public bool childForceExpandHeight = true;

        // Grid Layout Group.
        public Vector2 cellSize = new(100f, 100f);
        public Vector2 gridSpacing;
        public GridStartCorner startCorner;
        public GridStartAxis startAxis;
        public GridConstraint constraint;
        public int constraintCount = 2;
    }

    /// <summary>
    /// An optional Mask component: clips the children to the element's graphic. An element without a graphic of
    /// its own (e.g. Empty) gets an Image for it when built, since a Mask needs one.
    /// </summary>
    [Serializable]
    public sealed class MaskSettings
    {
        public bool enabled;
        public bool open = true;
        public bool showMaskGraphic = true;
    }

    /// <summary>An optional Rect Mask 2D component: clips the children to the element's rect, without a graphic.</summary>
    [Serializable]
    public sealed class RectMask2DSettings
    {
        public bool enabled;
        public bool open = true;
        public bool paddingOpen;

        // As uGUI keeps it: x left, y bottom, z right, w top.
        public Vector4 padding;
        public Vector2Int softness;
    }

    /// <summary>An optional Layout Element component: each size is only used while its box is ticked, as in uGUI.</summary>
    [Serializable]
    public sealed class LayoutElementSettings
    {
        public bool enabled;
        public bool open = true;
        public bool ignoreLayout;
        public bool useMinWidth;
        public float minWidth;
        public bool useMinHeight;
        public float minHeight;
        public bool usePreferredWidth;
        public float preferredWidth;
        public bool usePreferredHeight;
        public float preferredHeight;
        public bool useFlexibleWidth;
        public float flexibleWidth = 1f;
        public bool useFlexibleHeight;
        public float flexibleHeight = 1f;
        public int layoutPriority = 1;
    }
}
