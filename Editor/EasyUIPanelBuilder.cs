using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace EasyUI
{
    /// <summary>
    /// Builds a saved panel as plain uGUI objects: what <c>GameObject &gt; UI (Canvas) &gt; Easy UI &gt; &lt;Panel&gt;</c>
    /// does (see <see cref="EasyUIMenuGenerator"/>).
    /// </summary>
    /// <remarks>
    /// The panel becomes an object stretched over its parent - the selected object when it is inside a canvas,
    /// otherwise the scene's canvas, made (with an EventSystem) if there is none - with every element under it.
    /// Each element is made the way Unity's own <c>GameObject &gt; UI (Canvas)</c> menu makes its kind, then set up
    /// as it was designed: anchors, pivot, the settings of its components and the components added to it. The
    /// objects keep no link to the panel asset; from then on they are the scene's own.
    /// </remarks>
    internal static class EasyUIPanelBuilder
    {
        private const string SkinPath = "UI/Skin/";

        // For panels saved before the canvas's size was kept, when the parent has no size either.
        private static readonly Vector2 FallbackCanvasSize = new(1920f, 1080f);

        // The label color of Unity's default controls.
        private static readonly Color LabelColor = new(50f / 255f, 50f / 255f, 50f / 255f, 1f);

        public static void Create(string guid, MenuCommand command)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var panel = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<EasyUIPanel>(path);
            if (panel == null)
            {
                Debug.LogWarning("[EasyUI] This panel's asset no longer exists. The menu is being updated.");
                EasyUIMenuGenerator.Queue();
                return;
            }

            var undoName = "Create " + panel.name;
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(undoName);

            var parent = ParentFor(command.context as GameObject);
            var root = Build(panel.Document, panel.name, parent);
            Undo.RegisterCreatedObjectUndo(root, undoName);
            Selection.activeGameObject = root;
        }

        #region Hierarchy

        private static GameObject Build(EasyUIDocument document, string name, RectTransform parent)
        {
            var designSize = document.canvasSize;
            if (designSize.x <= 0f || designSize.y <= 0f)
            {
                designSize = parent.rect.size;
            }

            if (designSize.x <= 0f || designSize.y <= 0f)
            {
                designSize = FallbackCanvasSize;
            }

            var root = new GameObject(name, typeof(RectTransform));
            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(parent, false);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            GameObjectUtility.EnsureUniqueNameForSibling(root);

            var ui = new ControlSprites();
            BuildChildren(0, rootRect, new Rect(Vector2.zero, designSize), ChildrenByParent(document), ui);

            var layer = parent.gameObject.layer;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }

            return root;
        }

        // Each element under its parent's id, in the order they were added; elements whose parent is gone count
        // as the panel's own (0), as the window draws them.
        private static Dictionary<int, List<EasyUINode>> ChildrenByParent(EasyUIDocument document)
        {
            var ids = new HashSet<int>();
            foreach (var node in document.nodes)
            {
                ids.Add(node.id);
            }

            var children = new Dictionary<int, List<EasyUINode>>();
            foreach (var node in document.nodes)
            {
                var parentId = ids.Contains(node.parentId) ? node.parentId : 0;
                if (!children.TryGetValue(parentId, out var list))
                {
                    list = new List<EasyUINode>();
                    children.Add(parentId, list);
                }

                list.Add(node);
            }

            return children;
        }

        // `designParent` is the parent's rect in canvas units (top-left, y down): what the children's rects are
        // measured against.
        private static void BuildChildren(int parentId, RectTransform into, Rect designParent,
            Dictionary<int, List<EasyUINode>> children, ControlSprites ui)
        {
            if (!children.TryGetValue(parentId, out var list))
            {
                return;
            }

            foreach (var node in list)
            {
                var element = CreateElement(node, ui, out var content);
                var rect = (RectTransform)element.transform;
                rect.SetParent(into, false);
                element.name = string.IsNullOrEmpty(node.name) ? EasyUINode.DefaultName(node.type) : node.name;
                Place(rect, node, designParent);
                AddComponents(node, element, content);

                BuildChildren(node.id, content != null ? content : rect, node.Rect, children, ui);
            }
        }

        // Anchors and pivot as designed, and offsets that put the rect exactly where it was drawn in its parent.
        private static void Place(RectTransform rect, EasyUINode node, Rect parent)
        {
            var stretchX = node.anchorH == HorizontalAnchor.Stretch;
            var stretchY = node.anchorV == VerticalAnchor.Stretch;
            var anchorMin = new Vector2(stretchX ? 0f : AnchorPresets.Fraction(node.anchorH), stretchY ? 0f : AnchorPresets.Fraction(node.anchorV));
            var anchorMax = new Vector2(stretchX ? 1f : anchorMin.x, stretchY ? 1f : anchorMin.y);

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = node.pivot;

            // From the parent's bottom-left corner, y up, as a RectTransform counts.
            var left = node.position.x - parent.xMin;
            var bottom = parent.yMax - (node.position.y + node.size.y);
            rect.offsetMin = new Vector2(left - anchorMin.x * parent.width, bottom - anchorMin.y * parent.height);
            rect.offsetMax = new Vector2(left + node.size.x - anchorMax.x * parent.width, bottom + node.size.y - anchorMax.y * parent.height);
        }

        // The selected object when it is inside a canvas; otherwise the canvas, made if there is none.
        private static RectTransform ParentFor(GameObject selected)
        {
            if (selected == null)
            {
                selected = Selection.activeGameObject;
            }

            if (selected != null && !EditorUtility.IsPersistent(selected) && selected.transform is RectTransform rect &&
                selected.GetComponentInParent<Canvas>(true) != null)
            {
                return rect;
            }

            var canvas = FindCanvas();
            if (canvas == null)
            {
                canvas = CreateCanvas();
            }

            return (RectTransform)canvas.transform;
        }

        // The current stage's first active root canvas, a screen-space one if there is any.
        private static Canvas FindCanvas()
        {
            Canvas worldSpace = null;
            foreach (var canvas in StageUtility.GetCurrentStageHandle().FindComponentsOfType<Canvas>())
            {
                if (!canvas.isRootCanvas || !canvas.isActiveAndEnabled)
                {
                    continue;
                }

                if (canvas.renderMode != RenderMode.WorldSpace)
                {
                    return canvas;
                }

                worldSpace ??= canvas;
            }

            return worldSpace;
        }

        private static Canvas CreateCanvas()
        {
            var go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster))
            {
                layer = Mathf.Max(0, LayerMask.NameToLayer("UI"))
            };

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefabStage != null)
            {
                go.transform.SetParent(prefabStage.prefabContentsRoot.transform, false);
            }
            else
            {
                StageUtility.PlaceGameObjectInCurrentStage(go);
            }

            Undo.RegisterCreatedObjectUndo(go, "Create Canvas");

            if (prefabStage == null)
            {
                CreateEventSystemIfMissing();
            }

            return canvas;
        }

        // With the Input System's UI module when the project uses the Input System, the old one otherwise.
        private static void CreateEventSystemIfMissing()
        {
            if (StageUtility.GetCurrentStageHandle().FindComponentOfType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem));
            Type module = null;
