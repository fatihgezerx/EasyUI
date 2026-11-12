using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UGUIText = UnityEngine.UI.Text;

namespace EasyUI
{
    /// <summary>
    /// Reads a panel laid out by hand under a canvas back into an <see cref="EasyUIDocument"/> - the reverse of
    /// <see cref="EasyUIPanelBuilder"/>: every object under the root becomes an element of the kind its components
    /// say (Text, Image, Button, Scroll View...), placed where it is on the canvas, with its anchors, pivot, the
    /// settings of its components and the optional components Easy UI knows (Content Size Fitter, Canvas Group, layout
    /// groups, Layout Element, masks). What Easy UI cannot hold is left out and listed in the notes.
    /// </summary>
    /// <remarks>
    /// The pieces of a Scroll View, Button, Toggle or Dropdown are matched to Easy UI's parts (see
    /// <see cref="EasyUIParts"/>); a part the object does not have is made the way Unity makes it. The inside of a
    /// Slider or Input Field and a Dropdown's template are not read: Easy UI builds them as Unity does.
    /// </remarks>
    internal sealed class EasyUISceneConverter
    {
        private const float Epsilon = 0.001f;
        private const int MaxNamesShown = 4;

        private const string AnchorNote = "Anchors that are not a preset (left / center / right / stretch, top / middle / bottom) were moved to the nearest one";
        private const string TransformNote = "Rotation and scale are not kept (the size includes the scale)";
        private const string InactiveNote = "Inactive objects were converted as active (Easy UI keeps no active state): laid out as if absent (Ignore Layout) and their graphics made fully transparent";
        private const string DisabledComponentNote = "Disabled Layout Group / Content Size Fitter / Layout Element / Canvas Group / Mask components were left out";
        private const string ScrollBackgroundNote = "A Scroll View's own background look is not kept (Easy UI builds Unity's default, or none)";
        private const string DisabledNote = "Disabled graphics were made fully transparent (Easy UI keeps no enabled state)";
        private const string NoRectNote = "Objects without a Rect Transform were skipped";
        private const string InsideNote = "Inside a Slider / Input Field only Unity's own pieces are read (their place, look and text styles); anything else added there is not kept";
        private const string LegacyTextNote = "Legacy Text became TextMeshPro (its font was not kept)";
        private const string LegacyControlNote = "Legacy Dropdown / Input Field are not supported (use the TextMeshPro ones)";
        private const string LayoutGroupNote = "Only Horizontal, Vertical and Grid Layout Groups are kept";

        private readonly Transform canvasTransform;
        private readonly Rect canvasRect;
        private readonly EasyUIDocument document = new();
        private readonly HashSet<Transform> skipped = new();
        private readonly Dictionary<string, List<GameObject>> problems = new();
        private readonly Dictionary<Type, EasyUIRole> roleByComponent = new();
        private readonly Vector3[] corners = new Vector3[4];
        private Transform rootTransform;

        private EasyUISceneConverter(Canvas canvas)
        {
            canvasTransform = canvas.transform;
            canvasRect = ((RectTransform)canvas.transform).rect;
            CollectRoles();
        }

        /// <summary>
        /// Reads <paramref name="root"/> and everything under it into a new document: <paramref name="root"/> is its
        /// root element and the canvas it is under gives the canvas size. Returns null, with the reason in
        /// <paramref name="notes"/>, when it is not under a canvas; otherwise <paramref name="notes"/> lists what
        /// could not be kept.
        /// </summary>
        public static EasyUIDocument Convert(RectTransform root, out List<string> notes)
        {
            notes = new List<string>();
            var canvas = root.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                notes.Add("This object is not under a Canvas, so there is no canvas to measure the panel on.");
                return null;
            }

            var converter = new EasyUISceneConverter(canvas.rootCanvas);
            if (converter.canvasRect.width <= 0f || converter.canvasRect.height <= 0f)
            {
                notes.Add("The canvas has no size yet. Open the Game view (or select the canvas) and try again.");
                return null;
            }

            var document = converter.Run(root);
            converter.ListProblems(notes);
            return document;
        }

