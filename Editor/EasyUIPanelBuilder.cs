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
    /// does (see <see cref="EasyUIMenuGenerator"/>), and what other systems call to build a panel and then find
    /// its elements by role (see <see cref="Build"/>).
    /// </summary>
    /// <remarks>
    /// A panel becomes its root element, placed in its parent - the selected object when it is inside a canvas,
    /// otherwise the scene's canvas, made (with an EventSystem) if there is none - as it was drawn on the canvas
    /// (a new root is stretched over all of it), with every other element under it. The root is named after the
    /// panel unless it was named in Easy UI.
    /// Each element is made the way Unity's own <c>GameObject &gt; UI (Canvas)</c> menu makes its kind, then set up
    /// as it was designed: anchors, pivot, the settings of its components and the components added to it. Parts
    /// (a Scroll View's Viewport, a Button's Text...) are the objects Unity made for them, set up the same way. The
    /// objects keep no link to the panel asset; from then on they are the scene's own. Every element gets its roles'
    /// components (see <see cref="EasyUIRole.Component"/>), then every <see cref="IEasyUIBuildHandler"/> sets up
    /// what it knows.
    /// </remarks>
    public static class EasyUIPanelBuilder
    {
        private const string SkinPath = "UI/Skin/";

        internal static void Create(string guid, MenuCommand command)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var panel = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<EasyUIPanel>(path);
            if (panel == null)
            {
                Debug.LogWarning("[EasyUI] This panel's asset no longer exists. The menu is being updated.");
                EasyUIMenuGenerator.Queue();
                return;
            }

            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName("Create " + panel.name);
            Selection.activeGameObject = Create(panel, ParentFor(command.context as GameObject));
        }

        /// <summary>
        /// Builds <paramref name="panel"/> under <paramref name="parent"/> as the Easy UI menu does - its roles'
        /// components added, then every <see cref="IEasyUIBuildHandler"/> run - recorded for undo, and returns its
        /// root object.
        /// </summary>
        public static GameObject Create(EasyUIPanel panel, RectTransform parent)
        {
            var document = Prepare(panel);
            var built = new Dictionary<int, GameObject>();
            var root = Build(document, panel.name, parent, built);

            var build = new EasyUIBuild(panel, document, root, built);
            EasyUIBuildHandlers.Run(build);

            var undoName = "Create " + panel.name;
            Undo.RegisterCreatedObjectUndo(root, undoName);
            foreach (var moved in build.MovedOut)
            {
                Undo.RegisterCreatedObjectUndo(moved, undoName);
            }

            return root;
        }

        /// <summary>
        /// Builds <paramref name="panel"/> under <paramref name="parent"/> and returns its root object, with its roles'
        /// components but without running the <see cref="IEasyUIBuildHandler"/>s. When <paramref name="built"/> is
        /// given, it is filled with every element's object by the element's id - parts included - so a caller can
        /// find elements by role (<see cref="EasyUIRoles.FindNode"/>). Nothing is recorded for undo: the caller
        /// registers the root.
        /// </summary>
        public static GameObject Build(EasyUIPanel panel, RectTransform parent, Dictionary<int, GameObject> built) =>
            Build(Prepare(panel), panel.name, parent, built ?? new Dictionary<int, GameObject>());

        // A copy, so a panel saved by an older Easy UI is brought up to date without the asset changing.
        private static EasyUIDocument Prepare(EasyUIPanel panel)
        {
            var document = panel.Document.Clone();
            var canvas = document.canvasSize.x > 0f && document.canvasSize.y > 0f ? document.canvasSize : EasyUIDocument.FallbackCanvasSize;
            document.Upgrade(canvas);
            EasyUIParts.EnsureParts(document);
            return document;
        }

        /// <summary>
        /// Where a new panel goes: <paramref name="selected"/> (or the selected object) when it is inside a canvas,
        /// otherwise the current scene's canvas - made, with an EventSystem, if there is none.
        /// </summary>
        public static RectTransform ParentFor(GameObject selected)
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

        #region Hierarchy

        private static GameObject Build(EasyUIDocument document, string name, RectTransform parent, Dictionary<int, GameObject> built)
        {
            var designSize = document.canvasSize;
            if (designSize.x <= 0f || designSize.y <= 0f)
            {
                designSize = parent.rect.size;
            }

            if (designSize.x <= 0f || designSize.y <= 0f)
            {
                designSize = EasyUIDocument.FallbackCanvasSize;
            }

            var ui = new ControlSprites();
            var canvasRect = new Rect(Vector2.zero, designSize);
            var children = ChildrenByParent(document);
            var rootNode = document.RootNode;
            var root = BuildElement(rootNode, parent, canvasRect, children, ui, built);
            if (string.IsNullOrEmpty(rootNode.name))
            {
                root.name = name;
            }

            GameObjectUtility.EnsureUniqueNameForSibling(root);

            var layer = parent.gameObject.layer;
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                child.gameObject.layer = layer;
            }

            return root;
        }

        // Each element under its parent's id, in the order they were added; elements whose parent is gone count
        // as the root's, as the window draws them.
        private static Dictionary<int, List<EasyUINode>> ChildrenByParent(EasyUIDocument document)
        {
            var root = document.RootNode;
            var ids = new HashSet<int>();
            foreach (var node in document.nodes)
            {
                ids.Add(node.id);
            }

            var children = new Dictionary<int, List<EasyUINode>>();
            foreach (var node in document.nodes)
            {
                var parentId = ids.Contains(node.parentId) ? node.parentId : 0;
                if (parentId == 0 && node != root)
                {
                    parentId = root.id;
                }

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
        // measured against. A part isn't made: it is the object Unity made for it inside `into`.
        private static void BuildChildren(int parentId, RectTransform into, Rect designParent,
            Dictionary<int, List<EasyUINode>> children, ControlSprites ui, Dictionary<int, GameObject> built)
        {
            if (!children.TryGetValue(parentId, out var list))
            {
                return;
            }

            foreach (var node in list)
            {
                BuildElement(node, into, designParent, children, ui, built);
            }
        }

        // The element in `into`, set up as designed, with everything under it; null for a part Unity didn't make.
        private static GameObject BuildElement(EasyUINode node, RectTransform into, Rect designParent,
            Dictionary<int, List<EasyUINode>> children, ControlSprites ui, Dictionary<int, GameObject> built)
        {
            GameObject element;
            if (node.IsPart)
            {
                element = PartObject(into.gameObject, node.part);
                if (element == null)
                {
                    return null;
                }

                ApplyPart(element, node);
            }
            else
            {
                element = CreateElement(node, ui);
                ((RectTransform)element.transform).SetParent(into, false);
            }

            var rect = (RectTransform)element.transform;
            element.name = string.IsNullOrEmpty(node.name) ? DefaultName(node) : node.name;
            if (!node.IsPart || !EasyUIParts.IsDriven(node.part))
            {
                Place(rect, node, designParent);
            }

            AddComponents(node, element);
            AddRoleComponents(node, element);
            built[node.id] = element;

            BuildChildren(node.id, rect, node.Rect, children, ui, built);
            return element;
        }

        private static string DefaultName(EasyUINode node) =>
            node.IsPart ? EasyUIParts.DefaultName(node.part) : EasyUINode.DefaultName(node.type);

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

        // Made as Unity's GameObject > UI (Canvas) menu makes its kind, then set up as designed. Its parts are set
        // up by their own elements.
        private static GameObject CreateElement(EasyUINode node, ControlSprites ui)
        {
            GameObject go;
            switch (node.type)
            {
                case EasyUIElementType.Text:
                    go = TMP_DefaultControls.CreateText(ui.Tmp);
                    ApplyText(go.GetComponent<TMP_Text>(), node.text);
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
                    ApplyScrollView(go.GetComponent<ScrollRect>(), node);
                    break;

                default:
                    go = new GameObject(EasyUINode.DefaultName(node.type), typeof(RectTransform));
                    break;
            }

            return go;
        }

        // The object Unity made for `part` inside `parent` (the object of the part's parent element), or null.
        private static GameObject PartObject(GameObject parent, EasyUIPart part)
        {
            switch (part)
            {
                case EasyUIPart.Viewport:
                    return ObjectOf(parent.GetComponent<ScrollRect>(), scroll => scroll.viewport);
                case EasyUIPart.Content:
                    return ObjectOf(parent.GetComponentInParent<ScrollRect>(true), scroll => scroll.content);
                case EasyUIPart.ScrollbarHorizontal:
                    return ObjectOf(parent.GetComponent<ScrollRect>(), scroll => scroll.horizontalScrollbar);
                case EasyUIPart.ScrollbarVertical:
                    return ObjectOf(parent.GetComponent<ScrollRect>(), scroll => scroll.verticalScrollbar);
                case EasyUIPart.ScrollbarHandle:
                    return ObjectOf(parent.GetComponent<Scrollbar>(), scrollbar => scrollbar.handleRect);
                case EasyUIPart.ButtonText:
                    return ObjectOf(parent.GetComponentInChildren<TMP_Text>(true), text => text);
                case EasyUIPart.ToggleBackground:
                    return ObjectOf(parent.GetComponent<Toggle>(), toggle => toggle.targetGraphic);
                case EasyUIPart.ToggleCheckmark:
                    return ObjectOf(parent.GetComponentInParent<Toggle>(true), toggle => toggle.graphic);
                case EasyUIPart.DropdownLabel:
                    return ObjectOf(parent.GetComponent<TMP_Dropdown>(), dropdown => dropdown.captionText);
                case EasyUIPart.ToggleLabel:
                case EasyUIPart.DropdownArrow:
                    var child = parent.transform.Find(part == EasyUIPart.ToggleLabel ? "Label" : "Arrow");
                    return child != null ? child.gameObject : null;
                default:
                    return null;
            }
        }

        private static GameObject ObjectOf<T>(T owner, Func<T, Component> pick) where T : Component
        {
            if (owner == null)
            {
                return null;
            }

            var component = pick(owner);
            return component != null ? component.gameObject : null;
        }

        // A part's own component, set up as designed: its Image or its Text.
        private static void ApplyPart(GameObject element, EasyUINode node)
        {
            switch (node.type)
            {
                case EasyUIElementType.Image when element.TryGetComponent<Image>(out var image):
                    ApplyImage(image, node.image);
                    break;

                case EasyUIElementType.Text when element.TryGetComponent<TMP_Text>(out var text):
                    ApplyText(text, node.text);
                    break;
            }
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

        // Unity's Toggle comes with a legacy Text label; every Easy UI text is TextMeshPro, so it is swapped (its
        // Label part then sets it up).
        private static void ApplyToggle(GameObject go, EasyUINode node)
        {
            var toggle = go.GetComponent<Toggle>();
            ApplySelectable(toggle, node.selectable);
            toggle.toggleTransition = node.toggle.toggleTransition;
            toggle.SetIsOnWithoutNotify(node.toggle.isOn);

            var label = go.transform.Find("Label");
            if (label != null && label.TryGetComponent<Text>(out var legacy))
            {
                Object.DestroyImmediate(legacy);
                label.gameObject.AddComponent<TextMeshProUGUI>();
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

        // Its Scroll Rect's fields, and only the scrollbars ticked (the others' parts don't exist).
        private static void ApplyScrollView(ScrollRect scroll, EasyUINode node)
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
        }

        // The optional components, on the element itself. A part may already have one of them from Unity (a
        // Viewport's Mask): it is set up as designed, or removed when the design doesn't have it.
        private static void AddComponents(EasyUINode node, GameObject element)
        {
            var fitter = node.contentSizeFitter;
            if (fitter.enabled)
            {
                var component = GetOrAdd<ContentSizeFitter>(element);
                component.horizontalFit = (ContentSizeFitter.FitMode)fitter.horizontalFit;
                component.verticalFit = (ContentSizeFitter.FitMode)fitter.verticalFit;
            }

            var group = node.canvasGroup;
            if (group.enabled)
            {
                var component = GetOrAdd<CanvasGroup>(element);
                component.alpha = group.alpha;
                component.interactable = group.interactable;
                component.blocksRaycasts = group.blocksRaycasts;
                component.ignoreParentGroups = group.ignoreParentGroups;
            }

            if (node.layoutGroup.enabled)
            {
                AddLayoutGroup(element, node.layoutGroup);
            }

            var layout = node.layoutElement;
            if (layout.enabled)
            {
                var component = GetOrAdd<LayoutElement>(element);
                component.ignoreLayout = layout.ignoreLayout;
                component.minWidth = layout.useMinWidth ? layout.minWidth : -1f;
                component.minHeight = layout.useMinHeight ? layout.minHeight : -1f;
                component.preferredWidth = layout.usePreferredWidth ? layout.preferredWidth : -1f;
                component.preferredHeight = layout.usePreferredHeight ? layout.preferredHeight : -1f;
                component.flexibleWidth = layout.useFlexibleWidth ? layout.flexibleWidth : -1f;
                component.flexibleHeight = layout.useFlexibleHeight ? layout.flexibleHeight : -1f;
                component.layoutPriority = layout.layoutPriority;
            }

            if (node.mask.enabled)
            {
                // A Mask clips to its graphic: an element without one (e.g. Empty) gets a plain Image.
                if (!element.TryGetComponent<Graphic>(out _))
                {
                    element.AddComponent<Image>();
                }

                GetOrAdd<Mask>(element).showMaskGraphic = node.mask.showMaskGraphic;
            }
            else if (element.TryGetComponent<Mask>(out var unwanted))
            {
                Object.DestroyImmediate(unwanted);
            }

            var rectMask = node.rectMask2D;
            if (rectMask.enabled)
            {
                var component = GetOrAdd<RectMask2D>(element);
                component.padding = rectMask.padding;
                component.softness = rectMask.softness;
            }
        }

        // Each role's component: a role of your own (see EasyUICustomRoles.cs) adds its script, a system's role its
        // Component, if it has one. A system's role whose system is gone does nothing.
        private static void AddRoleComponents(EasyUINode node, GameObject element)
        {
            foreach (var roleId in node.roles)
            {
                Type type;
                if (EasyUICustomRoles.IsCustom(roleId))
                {
                    type = EasyUICustomRoles.ComponentOf(roleId);
                    if (type == null)
                    {
                        Debug.LogWarning($"[EasyUI] '{element.name}' has a role whose script is gone or can't be added: {roleId}.", element);
                        continue;
                    }
                }
                else
                {
                    type = EasyUIRoles.Find(roleId)?.Component;
                    if (type == null)
                    {
                        continue;
                    }
                }

                if (element.GetComponent(type) == null)
                {
                    element.AddComponent(type);
                }
            }
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent<T>(out var component) ? component : go.AddComponent<T>();

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