#if ENABLE_INPUT_SYSTEM
            module = Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
#endif
            go.AddComponent(module ?? typeof(StandaloneInputModule));
            StageUtility.PlaceGameObjectInCurrentStage(go);
            Undo.RegisterCreatedObjectUndo(go, "Create EventSystem");
        }

        #endregion

        #region Elements

        // Made as Unity's GameObject > UI (Canvas) menu makes its kind, then set up as designed. `content` is where
        // the element's children go when that isn't the element itself: a Scroll View's Content.
        private static GameObject CreateElement(EasyUINode node, ControlSprites ui, out RectTransform content)
        {
            content = null;
            GameObject go;
            switch (node.type)
            {
                case EasyUIElementType.Text:
                    go = TMP_DefaultControls.CreateText(ui.Tmp);
                    ApplyText(go.GetComponent<TextMeshProUGUI>(), node.text);
                    break;

                case EasyUIElementType.Image:
                    go = DefaultControls.CreateImage(ui.Ugui);
                    ApplyImage(go.GetComponent<Image>(), node.image);
                    break;

                case EasyUIElementType.RawImage:
                    go = DefaultControls.CreateRawImage(ui.Ugui);
                    ApplyRawImage(go.GetComponent<RawImage>(), node.rawImage);
                    break;

                case EasyUIElementType.Button:
                    go = TMP_DefaultControls.CreateButton(ui.Tmp);
                    ApplyImage(go.GetComponent<Image>(), node.image);
                    ApplySelectable(go.GetComponent<Button>(), node.selectable);
                    var buttonLabel = go.GetComponentInChildren<TMP_Text>(true);
                    if (buttonLabel != null)
                    {
                        buttonLabel.text = node.button.label;
                    }

                    break;

                case EasyUIElementType.Toggle:
                    go = DefaultControls.CreateToggle(ui.Ugui);
                    ApplyToggle(go, node);
                    break;

                case EasyUIElementType.Slider:
                    go = DefaultControls.CreateSlider(ui.Ugui);
                    ApplySlider(go.GetComponent<Slider>(), node);
                    break;

                case EasyUIElementType.Dropdown:
                    go = TMP_DefaultControls.CreateDropdown(ui.Tmp);
                    ApplyImage(go.GetComponent<Image>(), node.image);
                    ApplyDropdown(go.GetComponent<TMP_Dropdown>(), node);
                    break;

                case EasyUIElementType.InputField:
                    go = TMP_DefaultControls.CreateInputField(ui.Tmp);
                    ApplyImage(go.GetComponent<Image>(), node.image);
                    ApplyInputField(go.GetComponent<TMP_InputField>(), node);
                    break;

                case EasyUIElementType.ScrollView:
                    go = DefaultControls.CreateScrollView(ui.Ugui);
                    content = ApplyScrollView(go.GetComponent<ScrollRect>(), node);
                    break;

                default:
                    go = new GameObject(EasyUINode.DefaultName(node.type), typeof(RectTransform));
                    break;
            }

            return go;
        }

        private static void ApplyText(TMP_Text text, TextSettings settings)
        {
            text.text = settings.text;
            if (settings.font != null)
            {
                text.font = settings.font;
            }

            text.fontStyle = settings.fontStyle;
            text.fontSize = settings.fontSize;
            text.enableAutoSizing = settings.autoSize;
            text.fontSizeMin = settings.fontSizeMin;
            text.fontSizeMax = settings.fontSizeMax;
            text.color = settings.color;
            text.alignment = settings.alignment;
            text.textWrappingMode = settings.wrapping;
            text.overflowMode = settings.overflow;
            text.richText = settings.richText;
            text.raycastTarget = settings.raycastTarget;
        }

        private static void ApplyImage(Image image, ImageSettings settings)
        {
            image.sprite = settings.sprite;
            image.color = settings.color;
            image.raycastTarget = settings.raycastTarget;
            image.type = settings.imageType;
            image.preserveAspect = settings.preserveAspect;
            image.fillCenter = settings.fillCenter;
            image.pixelsPerUnitMultiplier = settings.pixelsPerUnitMultiplier;
            image.fillMethod = settings.fillMethod;
            image.fillOrigin = settings.fillOrigin;
            image.fillAmount = settings.fillAmount;
            image.fillClockwise = settings.fillClockwise;
        }

        private static void ApplyRawImage(RawImage image, RawImageSettings settings)
        {
            image.texture = settings.texture;
            image.color = settings.color;
            image.raycastTarget = settings.raycastTarget;
            image.uvRect = settings.uvRect;
        }

        private static void ApplySelectable(Selectable selectable, SelectableSettings settings)
        {
            selectable.interactable = settings.interactable;
            selectable.transition = settings.transition;
            selectable.colors = settings.colors;
            selectable.spriteState = settings.spriteState;

            // A copy: the panel asset's own triggers must not be shared with the scene.
            var triggers = settings.animationTriggers;
            selectable.animationTriggers = new AnimationTriggers
            {
                normalTrigger = triggers.normalTrigger,
                highlightedTrigger = triggers.highlightedTrigger,
                pressedTrigger = triggers.pressedTrigger,
                selectedTrigger = triggers.selectedTrigger,
                disabledTrigger = triggers.disabledTrigger
            };
        }

        // Unity's Toggle comes with a legacy Text label; every Easy UI text is TextMeshPro, so it is swapped.
        private static void ApplyToggle(GameObject go, EasyUINode node)
        {
            var toggle = go.GetComponent<Toggle>();
            ApplySelectable(toggle, node.selectable);
            toggle.toggleTransition = node.toggle.toggleTransition;
            toggle.SetIsOnWithoutNotify(node.toggle.isOn);

            var label = go.transform.Find("Label");
            if (label == null)
            {
                return;
            }

            if (label.TryGetComponent<Text>(out var legacy))
            {
                var fontSize = legacy.fontSize;
                Object.DestroyImmediate(legacy);
                var text = label.gameObject.AddComponent<TextMeshProUGUI>();
                text.fontSize = fontSize;
                text.color = LabelColor;
            }

            if (label.TryGetComponent<TMP_Text>(out var tmp))
            {
                tmp.text = node.toggle.label;
            }
        }

        private static void ApplySlider(Slider slider, EasyUINode node)
        {
            var settings = node.slider;
            ApplySelectable(slider, node.selectable);
            slider.SetDirection(settings.direction, true);
            slider.minValue = settings.minValue;
            slider.maxValue = settings.maxValue;
            slider.wholeNumbers = settings.wholeNumbers;
            slider.SetValueWithoutNotify(settings.value);
        }

        private static void ApplyDropdown(TMP_Dropdown dropdown, EasyUINode node)
        {
            var options = node.dropdown.options;
            ApplySelectable(dropdown, node.selectable);
            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(options));
            dropdown.SetValueWithoutNotify(Mathf.Clamp(node.dropdown.value, 0, Mathf.Max(0, options.Count - 1)));
            dropdown.RefreshShownValue();
        }

        private static void ApplyInputField(TMP_InputField input, EasyUINode node)
        {
            var settings = node.inputField;
            ApplySelectable(input, node.selectable);

            // Content Type first: it sets the Line Type of its own kinds, as in the Inspector.
            input.contentType = settings.contentType;
            input.lineType = settings.lineType;
            input.characterLimit = settings.characterLimit;
            input.readOnly = settings.readOnly;
            input.SetTextWithoutNotify(settings.text);
            if (input.placeholder is TMP_Text placeholder)
            {
                placeholder.text = settings.placeholder;
            }
        }

        // Its Scroll Rect's fields, and only the scrollbars ticked. Its Content starts as large as the view, so
        // the elements designed inside the view land where they were drawn. Returns the Content.
        private static RectTransform ApplyScrollView(ScrollRect scroll, EasyUINode node)
        {
            var settings = node.scrollView;
            scroll.horizontal = settings.horizontal;
            scroll.vertical = settings.vertical;
            scroll.movementType = (ScrollRect.MovementType)settings.movementType;
            scroll.elasticity = settings.elasticity;
            scroll.inertia = settings.inertia;
            scroll.decelerationRate = settings.decelerationRate;
            scroll.scrollSensitivity = settings.scrollSensitivity;

            if (settings.horizontalScrollbar)
            {
                scroll.horizontalScrollbarVisibility = (ScrollRect.ScrollbarVisibility)settings.horizontalScrollbarVisibility;
                scroll.horizontalScrollbarSpacing = settings.horizontalScrollbarSpacing;
            }
            else if (scroll.horizontalScrollbar != null)
            {
                Object.DestroyImmediate(scroll.horizontalScrollbar.gameObject);
                scroll.horizontalScrollbar = null;
            }

            if (settings.verticalScrollbar)
            {
                scroll.verticalScrollbarVisibility = (ScrollRect.ScrollbarVisibility)settings.verticalScrollbarVisibility;
                scroll.verticalScrollbarSpacing = settings.verticalScrollbarSpacing;
            }
            else if (scroll.verticalScrollbar != null)
            {
                Object.DestroyImmediate(scroll.verticalScrollbar.gameObject);
                scroll.verticalScrollbar = null;
            }

            var content = scroll.content;
            content.sizeDelta = new Vector2(content.sizeDelta.x, node.size.y);
            return content;
        }

        // The optional components. A Scroll View's Content Size Fitter and Layout Group go on its Content, which
        // is what they size and lay out; everything else goes on the element itself.
        private static void AddComponents(EasyUINode node, GameObject element, RectTransform content)
        {
            var host = content != null ? content.gameObject : element;

            var fitter = node.contentSizeFitter;
            if (fitter.enabled)
            {
                var component = host.AddComponent<ContentSizeFitter>();
                component.horizontalFit = (ContentSizeFitter.FitMode)fitter.horizontalFit;
                component.verticalFit = (ContentSizeFitter.FitMode)fitter.verticalFit;
            }

            var group = node.canvasGroup;
            if (group.enabled)
            {
                var component = element.AddComponent<CanvasGroup>();
                component.alpha = group.alpha;
                component.interactable = group.interactable;
                component.blocksRaycasts = group.blocksRaycasts;
                component.ignoreParentGroups = group.ignoreParentGroups;
            }

            if (node.layoutGroup.enabled)
            {
                AddLayoutGroup(host, node.layoutGroup);
            }

            var layout = node.layoutElement;
            if (layout.enabled)
            {
                var component = element.AddComponent<LayoutElement>();
                component.ignoreLayout = layout.ignoreLayout;
                component.minWidth = layout.useMinWidth ? layout.minWidth : -1f;
                component.minHeight = layout.useMinHeight ? layout.minHeight : -1f;
                component.preferredWidth = layout.usePreferredWidth ? layout.preferredWidth : -1f;
                component.preferredHeight = layout.usePreferredHeight ? layout.preferredHeight : -1f;
                component.flexibleWidth = layout.useFlexibleWidth ? layout.flexibleWidth : -1f;
                component.flexibleHeight = layout.useFlexibleHeight ? layout.flexibleHeight : -1f;
                component.layoutPriority = layout.layoutPriority;
            }
        }

        private static void AddLayoutGroup(GameObject host, LayoutGroupSettings settings)
        {
            LayoutGroup group;
            if (settings.kind == LayoutKind.Grid)
            {
                var grid = host.AddComponent<GridLayoutGroup>();
                if (grid == null)
                {
                    return;
                }

                grid.cellSize = settings.cellSize;
                grid.spacing = settings.gridSpacing;
                grid.startCorner = (GridLayoutGroup.Corner)settings.startCorner;
                grid.startAxis = (GridLayoutGroup.Axis)settings.startAxis;
                grid.constraint = (GridLayoutGroup.Constraint)settings.constraint;
                grid.constraintCount = Mathf.Max(1, settings.constraintCount);
                group = grid;
            }
            else
            {
                HorizontalOrVerticalLayoutGroup line = settings.kind == LayoutKind.Horizontal
                    ? host.AddComponent<HorizontalLayoutGroup>()
                    : host.AddComponent<VerticalLayoutGroup>();
                if (line == null)
                {
                    return;
                }

                line.spacing = settings.spacing;
                line.reverseArrangement = settings.reverseArrangement;
                line.childControlWidth = settings.controlChildWidth;
                line.childControlHeight = settings.controlChildHeight;
                line.childScaleWidth = settings.useChildScaleWidth;
                line.childScaleHeight = settings.useChildScaleHeight;
                line.childForceExpandWidth = settings.childForceExpandWidth;
                line.childForceExpandHeight = settings.childForceExpandHeight;
                group = line;
            }

            group.padding = new RectOffset(settings.paddingLeft, settings.paddingRight, settings.paddingTop, settings.paddingBottom);
            group.childAlignment = settings.childAlignment;
        }

        #endregion

        // The sprites Unity's own controls are made with, loaded once per build, for both uGUI's and
        // TextMeshPro's control factories.
        private sealed class ControlSprites
        {
            public readonly DefaultControls.Resources Ugui;
            public readonly TMP_DefaultControls.Resources Tmp;

            public ControlSprites()
            {
                var standard = Skin("UISprite");
                var background = Skin("Background");
                var inputField = Skin("InputFieldBackground");
                var knob = Skin("Knob");
                var checkmark = Skin("Checkmark");
                var dropdown = Skin("DropdownArrow");
                var mask = Skin("UIMask");

                Ugui = new DefaultControls.Resources
                {
                    standard = standard, background = background, inputField = inputField, knob = knob,
                    checkmark = checkmark, dropdown = dropdown, mask = mask
                };

                Tmp = new TMP_DefaultControls.Resources
                {
                    standard = standard, background = background, inputField = inputField, knob = knob,
                    checkmark = checkmark, dropdown = dropdown, mask = mask
                };
            }

            private static Sprite Skin(string name) => AssetDatabase.GetBuiltinExtraResource<Sprite>(SkinPath + name + ".psd");
        }
    }
}