        private EasyUIDocument Run(RectTransform root)
        {
            rootTransform = root;
            document.panelName = root.name;
            document.canvasSize = canvasRect.size;

            var rootNode = ConvertElement(root.gameObject, 0, EasyUIPart.None);
            document.rootId = rootNode.id;

            // A Button or Toggle without a label in the scene is made one by EnsureParts: it stays empty, as the scene was.
            var firstMade = document.nextId;
            EasyUIParts.EnsureParts(document);
            foreach (var node in document.nodes)
            {
                if (node.id >= firstMade && node.part is EasyUIPart.ButtonText or EasyUIPart.ToggleLabel)
                {
                    node.text.text = string.Empty;
                }
            }

            return document;
        }

        #region Elements

        private EasyUINode ConvertElement(GameObject go, int parentId, EasyUIPart part)
        {
            var node = new EasyUINode
            {
                id = document.nextId++,
                parentId = parentId,
                part = part,
                name = go.name,
                type = TypeOf(go, part),
                editStamp = ++document.editCounter
            };

            // The parent first, so a build (and the window) meets it before what is in it.
            document.nodes.Add(node);

            ReadRect(go, node);
            ReadSettings(go, node);
            ReadComponents(go, node);

            // An inactive object is no part of its parent's layout; Easy UI can't keep it inactive, so it is laid out as absent.
            if (!go.activeSelf && parentId != 0)
            {
                node.layoutElement.enabled = true;
                node.layoutElement.ignoreLayout = true;
            }
            ReadRoles(go, node);
            ConvertChildren(go, node);
            return node;
        }

        private EasyUIElementType TypeOf(GameObject go, EasyUIPart part)
        {
            if (go.TryGetComponent<UnityEngine.UI.Dropdown>(out _) || go.TryGetComponent<InputField>(out _))
            {
                Problem(LegacyControlNote, go);
            }

            var type = EasyUIElementType.EmptyObject;
            if (go.TryGetComponent<ScrollRect>(out _))
            {
                type = EasyUIElementType.ScrollView;
            }
            else if (go.TryGetComponent<TMP_Dropdown>(out _))
            {
                type = EasyUIElementType.Dropdown;
            }
            else if (go.TryGetComponent<TMP_InputField>(out _))
            {
                type = EasyUIElementType.InputField;
            }
            else if (go.TryGetComponent<Slider>(out _))
            {
                type = EasyUIElementType.Slider;
            }
            else if (go.TryGetComponent<Toggle>(out _))
            {
                type = EasyUIElementType.Toggle;
            }
            else if (go.TryGetComponent<Button>(out _))
            {
                type = EasyUIElementType.Button;
            }
            else if (HasText(go.transform))
            {
                type = EasyUIElementType.Text;
            }
            else if (go.TryGetComponent<RawImage>(out _))
            {
                type = EasyUIElementType.RawImage;
            }
            else if (go.TryGetComponent<Image>(out _))
            {
                type = EasyUIElementType.Image;
            }

            // A part is an Image, a Text or an Empty; what else sits on it is not a part of a part.
            if (part != EasyUIPart.None && EasyUIParts.IsComposite(type))
            {
                type = go.TryGetComponent<Image>(out _) ? EasyUIElementType.Image : EasyUIElementType.EmptyObject;
            }

            return type;
        }

        private static bool HasText(Transform transform) =>
            transform.TryGetComponent<TMP_Text>(out _) || transform.TryGetComponent<UGUIText>(out _);

        private void ConvertChildren(GameObject go, EasyUINode node)
        {
            // A scrollbar's handle sits in a Sliding Area that Easy UI does not keep: the handle stands in its place.
            Transform handleSlot = null;
            RectTransform handle = null;
            if (node.part is EasyUIPart.ScrollbarHorizontal or EasyUIPart.ScrollbarVertical &&
                go.TryGetComponent<Scrollbar>(out var scrollbar) && scrollbar.handleRect != null && scrollbar.handleRect.IsChildOf(go.transform))
            {
                handle = scrollbar.handleRect;
                handleSlot = handle;
                while (handleSlot.parent != go.transform)
                {
                    handleSlot = handleSlot.parent;
                }
            }

            if (!node.IsPart && node.type is EasyUIElementType.Slider or EasyUIElementType.InputField)
            {
                if (go.transform.childCount > 0)
                {
                    Problem(InsideNote, go);
                }

                return;
            }

            var parts = PartsOf(go, node);
            foreach (Transform child in go.transform)
            {
                if (child is not RectTransform)
                {
                    Problem(NoRectNote, child.gameObject);
                    continue;
                }

                if (skipped.Contains(child))
                {
                    continue;
                }

                if (child == handleSlot)
                {
                    var handleNode = ConvertElement(handle.gameObject, node.id, EasyUIPart.ScrollbarHandle);
                    ReadHandleFit(handle, handleNode.scrollbarHandle);
                    continue;
                }

                if (!child.gameObject.activeSelf)
                {
                    Problem(InactiveNote, child.gameObject);
                }

                var part = EasyUIPart.None;
                parts?.TryGetValue(child, out part);
                ConvertElement(child.gameObject, node.id, part);
            }
        }

