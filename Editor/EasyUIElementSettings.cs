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
    }

    /// <summary>An Image: an element's own, or the background of a Button, Dropdown or Input Field.</summary>
    [Serializable]
    public sealed class ImageSettings
    {
        public bool open = true;
        public Sprite sprite;
        public Color color = Color.white;
        public bool raycastTarget = true;
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
    }
}
