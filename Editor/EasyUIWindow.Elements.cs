using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace EasyUI
{
    // The sections of the components each kind of element is built with, under its Rect Transform, laid out like
    // their own Inspectors:
    //   Empty: none.  Text: Text.  Image: Image.  Raw Image: Raw Image.
    //   Button: Image (its background) and Button.  Toggle: Toggle.  Slider: Slider.
    //   Dropdown: Image and Dropdown.  Input Field: Image and Input Field.  Scroll View: Scroll View.
    // Every control's section starts with what all controls share (Interactable, Transition and its colors,
    // sprites or triggers).
    internal sealed partial class EasyUIWindow
    {
        private void DrawElementSections(EasyUINode node)
        {
            switch (node.type)
            {
                case EasyUIElementType.Text:
                    if (Section("Text", ref node.text.open))
                    {
                        DrawText(node.text);
                    }

                    break;

                case EasyUIElementType.Image:
                    DrawImageSection(node.image);
                    break;

                case EasyUIElementType.RawImage:
                    if (Section("Raw Image", ref node.rawImage.open))
                    {
                        DrawRawImage(node.rawImage);
                    }

                    break;

                case EasyUIElementType.Button:
                    DrawImageSection(node.image);
                    if (Section("Button", ref node.button.open))
                    {
                        DrawSelectable(node.selectable);
                        node.button.label = TextRow("Text", node.button.label);
                    }

                    break;

                case EasyUIElementType.Toggle:
                    if (Section("Toggle", ref node.toggle.open))
                    {
                        DrawSelectable(node.selectable);
                        ToggleRow("Is On", ref node.toggle.isOn);
                        node.toggle.toggleTransition = (Toggle.ToggleTransition)EnumRow("Toggle Transition", node.toggle.toggleTransition);
                        node.toggle.label = TextRow("Text", node.toggle.label);
                    }

                    break;

                case EasyUIElementType.Slider:
                    if (Section("Slider", ref node.slider.open))
                    {
                        DrawSelectable(node.selectable);
                        DrawSlider(node.slider);
                    }

                    break;

                case EasyUIElementType.Dropdown:
                    DrawImageSection(node.image);
                    if (Section("Dropdown", ref node.dropdown.open))
                    {
                        DrawSelectable(node.selectable);
                        DrawDropdown(node.dropdown);
                    }

                    break;

                case EasyUIElementType.InputField:
                    DrawImageSection(node.image);
                    if (Section("Input Field", ref node.inputField.open))
                    {
                        DrawSelectable(node.selectable);
                        DrawInputField(node.inputField);
                    }

                    break;

                case EasyUIElementType.ScrollView:
                    if (Section("Scroll View", ref node.scrollView.open))
                    {
                        DrawScrollViewSection(node);
                    }

                    break;
            }
        }

        // The sprites Unity's own controls start with.
        private static void ApplyDefaultLook(EasyUINode node)
        {
            var sprite = node.type switch
            {
                EasyUIElementType.Button or EasyUIElementType.Dropdown => "UI/Skin/UISprite.psd",
                EasyUIElementType.InputField => "UI/Skin/InputFieldBackground.psd",
                _ => null
            };

            if (sprite != null)
            {
                node.image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>(sprite);
                node.image.imageType = Image.Type.Sliced;
            }
        }

        #region Text

        private void DrawText(TextSettings text)
        {
            EditorGUILayout.LabelField("Text Input");
            text.text = EditorGUILayout.TextArea(text.text, GUILayout.MinHeight(48f));
            Gap();

            text.font = (TMP_FontAsset)EditorGUILayout.ObjectField(new GUIContent("Font Asset", "Empty: the default font of TMP Settings"),
                text.font, typeof(TMP_FontAsset), false);
            Gap();
            text.fontStyle = (FontStyles)EditorGUILayout.EnumFlagsField("Font Style", text.fontStyle);
            Gap();
            text.fontSize = Mathf.Max(0f, EditorGUILayout.FloatField("Font Size", text.fontSize));
            Gap();
            ToggleRow("Auto Size", ref text.autoSize);
            if (text.autoSize)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    text.fontSizeMin = Mathf.Max(0f, EditorGUILayout.FloatField("Min", text.fontSizeMin));
                    Gap();
                    text.fontSizeMax = Mathf.Max(text.fontSizeMin, EditorGUILayout.FloatField("Max", text.fontSizeMax));
                    Gap();
                }
            }

            text.color = EditorGUILayout.ColorField("Vertex Color", text.color);
            Gap();
            text.alignment = (TextAlignmentOptions)EnumRow("Alignment", text.alignment);
            text.wrapping = (TextWrappingModes)EnumRow("Wrapping", text.wrapping);
            text.overflow = (TextOverflowModes)EnumRow("Overflow", text.overflow);
            ToggleRow("Rich Text", ref text.richText);
            ToggleRow("Raycast Target", ref text.raycastTarget);
        }

        #endregion

        #region Images

        private void DrawImageSection(ImageSettings image)
        {
            if (!Section("Image", ref image.open))
            {
                return;
            }

            image.sprite = (Sprite)EditorGUILayout.ObjectField("Source Image", image.sprite, typeof(Sprite), false,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            Gap();
            image.color = EditorGUILayout.ColorField("Color", image.color);
            Gap();
            ToggleRow("Raycast Target", ref image.raycastTarget);
            image.imageType = (Image.Type)EnumRow("Image Type", image.imageType);

            using (new EditorGUI.IndentLevelScope())
            {
                switch (image.imageType)
                {
                    case Image.Type.Simple:
                        ToggleRow("Preserve Aspect", ref image.preserveAspect);
                        break;

                    case Image.Type.Sliced:
                    case Image.Type.Tiled:
                        ToggleRow("Fill Center", ref image.fillCenter);
                        image.pixelsPerUnitMultiplier = Mathf.Max(0.01f, EditorGUILayout.FloatField("Pixels Per Unit Multiplier", image.pixelsPerUnitMultiplier));
                        Gap();
                        break;

                    case Image.Type.Filled:
                        DrawFill(image);
                        ToggleRow("Preserve Aspect", ref image.preserveAspect);
                        break;
                }
            }
        }

        // Fill Method, then the Fill Origin that method has (left / right for Horizontal, a corner for Radial 90...).
        private static void DrawFill(ImageSettings image)
        {
            image.fillMethod = (Image.FillMethod)EnumRow("Fill Method", image.fillMethod);
            var origins = image.fillMethod is Image.FillMethod.Horizontal or Image.FillMethod.Vertical ? 2 : 4;
            image.fillOrigin = Mathf.Clamp(image.fillOrigin, 0, origins - 1);

            image.fillOrigin = image.fillMethod switch
            {
                Image.FillMethod.Horizontal => (int)(Image.OriginHorizontal)EnumRow("Fill Origin", (Image.OriginHorizontal)image.fillOrigin),
                Image.FillMethod.Vertical => (int)(Image.OriginVertical)EnumRow("Fill Origin", (Image.OriginVertical)image.fillOrigin),
                Image.FillMethod.Radial90 => (int)(Image.Origin90)EnumRow("Fill Origin", (Image.Origin90)image.fillOrigin),
                Image.FillMethod.Radial180 => (int)(Image.Origin180)EnumRow("Fill Origin", (Image.Origin180)image.fillOrigin),
                _ => (int)(Image.Origin360)EnumRow("Fill Origin", (Image.Origin360)image.fillOrigin)
            };

            image.fillAmount = EditorGUILayout.Slider("Fill Amount", image.fillAmount, 0f, 1f);
            Gap();
            if (image.fillMethod is not (Image.FillMethod.Horizontal or Image.FillMethod.Vertical))
            {
                ToggleRow("Clockwise", ref image.fillClockwise);
            }
        }

        private static void DrawRawImage(RawImageSettings raw)
        {
            raw.texture = (Texture)EditorGUILayout.ObjectField("Texture", raw.texture, typeof(Texture), false,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            Gap();
            raw.color = EditorGUILayout.ColorField("Color", raw.color);
            Gap();
            ToggleRow("Raycast Target", ref raw.raycastTarget);
            raw.uvRect = EditorGUILayout.RectField("UV Rect", raw.uvRect);
            Gap();
        }

        #endregion

        #region Controls

        // What every control shares: Interactable, and its Transition - colors, sprites or animation triggers.
        private static void DrawSelectable(SelectableSettings selectable)
        {
            ToggleRow("Interactable", ref selectable.interactable);
            selectable.transition = (Selectable.Transition)EnumRow("Transition", selectable.transition);

            using (new EditorGUI.IndentLevelScope())
            {
                switch (selectable.transition)
                {
                    case Selectable.Transition.ColorTint:
                    {
                        var colors = selectable.colors;
                        colors.normalColor = ColorRow("Normal Color", colors.normalColor);
                        colors.highlightedColor = ColorRow("Highlighted Color", colors.highlightedColor);
                        colors.pressedColor = ColorRow("Pressed Color", colors.pressedColor);
                        colors.selectedColor = ColorRow("Selected Color", colors.selectedColor);
                        colors.disabledColor = ColorRow("Disabled Color", colors.disabledColor);
                        colors.colorMultiplier = EditorGUILayout.Slider("Color Multiplier", colors.colorMultiplier, 1f, 5f);
                        Gap();
                        colors.fadeDuration = Mathf.Max(0f, EditorGUILayout.FloatField("Fade Duration", colors.fadeDuration));
                        Gap();
                        selectable.colors = colors;
                        break;
                    }

                    case Selectable.Transition.SpriteSwap:
                    {
                        var sprites = selectable.spriteState;
                        sprites.highlightedSprite = SpriteRow("Highlighted Sprite", sprites.highlightedSprite);
                        sprites.pressedSprite = SpriteRow("Pressed Sprite", sprites.pressedSprite);
                        sprites.selectedSprite = SpriteRow("Selected Sprite", sprites.selectedSprite);
                        sprites.disabledSprite = SpriteRow("Disabled Sprite", sprites.disabledSprite);
                        selectable.spriteState = sprites;
                        break;
                    }

                    case Selectable.Transition.Animation:
                    {
                        var triggers = selectable.animationTriggers;
                        triggers.normalTrigger = TextRow("Normal Trigger", triggers.normalTrigger);
                        triggers.highlightedTrigger = TextRow("Highlighted Trigger", triggers.highlightedTrigger);
                        triggers.pressedTrigger = TextRow("Pressed Trigger", triggers.pressedTrigger);
                        triggers.selectedTrigger = TextRow("Selected Trigger", triggers.selectedTrigger);
                        triggers.disabledTrigger = TextRow("Disabled Trigger", triggers.disabledTrigger);
                        break;
                    }
                }
            }

            GUILayout.Space(4f);
        }

        private static void DrawSlider(SliderSettings slider)
        {
            slider.direction = (Slider.Direction)EnumRow("Direction", slider.direction);
            slider.minValue = EditorGUILayout.FloatField("Min Value", slider.minValue);
            Gap();
            slider.maxValue = EditorGUILayout.FloatField("Max Value", slider.maxValue);
            Gap();
            ToggleRow("Whole Numbers", ref slider.wholeNumbers);

            var low = Mathf.Min(slider.minValue, slider.maxValue);
            var high = Mathf.Max(slider.minValue, slider.maxValue);
            slider.value = slider.wholeNumbers
                ? EditorGUILayout.IntSlider("Value", Mathf.RoundToInt(slider.value), Mathf.RoundToInt(low), Mathf.RoundToInt(high))
                : EditorGUILayout.Slider("Value", slider.value, low, high);
            Gap();
        }

        // Its options as a list - each one's text with a minus to remove it, and a plus under them - then Value,
        // the option shown first.
        private static void DrawDropdown(DropdownSettings dropdown)
        {
            EditorGUILayout.LabelField("Options");
            var remove = -1;
            using (new EditorGUI.IndentLevelScope())
            {
                for (var i = 0; i < dropdown.options.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        dropdown.options[i] = EditorGUILayout.TextField(dropdown.options[i]);
                        if (GUILayout.Button(new GUIContent("−", "Remove this option"), EditorStyles.miniButton, GUILayout.Width(22f)))
                        {
                            remove = i;
                        }
                    }

                    Gap();
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("+", "Add an option"), EditorStyles.miniButton, GUILayout.Width(22f)))
                    {
                        dropdown.options.Add($"Option {(char)('A' + dropdown.options.Count % 26)}");
                    }
                }
            }

            if (remove >= 0)
            {
                dropdown.options.RemoveAt(remove);
            }

            Gap();
            using (new EditorGUI.DisabledScope(dropdown.options.Count == 0))
            {
                dropdown.value = dropdown.options.Count > 0
                    ? EditorGUILayout.Popup("Value", Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1), dropdown.options.ToArray())
                    : EditorGUILayout.Popup("Value", 0, new[] { "(no options)" });
            }

            Gap();
        }

        private static void DrawInputField(InputFieldSettings input)
        {
            input.text = TextRow("Text", input.text);
            input.placeholder = TextRow("Placeholder", input.placeholder);
            input.contentType = (TMP_InputField.ContentType)EnumRow("Content Type", input.contentType);
            input.lineType = (TMP_InputField.LineType)EnumRow("Line Type", input.lineType);
            input.characterLimit = Mathf.Max(0, EditorGUILayout.IntField(new GUIContent("Character Limit", "0: no limit"), input.characterLimit));
            Gap();
            ToggleRow("Read Only", ref input.readOnly);
        }

        #endregion

        #region Rows

        private static void Gap() => GUILayout.Space(RowGap);

        private static System.Enum EnumRow(string label, System.Enum value)
        {
            var result = EditorGUILayout.EnumPopup(label, value);
            Gap();
            return result;
        }

        private static string TextRow(string label, string value)
        {
            var result = EditorGUILayout.TextField(label, value);
            Gap();
            return result;
        }

        private static Color ColorRow(string label, Color value)
        {
            var result = EditorGUILayout.ColorField(label, value);
            Gap();
            return result;
        }

        private static Sprite SpriteRow(string label, Sprite value)
        {
            var result = (Sprite)EditorGUILayout.ObjectField(label, value, typeof(Sprite), false, GUILayout.Height(EditorGUIUtility.singleLineHeight));
            Gap();
            return result;
        }

        #endregion
    }
}