        // The Sliding Area the handle sits in (full size when it sits straight in the scrollbar) and the handle's own offsets.
        private static void ReadHandleFit(RectTransform handle, ScrollbarHandleSettings settings)
        {
            settings.styled = true;
            settings.handleOffsetMin = handle.offsetMin;
            settings.handleOffsetMax = handle.offsetMax;

            var area = handle.parent as RectTransform;
            if (area == null || area.GetComponent<Scrollbar>() != null)
            {
                settings.areaAnchorMin = Vector2.zero;
                settings.areaAnchorMax = Vector2.one;
                settings.areaOffsetMin = Vector2.zero;
                settings.areaOffsetMax = Vector2.zero;
                return;
            }

            settings.areaAnchorMin = area.anchorMin;
            settings.areaAnchorMax = area.anchorMax;
            settings.areaOffsetMin = area.offsetMin;
            settings.areaOffsetMax = area.offsetMax;
        }

        // Which of the object's direct children are the parts of its element (or, for a part, of the part): null when
        // none. Only direct children count - Easy UI keeps the parts one level down, as Unity makes them.
        private Dictionary<Transform, EasyUIPart> PartsOf(GameObject go, EasyUINode node)
        {
            var parts = new Dictionary<Transform, EasyUIPart>();
            if (node.part == EasyUIPart.Viewport)
            {
                var scroll = go.GetComponentInParent<ScrollRect>(true);
                if (scroll != null)
                {
                    AddPart(parts, go, scroll.content, EasyUIPart.Content);
                }
            }
            else if (node.part == EasyUIPart.ToggleBackground)
            {
                var toggle = go.GetComponentInParent<Toggle>(true);
                if (toggle != null)
                {
                    AddPart(parts, go, toggle.graphic, EasyUIPart.ToggleCheckmark);
                }
            }
            else if (!node.IsPart)
            {
                switch (node.type)
                {
                    case EasyUIElementType.ScrollView:
                        var scroll = go.GetComponent<ScrollRect>();
                        AddPart(parts, go, scroll.viewport, EasyUIPart.Viewport);
                        AddPart(parts, go, scroll.horizontalScrollbar, EasyUIPart.ScrollbarHorizontal);
                        AddPart(parts, go, scroll.verticalScrollbar, EasyUIPart.ScrollbarVertical);
                        break;

                    case EasyUIElementType.Button:
                        AddPart(parts, go, FirstTextChild(go.transform, null), EasyUIPart.ButtonText);
                        break;

                    case EasyUIElementType.Toggle:
                        var toggle = go.GetComponent<Toggle>();
                        AddPart(parts, go, toggle.targetGraphic, EasyUIPart.ToggleBackground);
                        AddPart(parts, go, FirstTextChild(go.transform, toggle.targetGraphic != null ? toggle.targetGraphic.transform : null), EasyUIPart.ToggleLabel);
                        break;

                    case EasyUIElementType.Dropdown:
                        var dropdown = go.GetComponent<TMP_Dropdown>();
                        AddPart(parts, go, dropdown.captionText, EasyUIPart.DropdownLabel);
                        AddPart(parts, go, go.transform.Find("Arrow"), EasyUIPart.DropdownArrow);

                        // Its list is made again as Unity makes it.
                        if (dropdown.template != null)
                        {
                            skipped.Add(dropdown.template);
                        }

                        break;
                }
            }

            return parts.Count > 0 ? parts : null;
        }

