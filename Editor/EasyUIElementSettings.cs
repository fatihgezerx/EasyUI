using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace EasyUI
{
    // The components each kind of element is built with, as their Inspectors show them, with the defaults of
    // Unity's GameObject > UI menu. An element only uses the ones of its kind (see EasyUIWindow.Elements.cs).

    /// <summary>A TextMeshPro text: an element's own (Text), or the label of a Button or Toggle.</summary>
    [Serializable]
    public sealed class TextSettings
    {
        public bool open = true;
        [TextArea(2, 6)] public string text = "New Text";
        public TMP_FontAsset font;
        public FontStyles fontStyle = FontStyles.Normal;
        public float fontSize = 36f;
        public bool autoSize;
        public float fontSizeMin = 18f;
        public float fontSizeMax = 72f;
        public Color color = Color.white;
        public TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft;
        public TextWrappingModes wrapping = TextWrappingModes.Normal;
        public TextOverflowModes overflow = TextOverflowModes.Overflow;
        public bool richText = true;
        public bool raycastTarget = true;
        public bool maskable = true;
    }

    /// <summary>An Image: an element's own, or the background of a Button, Dropdown or Input Field.</summary>
    [Serializable]
    public sealed class ImageSettings
    {
        public bool open = true;
        public Sprite sprite;
        public Color color = Color.white;
        public bool raycastTarget = true;
        public bool maskable = true;
        public Image.Type imageType = Image.Type.Simple;
        public bool preserveAspect;
        public bool fillCenter = true;
        public float pixelsPerUnitMultiplier = 1f;
        public Image.FillMethod fillMethod = Image.FillMethod.Radial360;
        public int fillOrigin;
        [Range(0f, 1f)] public float fillAmount = 1f;
        public bool fillClockwise = true;
    }

    [Serializable]
    public sealed class RawImageSettings
    {
        public bool open = true;
        public Texture texture;
        public Color color = Color.white;
        public bool raycastTarget = true;
        public bool maskable = true;
        public Rect uvRect = new(0f, 0f, 1f, 1f);
    }

    /// <summary>What every control (Button, Toggle, Slider, Dropdown, Input Field) shares: its Selectable fields.</summary>
    [Serializable]
    public sealed class SelectableSettings
    {
        public bool interactable = true;
        public Selectable.Transition transition = Selectable.Transition.ColorTint;
        public ColorBlock colors = ColorBlock.defaultColorBlock;
        public SpriteState spriteState;
        public AnimationTriggers animationTriggers = new();
    }

    [Serializable]
    public sealed class ButtonSettings
    {
        public bool open = true;
        // Only read once, to seed the Text part of a Button saved before parts existed.
        public string label = "Button";
    }

    [Serializable]
    public sealed class ToggleSettings
    {
        public bool open = true;
        public bool isOn = true;
        public Toggle.ToggleTransition toggleTransition = Toggle.ToggleTransition.Fade;
        // Only read once, to seed the Label part of a Toggle saved before parts existed.
        public string label = "Toggle";
    }

    [Serializable]
    public sealed class SliderSettings
    {
        public bool open = true;
        public Slider.Direction direction = Slider.Direction.LeftToRight;
        public float minValue;
        public float maxValue = 1f;
        public bool wholeNumbers;
        public float value;

        // Off: the pieces are as Unity makes them; on: each is placed and drawn as its settings below say (Convert fills them in).
        public bool styled;
        public SliderPieceSettings background = new();
        public SliderPieceSettings fillArea = new();
        public SliderPieceSettings fill = new();
        public SliderPieceSettings handleArea = new();
        public SliderPieceSettings handle = new();
    }

    /// <summary>
    /// One piece of a slider - Background, Fill Area, Fill, Handle Slide Area, Handle: where it sits in its parent and the Image it
    /// has, if any. A piece the slider doesn't have (<see cref="present"/> off) is built as Unity makes it, but not drawn.
    /// </summary>
    [Serializable]
    public sealed class SliderPieceSettings
    {
        public bool open = true;
        public bool present = true;
        public bool hasImage;
        public ImageSettings image = new();

        // As a RectTransform keeps them. A slider moves the anchors of its Fill and Handle itself, so those two are only offset.
        public Vector2 anchorMin;
        public Vector2 anchorMax = Vector2.one;
        public Vector2 offsetMin;
        public Vector2 offsetMax;
    }

    /// <summary>
    /// Where a scrollbar's Handle sits in the Sliding Area Unity builds under its Scrollbar (that area is not an element of its
    /// own). Only the Handle node of a converted scene uses it; off, the two are as Unity makes them.
    /// </summary>
    [Serializable]
    public sealed class ScrollbarHandleSettings
    {
        public bool styled;

        // The Sliding Area in the Scrollbar, as a RectTransform keeps it.
        public Vector2 areaAnchorMin;
        public Vector2 areaAnchorMax = Vector2.one;
        public Vector2 areaOffsetMin;
        public Vector2 areaOffsetMax;

        // The Handle in the Sliding Area: its anchors along the bar belong to the Scrollbar, so only the offsets are kept.
        public Vector2 handleOffsetMin;
        public Vector2 handleOffsetMax;
    }

    [Serializable]
    public sealed class DropdownSettings
    {
        public bool open = true;
        public List<string> options = new() { "Option A", "Option B", "Option C" };
        public int value;
    }

    [Serializable]
    public sealed class InputFieldSettings
    {
        public bool open = true;
        public string text = string.Empty;
        public string placeholder = "Enter text...";
        public TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard;
        public TMP_InputField.LineType lineType = TMP_InputField.LineType.SingleLine;
        public int characterLimit;
        public bool readOnly;

        // Off: the text and placeholder look as Unity makes them; on: as the two styles below (their own text is not used).
        public bool styled;
        public TextSettings textStyle = new();
        public TextSettings placeholderStyle = new();

        // Where the text area sits inside the field, as a RectTransform keeps it: offsetMin is (left, bottom), offsetMax (right, top).
        // Used with the styles; Unity's own field has (10, 7) and (-10, -6).
        public Vector2 viewportMin = new(10f, 7f);
        public Vector2 viewportMax = new(-10f, -6f);

        // The text's and the placeholder's own rectangles inside the text area (Unity's are stretched, with no offsets).
        public Vector2 textMin;
        public Vector2 textMax;
        public Vector2 placeholderMin;
        public Vector2 placeholderMax;
    }
}