        private static void AddPart(Dictionary<Transform, EasyUIPart> parts, GameObject owner, Component piece, EasyUIPart part)
        {
            if (piece != null && piece.transform.parent == owner.transform && piece.transform is RectTransform)
            {
                parts.TryAdd(piece.transform, part);
            }
        }

        private static Component FirstTextChild(Transform parent, Transform except)
        {
            foreach (Transform child in parent)
            {
                if (child != except && HasText(child))
                {
                    return child;
                }
            }

            return null;
        }

        #endregion

        #region Rect

        private void ReadRect(GameObject go, EasyUINode node)
        {
            var rect = (RectTransform)go.transform;

            // Where it is drawn on the canvas, in the canvas's units, from the canvas's top-left corner down.
            rect.GetWorldCorners(corners);
            var min = canvasTransform.InverseTransformPoint(corners[0]);
            var max = canvasTransform.InverseTransformPoint(corners[2]);
            node.position = new Vector2(Round(min.x - canvasRect.xMin), Round(canvasRect.yMax - max.y));
            node.size = new Vector2(Round(max.x - min.x), Round(max.y - min.y));
            node.pivot = new Vector2(Round(rect.pivot.x), Round(rect.pivot.y));
            node.anchorH = HorizontalOf(rect, go);
            node.anchorV = VerticalOf(rect, go);

            if (Quaternion.Angle(rect.localRotation, Quaternion.identity) > 0.01f || !Approximately(rect.localScale, Vector3.one))
            {
                Problem(TransformNote, go);
            }
        }

        private HorizontalAnchor HorizontalOf(RectTransform rect, GameObject go)
        {
            var min = rect.anchorMin.x;
            var max = rect.anchorMax.x;
            if (Mathf.Abs(max - min) > Epsilon)
            {
                if (min > Epsilon || max < 1f - Epsilon)
                {
                    Problem(AnchorNote, go);
                }

                return HorizontalAnchor.Stretch;
            }

            if (!IsPreset(min))
            {
                Problem(AnchorNote, go);
            }

            return min < 0.25f ? HorizontalAnchor.Left : min > 0.75f ? HorizontalAnchor.Right : HorizontalAnchor.Center;
        }

        private VerticalAnchor VerticalOf(RectTransform rect, GameObject go)
        {
            var min = rect.anchorMin.y;
            var max = rect.anchorMax.y;
            if (Mathf.Abs(max - min) > Epsilon)
            {
                if (min > Epsilon || max < 1f - Epsilon)
                {
                    Problem(AnchorNote, go);
                }

                return VerticalAnchor.Stretch;
            }

            if (!IsPreset(min))
            {
                Problem(AnchorNote, go);
            }

            return min < 0.25f ? VerticalAnchor.Bottom : min > 0.75f ? VerticalAnchor.Top : VerticalAnchor.Middle;
        }

        private static bool IsPreset(float anchor) =>
            Mathf.Abs(anchor) < Epsilon || Mathf.Abs(anchor - 0.5f) < Epsilon || Mathf.Abs(anchor - 1f) < Epsilon;

        private static float Round(float value) => Mathf.Round(value * 1000f) / 1000f;

        private static bool Approximately(Vector3 a, Vector3 b) =>
            Mathf.Abs(a.x - b.x) < Epsilon && Mathf.Abs(a.y - b.y) < Epsilon && Mathf.Abs(a.z - b.z) < Epsilon;

        #endregion

        #region Settings of a kind

        private void ReadSettings(GameObject go, EasyUINode node)
        {
            switch (node.type)
            {
                case EasyUIElementType.Text:
                    node.text = ReadAnyText(go);
                    break;

                case EasyUIElementType.Image:
                    ReadImage(go, node);
                    break;

                case EasyUIElementType.RawImage:
                    node.rawImage = ReadRawImage(go.GetComponent<RawImage>());
                    Hide(go.GetComponent<RawImage>(), ref node.rawImage.color);
                    break;

                case EasyUIElementType.Button:
                    ReadImage(go, node);
                    node.selectable = ReadSelectable(go.GetComponent<Button>());
                    break;

                case EasyUIElementType.Toggle:
                    var toggle = go.GetComponent<Toggle>();
                    node.selectable = ReadSelectable(toggle);
                    node.toggle.isOn = toggle.isOn;
                    node.toggle.toggleTransition = toggle.toggleTransition;
                    break;

                case EasyUIElementType.Slider:
                    var slider = go.GetComponent<Slider>();
                    node.selectable = ReadSelectable(slider);
                    node.slider.direction = slider.direction;
                    node.slider.minValue = slider.minValue;
                    node.slider.maxValue = slider.maxValue;
                    node.slider.wholeNumbers = slider.wholeNumbers;
                    node.slider.value = slider.value;
                    node.slider.styled = true;
                    node.slider.background = ReadPiece(slider.transform.Find("Background"));
                    node.slider.fillArea = ReadPiece(slider.fillRect != null && slider.fillRect.parent != slider.transform ? slider.fillRect.parent : null);
                    node.slider.fill = ReadPiece(slider.fillRect);
                    node.slider.handleArea = ReadPiece(slider.handleRect != null && slider.handleRect.parent != slider.transform ? slider.handleRect.parent : null);
                    node.slider.handle = ReadPiece(slider.handleRect);
                    break;

                case EasyUIElementType.Dropdown:
                    var dropdown = go.GetComponent<TMP_Dropdown>();
                    ReadImage(go, node);
                    node.selectable = ReadSelectable(dropdown);
                    node.dropdown.options = new List<string>(dropdown.options.Count);
                    foreach (var option in dropdown.options)
                    {
                        node.dropdown.options.Add(option.text);
                    }

                    node.dropdown.value = dropdown.value;
                    break;

                case EasyUIElementType.InputField:
                    var input = go.GetComponent<TMP_InputField>();
                    ReadImage(go, node);
                    node.selectable = ReadSelectable(input);
                    node.inputField.text = input.text;
                    node.inputField.placeholder = input.placeholder is TMP_Text placeholder ? placeholder.text : string.Empty;
                    node.inputField.contentType = input.contentType;
                    node.inputField.lineType = input.lineType;
                    node.inputField.characterLimit = input.characterLimit;
                    node.inputField.readOnly = input.readOnly;
                    if (input.textViewport != null)
                    {
                        node.inputField.viewportMin = input.textViewport.offsetMin;
                        node.inputField.viewportMax = input.textViewport.offsetMax;
                    }

                    if (input.textComponent != null)
                    {
                        node.inputField.styled = true;
                        node.inputField.textStyle = ReadTmp(input.textComponent);
                        node.inputField.textMin = input.textComponent.rectTransform.offsetMin;
                        node.inputField.textMax = input.textComponent.rectTransform.offsetMax;
                        if (input.placeholder is TMP_Text placeholderText)
                        {
                            node.inputField.placeholderStyle = ReadTmp(placeholderText);
                            node.inputField.placeholderMin = placeholderText.rectTransform.offsetMin;
                            node.inputField.placeholderMax = placeholderText.rectTransform.offsetMax;
                        }
                    }

                    break;

                case EasyUIElementType.ScrollView:
                    ReadScrollView(go, go.GetComponent<ScrollRect>(), node.scrollView);
                    break;
            }
        }

        private TextSettings ReadTmp(TMP_Text text)
        {
            var settings = new TextSettings
            {
                text = text.text,
                font = text.font,
                fontStyle = text.fontStyle,
                fontSize = text.fontSize,
                autoSize = text.enableAutoSizing,
                fontSizeMin = text.fontSizeMin,
                fontSizeMax = text.fontSizeMax,
                color = text.color,
                alignment = text.alignment,
                wrapping = text.textWrappingMode,
                overflow = text.overflowMode,
                richText = text.richText,
                raycastTarget = text.raycastTarget,
                maskable = text.maskable
            };

            Hide(text, ref settings.color);
            return settings;
        }

        private TextSettings ReadAnyText(GameObject go)
        {
            if (go.TryGetComponent<TMP_Text>(out var text))
            {
                return ReadTmp(text);
            }

            var legacy = go.GetComponent<UGUIText>();
            Problem(LegacyTextNote, go);
            var converted = new TextSettings
            {
                text = legacy.text,
                fontStyle = legacy.fontStyle switch
                {
                    FontStyle.Bold => FontStyles.Bold,
                    FontStyle.Italic => FontStyles.Italic,
                    FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
                    _ => FontStyles.Normal
                },
                fontSize = legacy.fontSize,
                autoSize = legacy.resizeTextForBestFit,
                fontSizeMin = legacy.resizeTextMinSize,
                fontSizeMax = legacy.resizeTextMaxSize,
                color = legacy.color,
                alignment = AlignmentOf(legacy.alignment),
                wrapping = legacy.horizontalOverflow == HorizontalWrapMode.Wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap,
                richText = legacy.supportRichText,
                raycastTarget = legacy.raycastTarget,
                maskable = legacy.maskable
            };

            Hide(legacy, ref converted.color);
            return converted;
        }

        private static TextAlignmentOptions AlignmentOf(TextAnchor anchor) => anchor switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center
        };

        private void ReadImage(GameObject go, EasyUINode node)
        {
            // A Button, Dropdown or Input Field without an Image is built with one: it stays invisible and doesn't catch clicks.
            if (!go.TryGetComponent<Image>(out var image))
            {
                node.image.color = new Color(1f, 1f, 1f, 0f);
                node.image.raycastTarget = false;
                return;
            }

            node.image = ReadImageSettings(image);
        }

        // The look of an Image; a disabled one, or one under an inactive object, is a transparent one.
        private ImageSettings ReadImageSettings(Image image)
        {
            var look = new ImageSettings
            {
                sprite = image.sprite,
                color = image.color,
                raycastTarget = image.raycastTarget,
                maskable = image.maskable,
                imageType = image.type,
                preserveAspect = image.preserveAspect,
                fillCenter = image.fillCenter,
                pixelsPerUnitMultiplier = image.pixelsPerUnitMultiplier,
                fillMethod = image.fillMethod,
                fillOrigin = image.fillOrigin,
                fillAmount = image.fillAmount,
                fillClockwise = image.fillClockwise
            };

            Hide(image, ref look.color);
            return look;
        }

        // One piece of a slider: where it sits and its Image, if it has one. One the slider doesn't have is not present (the built
        // slider has every piece: that one is built transparent).
        private SliderPieceSettings ReadPiece(Transform piece)
        {
            var settings = new SliderPieceSettings();
            if (piece == null)
            {
                settings.present = false;
                return settings;
            }

            var rect = (RectTransform)piece;
            settings.anchorMin = rect.anchorMin;
            settings.anchorMax = rect.anchorMax;
            settings.offsetMin = rect.offsetMin;
            settings.offsetMax = rect.offsetMax;
            if (piece.TryGetComponent<Image>(out var image))
            {
                settings.hasImage = true;
                settings.image = ReadImageSettings(image);
            }

            return settings;
        }

        // A disabled graphic, or one under an inactive object of the panel, is drawn nowhere: Easy UI has no enabled or
        // active state, so its colour goes fully transparent (it still catches clicks if it did).
        private void Hide(Behaviour graphic, ref Color color)
        {
            if (!IsHidden(graphic))
            {
                return;
            }

            color.a = 0f;
            if (!graphic.enabled)
            {
                Problem(DisabledNote, graphic.gameObject);
            }
        }

        // Disabled, or under an inactive object of the panel (the panel's own root and what is above it don't count).
        private bool IsHidden(Behaviour graphic)
        {
            if (!graphic.enabled)
            {
                return true;
            }

            for (var parent = graphic.transform; parent != null && parent != rootTransform; parent = parent.parent)
            {
                if (!parent.gameObject.activeSelf)
                {
                    return true;
                }
            }

            return false;
        }

        private static RawImageSettings ReadRawImage(RawImage image) => new()
        {
            texture = image.texture,
            color = image.color,
            raycastTarget = image.raycastTarget,
            maskable = image.maskable,
            uvRect = image.uvRect
        };

        private static SelectableSettings ReadSelectable(Selectable selectable)
        {
            var triggers = selectable.animationTriggers;
            return new SelectableSettings
            {
                interactable = selectable.interactable,
                transition = selectable.transition,
                colors = selectable.colors,
                spriteState = selectable.spriteState,
                animationTriggers = new AnimationTriggers
                {
                    normalTrigger = triggers.normalTrigger,
                    highlightedTrigger = triggers.highlightedTrigger,
                    pressedTrigger = triggers.pressedTrigger,
                    selectedTrigger = triggers.selectedTrigger,
                    disabledTrigger = triggers.disabledTrigger
                }
            };
        }

        private void ReadScrollView(GameObject go, ScrollRect scroll, ScrollViewSettings settings)
        {
            // Unity's Scroll View background is its root Image: shown while it exists, is enabled and isn't invisible.
            var shown = go.TryGetComponent<Image>(out var back) && !IsHidden(back) && back.color.a > 0.01f;
            settings.background = shown;
            if (shown && (back.sprite == null || back.sprite.name != "Background" || back.color.a > 0.45f))
            {
                Problem(ScrollBackgroundNote, go);
            }

            settings.horizontal = scroll.horizontal;
            settings.vertical = scroll.vertical;
            settings.movementType = (ScrollMovementType)(int)scroll.movementType;
            settings.elasticity = scroll.elasticity;
            settings.inertia = scroll.inertia;
            settings.decelerationRate = scroll.decelerationRate;
            settings.scrollSensitivity = scroll.scrollSensitivity;

            settings.horizontalScrollbar = scroll.horizontalScrollbar != null;
            settings.horizontalScrollbarVisibility = (ScrollbarVisibility)(int)scroll.horizontalScrollbarVisibility;
            settings.horizontalScrollbarSpacing = scroll.horizontalScrollbarSpacing;
            settings.verticalScrollbar = scroll.verticalScrollbar != null;
            settings.verticalScrollbarVisibility = (ScrollbarVisibility)(int)scroll.verticalScrollbarVisibility;
            settings.verticalScrollbarSpacing = scroll.verticalScrollbarSpacing;
        }

        #endregion

        #region Optional components

        // A disabled component does nothing in the scene, so it is not read (and the object keeps its own look).
        private bool Enabled(Behaviour component, GameObject go)
        {
            if (component.enabled)
            {
                return true;
            }

            Problem(DisabledComponentNote, go);
            return false;
        }

        private void ReadComponents(GameObject go, EasyUINode node)
        {
            if (go.TryGetComponent<ContentSizeFitter>(out var fitter) && Enabled(fitter, go))
            {
                node.contentSizeFitter.enabled = true;
                node.contentSizeFitter.horizontalFit = (FitMode)(int)fitter.horizontalFit;
                node.contentSizeFitter.verticalFit = (FitMode)(int)fitter.verticalFit;
            }

            if (go.TryGetComponent<CanvasGroup>(out var group) && Enabled(group, go))
            {
                node.canvasGroup.enabled = true;
                node.canvasGroup.alpha = group.alpha;
                node.canvasGroup.interactable = group.interactable;
                node.canvasGroup.blocksRaycasts = group.blocksRaycasts;
                node.canvasGroup.ignoreParentGroups = group.ignoreParentGroups;
            }

            if (go.TryGetComponent<LayoutGroup>(out var layoutGroup) && Enabled(layoutGroup, go))
            {
                ReadLayoutGroup(go, layoutGroup, node.layoutGroup);
            }

            if (go.TryGetComponent<LayoutElement>(out var element) && Enabled(element, go))
            {
                var settings = node.layoutElement;
                settings.enabled = true;
                settings.ignoreLayout = element.ignoreLayout;
                settings.useMinWidth = element.minWidth >= 0f;
                settings.minWidth = Mathf.Max(0f, element.minWidth);
                settings.useMinHeight = element.minHeight >= 0f;
                settings.minHeight = Mathf.Max(0f, element.minHeight);
                settings.usePreferredWidth = element.preferredWidth >= 0f;
                settings.preferredWidth = Mathf.Max(0f, element.preferredWidth);
                settings.usePreferredHeight = element.preferredHeight >= 0f;
                settings.preferredHeight = Mathf.Max(0f, element.preferredHeight);
                settings.useFlexibleWidth = element.flexibleWidth >= 0f;
                settings.flexibleWidth = element.flexibleWidth >= 0f ? element.flexibleWidth : settings.flexibleWidth;
                settings.useFlexibleHeight = element.flexibleHeight >= 0f;
                settings.flexibleHeight = element.flexibleHeight >= 0f ? element.flexibleHeight : settings.flexibleHeight;
                settings.layoutPriority = element.layoutPriority;
            }

            if (go.TryGetComponent<Mask>(out var mask) && Enabled(mask, go))
            {
                node.mask.enabled = true;
                node.mask.showMaskGraphic = mask.showMaskGraphic;
            }

            if (go.TryGetComponent<RectMask2D>(out var rectMask) && Enabled(rectMask, go))
            {
                node.rectMask2D.enabled = true;
                node.rectMask2D.padding = rectMask.padding;
                node.rectMask2D.softness = rectMask.softness;
            }
        }

        private void ReadLayoutGroup(GameObject go, LayoutGroup group, LayoutGroupSettings settings)
        {
            switch (group)
            {
                case GridLayoutGroup grid:
                    settings.kind = LayoutKind.Grid;
                    settings.cellSize = grid.cellSize;
                    settings.gridSpacing = grid.spacing;
                    settings.startCorner = (GridStartCorner)(int)grid.startCorner;
                    settings.startAxis = (GridStartAxis)(int)grid.startAxis;
                    settings.constraint = (GridConstraint)(int)grid.constraint;
                    settings.constraintCount = grid.constraintCount;
                    break;

                case HorizontalOrVerticalLayoutGroup line:
                    settings.kind = line is HorizontalLayoutGroup ? LayoutKind.Horizontal : LayoutKind.Vertical;
                    settings.spacing = line.spacing;
                    settings.reverseArrangement = line.reverseArrangement;
                    settings.controlChildWidth = line.childControlWidth;
                    settings.controlChildHeight = line.childControlHeight;
                    settings.useChildScaleWidth = line.childScaleWidth;
                    settings.useChildScaleHeight = line.childScaleHeight;
                    settings.childForceExpandWidth = line.childForceExpandWidth;
                    settings.childForceExpandHeight = line.childForceExpandHeight;
                    break;

                default:
                    Problem(LayoutGroupNote, go);
                    return;
            }

            settings.enabled = true;
            settings.paddingLeft = group.padding.left;
            settings.paddingRight = group.padding.right;
            settings.paddingTop = group.padding.top;
            settings.paddingBottom = group.padding.bottom;
            settings.childAlignment = group.childAlignment;
        }

        #endregion

        #region Roles

        // A role is read back from the component it adds - but only a component that belongs to one role alone, since
        // otherwise there is no telling which of them the object was meant to have.
        private void CollectRoles()
        {
            var counts = new Dictionary<Type, int>();
            foreach (var role in EasyUIRoles.All)
            {
                if (role.Component != null)
                {
                    counts[role.Component] = counts.TryGetValue(role.Component, out var count) ? count + 1 : 1;
                }
            }

            foreach (var role in EasyUIRoles.All)
            {
                if (role.Component != null && counts[role.Component] == 1)
                {
                    roleByComponent.Add(role.Component, role);
                }
            }
        }

        private void ReadRoles(GameObject go, EasyUINode node)
        {
            foreach (var pair in roleByComponent)
            {
                var role = pair.Value;
                if (go.GetComponent(pair.Key) == null || !role.Fits(node.type) || node.HasRole(role.Id) ||
                    EasyUIRoles.WhyNot(node, role) != null || (role.Unique && EasyUIRoles.FindNode(document, role.Id) != null))
                {
                    continue;
                }

                node.roles.Add(role.Id);
            }
        }

        #endregion

        #region Notes

        // The same problem on many objects is one line: what it is, then the first few objects it is on.
        private void Problem(string text, GameObject go)
        {
            if (!problems.TryGetValue(text, out var objects))
            {
                objects = new List<GameObject>();
                problems.Add(text, objects);
            }

            // Once per object, however many of its anchors or axes it is about.
            if (!objects.Contains(go))
            {
                objects.Add(go);
            }
        }

        private void ListProblems(List<string> notes)
        {
            var line = new StringBuilder();
            foreach (var pair in problems)
            {
                line.Clear();
                line.Append(pair.Key).Append(": ");
                var objects = pair.Value;
                for (var i = 0; i < objects.Count && i < MaxNamesShown; i++)
                {
                    line.Append(i > 0 ? ", " : string.Empty).Append(objects[i].name);
                }

                if (objects.Count > MaxNamesShown)
                {
                    line.Append(" (+").Append(objects.Count - MaxNamesShown).Append(" more)");
                }

                notes.Add(line.ToString());
            }
        }

        #endregion
    }
}
