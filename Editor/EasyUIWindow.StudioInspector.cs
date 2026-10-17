#if HAS_VENEER
using System;
using System.Collections.Generic;
using Veneer;
using TMPro;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using UGUIImage = UnityEngine.UI.Image;
using UGUISelectable = UnityEngine.UI.Selectable;
using UGUISlider = UnityEngine.UI.Slider;
using UGUIToggle = UnityEngine.UI.Toggle;

namespace EasyUI
{
    // The Inspector of the studio window: the selected element's properties as UI Toolkit cards on three tabs.
    //   Element    - Rect Transform (anchor, position, size, pivot), the settings most used for its kind, Roles.
    //   Details    - the rest of the settings of its kind (text style, image type, transition and colours...).
    //   Components - the optional components (Content Size Fitter, Canvas Group, layout groups...) and the add button.
    // Parts and Roles sit with the element's identity. The data is the plain EasyUINode, edited in place exactly as the
    // IMGUI panel did (undo is the window's own history, see EasyUIWindow.History.cs). The panel is built when the
    // selection (or the design object, after undo / open) changes and every control follows the data through a list of
    // small sync actions, run about ten times a second and after each interaction; a control being typed in is skipped.
    internal sealed partial class EasyUIWindow
    {
        private readonly List<Action> inspectorSyncs = new();
        private List<Action> syncTarget;
        private EasyUINode builtNode;
        private int builtId;

        private void ResetInspectorState()
        {
            inspectorSyncs.Clear();
            syncTarget = inspectorSyncs;
            builtNode = null;
            builtId = -1;
        }

        // Rebuilds when the shown element (or the design object it lives in) is no longer the one built; else refreshes values.
        private void SyncInspector()
        {
            if (inspectorHost == null)
            {
                return;
            }

            var node = document.Find(_selectedId);
            if (node != builtNode || _selectedId != builtId)
            {
                RebuildInspector();
                return;
            }

            for (var i = 0; i < inspectorSyncs.Count; i++)
            {
                inspectorSyncs[i]();
            }
        }

        private void AddSync(Action sync) => syncTarget.Add(sync);

        // A control of the Inspector changed the design.
        private void AfterEdit() => Repaint();

        private static bool IsEditing(VisualElement field)
        {
            var focused = field.panel?.focusController?.focusedElement as VisualElement;
            return focused != null && (focused == field || field.Contains(focused));
        }

        private static string ElementIcon(EasyUIElementType type) => type switch
        {
            EasyUIElementType.Text => "text",
            EasyUIElementType.Image => "image",
            EasyUIElementType.RawImage => "image",
            EasyUIElementType.Button => "cursor",
            EasyUIElementType.Toggle => "check-circle",
            EasyUIElementType.Slider => "sliders",
            EasyUIElementType.Dropdown => "chevron-down",
            EasyUIElementType.InputField => "edit",
            EasyUIElementType.ScrollView => "list",
            _ => "cube"
        };

        private void RebuildInspector()
        {
            inspectorSyncs.Clear();
            syncTarget = inspectorSyncs;
            inspectorHost.Clear();

            var node = document.Find(_selectedId);
            builtNode = node;
            builtId = _selectedId;
            if (node == null)
            {
                inspectorHost.Add(VeneerStyle.EmptyState("cursor", "Nothing selected",
                    "Click an element in the workspace or the hierarchy to edit its Rect Transform and components here.", Brand));
                return;
            }

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("eu-inspector__scroll");
            inspectorHost.Add(scroll);

            BuildHeader(scroll, node);
            BuildPartsRow(scroll, node);

            var view = VeneerStyle.TabView("EasyUI.InspectorTab", new[] { "Element", "Details", "Components" },
                new[] { "move", "sliders", "layers" },
                new[] { "Rect Transform, the main settings and roles", "The rest of this element's settings", "Optional components" },
                Brand, out var pages, out _);
            scroll.Add(view);

            BuildRectCard(pages[0], node);
            BuildLook(pages[0], pages[1], node);
            BuildRolesCard(pages[0], node);
            BuildComponents(pages[2], node);
        }

        #region Header, parts, roles

        private void BuildHeader(VisualElement parent, EasyUINode node)
        {
            var head = new VisualElement();
            head.AddToClassList("eu-head");
            head.Add(VeneerStyle.IconElement(ElementIcon(node.type), "vn-icon--accent", Brand));
            var text = new VisualElement();
            text.AddToClassList("eu-head__text");
            var title = VeneerStyle.Title(NameOf(node));
            title.AddToClassList("vn-ellipsis");
            var subtitle = VeneerStyle.Subtitle(Subtitle(node));
            text.Add(title);
            text.Add(subtitle);
            head.Add(text);
            var many = VeneerStyle.Pill(string.Empty, "info");
            VeneerStyle.SetDisplay(many, false);
            head.Add(many);
            parent.Add(head);

            AddSync(() =>
            {
                var name = NameOf(node);
                if (title.text != name)
                {
                    title.text = name;
                }

                var sub = Subtitle(node);
                if (subtitle.text != sub)
                {
                    subtitle.text = sub;
                }

                VeneerStyle.SetDisplay(many, _selected.Count > 1);
                if (_selected.Count > 1)
                {
                    VeneerStyle.SetPill(many, "info", _selected.Count + " selected");
                }
            });
        }

        // On a composite element or one of its parts: a menu of the element and every part of it.
        private void BuildPartsRow(VisualElement parent, EasyUINode node)
        {
            var owner = EasyUIParts.OwnerOf(document, node);
            if (owner == null || !EasyUIParts.IsComposite(owner.type))
            {
                return;
            }

            Button button = null;
            button = VeneerStyle.Button(node.IsPart ? EasyUIParts.DisplayName(node.part) : "Select a part", "layers",
                "Select a part of this element. Clicking the selected element again also goes one part deeper.", () =>
                {
                    var menu = new GenericMenu();
                    var ownerId = owner.id;
                    menu.AddItem(new GUIContent($"{NameOf(owner)} (the element)"), node == owner, () => SelectFromPanel(ownerId));
                    menu.AddSeparator(string.Empty);
                    AddPartItems(menu, owner, string.Empty, node);
                    menu.DropDown(button.worldBound);
                }, false, Brand);
            parent.Add(VeneerStyle.FieldRow("Parts", button, "Select a part of this element.", out _));
        }

        private void BuildRolesCard(VisualElement page, EasyUINode node)
        {
            var card = NewCard(page, "Roles", "tag");
            var list = new VisualElement();
            card.Add(list);
            DynamicRegion(list, () => string.Join("|", node.roles), (host, local) =>
            {
                foreach (var roleId in node.roles)
                {
                    var role = EasyUIRoles.Find(roleId);
                    var id = roleId;
                    var line = new VisualElement();
                    line.AddToClassList("eu-role");
                    var name = new Label(role != null ? role.MenuPath : $"Missing ({roleId})");
                    name.AddToClassList("eu-role__name");
                    name.tooltip = role != null ? role.Description : "Its system (or script) is no longer in the project.";
                    line.Add(name);
                    line.Add(VeneerStyle.IconButton("close", "Take this role off", () => RemoveRole(node, id), Brand));
                    host.Add(line);
                    if (role != null && !string.IsNullOrEmpty(role.Description))
                    {
                        // The panel has a fixed width: the description wraps and the card grows downwards.
                        Label description = VeneerStyle.MutedLabel(role.Description);
                        description.style.whiteSpace = WhiteSpace.Normal;
                        description.style.flexShrink = 0f;
                        host.Add(description);
                    }
                }
            });

            Button add = null;
            add = VeneerStyle.Button("Add role", "plus",
                "What this element is to other systems, e.g. the inventory's slot template. It can have several.", () =>
                {
                    var wb = add.worldBound;
                    var screen = new Rect(position.x + wb.x, position.y + wb.y, wb.width, wb.height);
                    BuildRoleMenu(node, screen).DropDown(wb);
                }, false, Brand);
            add.style.alignSelf = Align.FlexStart;
            card.Add(add);
        }

        #endregion

        #region Building helpers

        private VisualElement NewCard(VisualElement parent, string title, string icon)
        {
            var card = VeneerStyle.Card(title, icon, Brand);
            parent.Add(card);
            return card;
        }

        private void Show(VisualElement element, Func<bool> condition)
        {
            VeneerStyle.SetDisplay(element, condition());
            AddSync(() => VeneerStyle.SetDisplay(element, condition()));
        }

        // A part of the panel whose content depends on data that changes its shape (the roles, the options): rebuilt when
        // `signature` changes; its own controls sync through a list of their own.
        private void DynamicRegion(VisualElement host, Func<string> signature, Action<VisualElement, List<Action>> build)
        {
            var local = new List<Action>();
            var shown = string.Empty;

            void Rebuild()
            {
                host.Clear();
                local.Clear();
                shown = signature();
                var previous = syncTarget;
                syncTarget = local;
                build(host, local);
                syncTarget = previous;
            }

            Rebuild();
            AddSync(() =>
            {
                if (signature() != shown)
                {
                    Rebuild();
                    return;
                }

                for (var i = 0; i < local.Count; i++)
                {
                    local[i]();
                }
            });
        }

        // A label + control row bound to data: edits write through `set`; the control follows `get` unless it is being typed in.
        private VisualElement Row<T>(string label, string tip, BaseField<T> field, Func<T> get, Action<T> set, bool reapply = false)
        {
            field.SetValueWithoutNotify(get());
            field.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                AfterEdit();
                if (reapply)
                {
                    var value = get();
                    if (!EqualityComparer<T>.Default.Equals(value, field.value))
                    {
                        field.SetValueWithoutNotify(value);
                    }
                }
            });
            AddSync(() =>
            {
                if (IsEditing(field))
                {
                    return;
                }

                var value = get();
                if (!EqualityComparer<T>.Default.Equals(value, field.value))
                {
                    field.SetValueWithoutNotify(value);
                }
            });
            return VeneerStyle.FieldRow(label, field, tip, out _);
        }

        private VisualElement FloatRow(string label, string tip, Func<float> get, Action<float> set, string unit = null)
        {
            var field = new FloatField { isDelayed = true };
            field.AddToClassList("vn-number");
            var row = Row(label, tip, field, get, set, true);
            AddUnit(row, unit);
            return row;
        }

        private VisualElement IntRow(string label, string tip, Func<int> get, Action<int> set, string unit = null)
        {
            var field = new IntegerField { isDelayed = true };
            field.AddToClassList("vn-number");
            var row = Row(label, tip, field, get, set, true);
            AddUnit(row, unit);
            return row;
        }

        private static void AddUnit(VisualElement row, string unit)
        {
            if (string.IsNullOrEmpty(unit))
            {
                return;
            }

            var unitLabel = new Label(unit);
            unitLabel.AddToClassList("vn-unit");
            row.Add(unitLabel);
        }

        private VisualElement TextRow(string label, string tip, Func<string> get, Action<string> set, bool multiline = false)
        {
            var field = new TextField { multiline = multiline };
            if (multiline)
            {
                field.style.minHeight = 54f;
            }

            return Row(label, tip, field, get, set);
        }

        private VisualElement ColorRow(string label, Func<Color> get, Action<Color> set) =>
            Row(label, null, new ColorField(), get, set);

        private VisualElement ObjectRow<T>(string label, string tip, Func<T> get, Action<T> set) where T : UnityEngine.Object
        {
            var field = new ObjectField { objectType = typeof(T), allowSceneObjects = false };
            return Row<UnityEngine.Object>(label, tip, field, () => get(), value => set(value as T));
        }

        private VisualElement EnumRow<TEnum>(string label, string tip, Func<TEnum> get, Action<TEnum> set, bool flags = false)
            where TEnum : struct, Enum
        {
            BaseField<Enum> field = flags ? new EnumFlagsField((Enum)(object)get()) : new EnumField((Enum)(object)get());
            return Row(label, tip, field, () => (Enum)(object)get(), value => set((TEnum)(object)value));
        }

        private VisualElement SliderRow(string label, string tip, Func<float> low, Func<float> high, Func<float> get, Action<float> set)
        {
            var slider = new Slider(low(), high()) { showInputField = true };
            var row = Row(label, tip, slider, get, set);
            AddSync(() =>
            {
                if (!Mathf.Approximately(slider.lowValue, low()))
                {
                    slider.lowValue = low();
                }

                if (!Mathf.Approximately(slider.highValue, high()))
                {
                    slider.highValue = high();
                }
            });
            return row;
        }

        private VisualElement SegmentedRow(string label, string[] names, Func<int> get, Action<int> set)
        {
            var group = VeneerStyle.SegmentedIcons(new string[names.Length], names, get(), index =>
            {
                set(index);
                AfterEdit();
            }, Brand);
            AddSync(() => VeneerStyle.SetSegmentedSelected(group, get()));
            return VeneerStyle.FieldRow(label, group, null, out _);
        }

        private VisualElement BoolChip(string text, string icon, string tip, Func<bool> get, Action<bool> set)
        {
            var chip = VeneerStyle.Chip(text, icon, get(), on =>
            {
                set(on);
                AfterEdit();
            }, Brand);
            chip.tooltip = tip;
            AddSync(() => VeneerStyle.SetChipOn(chip, get()));
            return chip;
        }

        private static VisualElement ChipRow(params VisualElement[] chips)
        {
            var row = VeneerStyle.Row(true);
            foreach (var chip in chips)
            {
                row.Add(chip);
            }

            return row;
        }

        #endregion

        #region Rect Transform

        private void RectValues(EasyUINode node, out float x1, out float y1, out float x2, out float y2)
        {
            var parent = ParentRect(node);
            var rect = node.Rect;
            var stretchX = node.anchorH == HorizontalAnchor.Stretch;
            var stretchY = node.anchorV == VerticalAnchor.Stretch;
            var anchorX = parent.x + AnchorPresets.Fraction(node.anchorH) * parent.width;
            var anchorUp = AnchorPresets.Fraction(node.anchorV) * parent.height;
            var pivotUp = parent.yMax - (rect.y + rect.height * (1f - node.pivot.y));

            x1 = stretchX ? rect.xMin - parent.xMin : rect.x + rect.width * node.pivot.x - anchorX;
            y1 = stretchY ? rect.yMin - parent.yMin : pivotUp - anchorUp;
            x2 = stretchX ? parent.xMax - rect.xMax : rect.width;
            y2 = stretchY ? parent.yMax - rect.yMax : rect.height;
        }

        // One of the four fields changed: the other three are read from the element, then the rect is worked out as the
        // IMGUI panel did (size settled first, so the pivot stays where Pos X / Pos Y put it).
        private void CommitRect(EasyUINode node, int index, float value)
        {
            RectValues(node, out var x1, out var y1, out var x2, out var y2);
            switch (index)
            {
                case 0:
                    x1 = value;
                    break;
                case 1:
                    y1 = value;
                    break;
                case 2:
                    x2 = value;
                    break;
                default:
                    y2 = value;
                    break;
            }

            var parent = ParentRect(node);
            var stretchX = node.anchorH == HorizontalAnchor.Stretch;
            var stretchY = node.anchorV == VerticalAnchor.Stretch;
            var anchorX = parent.x + AnchorPresets.Fraction(node.anchorH) * parent.width;
            var anchorUp = AnchorPresets.Fraction(node.anchorV) * parent.height;

            float xMin, width, yMin, height;
            if (stretchX)
            {
                xMin = parent.xMin + x1;
                width = Mathf.Max(parent.xMax - x2 - xMin, 0f);
            }
            else
            {
                width = Mathf.Max(x2, 0f);
                xMin = anchorX + x1 - width * node.pivot.x;
            }

            if (stretchY)
            {
                yMin = parent.yMin + y1;
                height = Mathf.Max(parent.yMax - y2 - yMin, 0f);
            }
            else
            {
                height = Mathf.Max(y2, 0f);
                yMin = parent.yMax - (y1 + anchorUp) - height * (1f - node.pivot.y);
            }

            SetRect(node, new Rect(xMin, yMin, width, height));
            AfterEdit();
        }

        private void BuildRectCard(VisualElement page, EasyUINode node)
        {
            var card = NewCard(page, "Rect Transform", "scale");
            if (node.IsPart && EasyUIParts.IsDriven(node.part))
            {
                card.Add(VeneerStyle.Hint("Sized by its Scrollbar at runtime.", "info", Brand));
                return;
            }

            var driven = VeneerStyle.Hint(string.Empty, "info", out Label drivenText, Brand);
            card.Add(driven);
            AddSync(() =>
            {
                var text = DrivenBy(node);
                VeneerStyle.SetDisplay(driven, text != null);
                if (text != null && drivenText.text != text)
                {
                    drivenText.text = text;
                }
            });
            VeneerStyle.SetDisplay(driven, false);

            var block = new VisualElement();
            block.AddToClassList("eu-rect");

            // The anchor preset icon stays IMGUI (it is drawn by AnchorPresets); a click opens the presets popup.
            var anchor = new IMGUIContainer(() => DrawAnchorButton(node));
            anchor.AddToClassList("eu-anchor");
            anchor.tooltip = "Anchor presets (Shift: also set the pivot, Alt: also set the position)";
            block.Add(anchor);

            var grid = new VisualElement();
            grid.AddToClassList("eu-rect__grid");
            var fields = new FloatField[4];
            for (var i = 0; i < 4; i++)
            {
                var index = i;
                var field = new FloatField(string.Empty);
                field.AddToClassList("eu-mini");
                field.RegisterValueChangedCallback(evt => CommitRect(node, index, evt.newValue));
                fields[i] = field;
            }

            var line1 = new VisualElement();
            line1.AddToClassList("eu-rect__line");
            line1.Add(fields[0]);
            line1.Add(fields[1]);
            var line2 = new VisualElement();
            line2.AddToClassList("eu-rect__line");
            line2.Add(fields[2]);
            line2.Add(fields[3]);
            grid.Add(line1);
            grid.Add(line2);
            block.Add(grid);
            card.Add(block);

            void SyncRect()
            {
                var stretchX = node.anchorH == HorizontalAnchor.Stretch;
                var stretchY = node.anchorV == VerticalAnchor.Stretch;
                SetLabel(fields[0], stretchX ? "Left" : "Pos X");
                SetLabel(fields[1], stretchY ? "Top" : "Pos Y");
                SetLabel(fields[2], stretchX ? "Right" : "Width");
                SetLabel(fields[3], stretchY ? "Bottom" : "Height");
                RectValues(node, out var x1, out var y1, out var x2, out var y2);
                SetIfIdle(fields[0], x1);
                SetIfIdle(fields[1], y1);
                SetIfIdle(fields[2], x2);
                SetIfIdle(fields[3], y2);
                anchor.MarkDirtyRepaint();
            }

            SyncRect();
            AddSync(SyncRect);

            // Changing the pivot keeps the rect where it is; only the positions read differently.
            card.Add(Row("Pivot", "Pivot of the element, 0 to 1", new Vector2Field(), () => node.pivot,
                value => node.pivot = new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y)), true));
        }

        private static void SetLabel(FloatField field, string text)
        {
            if (field.label != text)
            {
                field.label = text;
            }
        }

        private static void SetIfIdle(FloatField field, float value)
        {
            if (!IsEditing(field) && !Mathf.Approximately(field.value, value))
            {
                field.SetValueWithoutNotify(value);
            }
        }

        // The preset icon with its axis names; a click opens the presets.
        private void DrawAnchorButton(EasyUINode node)
        {
            var area = new Rect(2f, 2f, 48f, 48f);
            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(area, AnchorButtonColor);
            }

            AnchorPresets.DrawIcon(area, node.anchorH, node.anchorV, false);
            AnchorPresets.DrawOutline(area, AnchorButtonBorderColor);
            GUI.Label(new Rect(area.x, area.y + 1f, area.width, 12f), AnchorPresets.Name(node.anchorH), CenteredMiniLabel);
            GUI.Label(new Rect(area.x, area.yMax - 13f, area.width, 12f), AnchorPresets.Name(node.anchorV), CenteredMiniLabel);

            if (GUI.Button(area, GUIContent.none, GUIStyle.none))
            {
                var id = node.id;
                UnityEditor.PopupWindow.Show(area, new AnchorPresetPopup(node.anchorH, node.anchorV,
                    (horizontal, vertical, setPivot, setPosition) => ApplyAnchorPreset(id, horizontal, vertical, setPivot, setPosition)));
            }
        }

        #endregion

        #region Element settings

        // Front: what is changed most for this kind. Details: everything else of the kind.
        private void BuildLook(VisualElement front, VisualElement details, EasyUINode node)
        {
            switch (node.type)
            {
                case EasyUIElementType.Text:
                    BuildTextFront(NewCard(front, "Text", "text"), node.text);
                    BuildTextDetails(NewCard(details, "Text style", "edit"), node.text);
                    break;

                case EasyUIElementType.Image:
                    BuildImageFront(NewCard(front, "Image", "image"), node.image);
                    BuildImageDetails(NewCard(details, "Image", "image"), node.image);
                    break;

                case EasyUIElementType.RawImage:
                {
                    var raw = node.rawImage;
                    var card = NewCard(front, "Raw Image", "image");
                    card.Add(ObjectRow<Texture>("Texture", null, () => raw.texture, v => raw.texture = v));
                    card.Add(ColorRow("Color", () => raw.color, v => raw.color = v));
                    var more = NewCard(details, "Raw Image", "image");
                    more.Add(Row("UV Rect", null, new RectField(), () => raw.uvRect, v => raw.uvRect = v));
                    more.Add(ChipRow(RaycastChip(() => raw.raycastTarget, v => raw.raycastTarget = v)));
                    break;
                }

                case EasyUIElementType.Button:
                    BuildImageFront(NewCard(front, "Background", "image"), node.image);
                    NewCard(front, "Button", "cursor").Add(ChipRow(InteractableChip(node.selectable)));
                    BuildImageDetails(NewCard(details, "Background", "image"), node.image);
                    BuildTransition(details, node.selectable);
                    break;

                case EasyUIElementType.Toggle:
                {
                    var toggle = node.toggle;
                    var card = NewCard(front, "Toggle", "check-circle");
                    card.Add(ChipRow(BoolChip("Is On", "check", "Whether the toggle starts on", () => toggle.isOn, v => toggle.isOn = v),
                        InteractableChip(node.selectable)));
                    var more = NewCard(details, "Toggle", "check-circle");
                    more.Add(EnumRow("Toggle Transition", null, () => toggle.toggleTransition, v => toggle.toggleTransition = v));
                    BuildTransition(details, node.selectable);
                    break;
                }

                case EasyUIElementType.Slider:
                    BuildSlider(front, details, node);
                    break;

                case EasyUIElementType.Dropdown:
                    BuildDropdown(front, node.dropdown);
                    front.Add(NewCardWith("Dropdown", "chevron-down", ChipRow(InteractableChip(node.selectable))));
                    BuildImageDetails(NewCard(details, "Background", "image"), node.image);
                    BuildTransition(details, node.selectable);
                    break;

                case EasyUIElementType.InputField:
                {
                    var input = node.inputField;
                    var card = NewCard(front, "Input Field", "edit");
                    card.Add(TextRow("Text", null, () => input.text, v => input.text = v));
                    card.Add(TextRow("Placeholder", null, () => input.placeholder, v => input.placeholder = v));
                    card.Add(EnumRow("Content Type", null, () => input.contentType, v => input.contentType = v));
                    card.Add(ChipRow(InteractableChip(node.selectable)));
                    var more = NewCard(details, "Input Field", "edit");
                    more.Add(EnumRow("Line Type", null, () => input.lineType, v => input.lineType = v));
                    more.Add(IntRow("Character Limit", "0: no limit", () => input.characterLimit, v => input.characterLimit = Mathf.Max(0, v)));
                    more.Add(ChipRow(BoolChip("Read Only", "lock", null, () => input.readOnly, v => input.readOnly = v)));
                    BuildImageDetails(NewCard(details, "Background", "image"), node.image);
                    BuildTransition(details, node.selectable);
                    break;
                }

                case EasyUIElementType.ScrollView:
                    BuildScrollView(front, details, node);
                    break;

                default:
                    front.Add(VeneerStyle.Hint("An Empty element is a plain container: it has only a Rect Transform. Add components on the Components tab.",
                        "info", Brand));
                    details.Add(VeneerStyle.Hint("An Empty element has no settings of its own.", "info", Brand));
                    break;
            }
        }

        private VisualElement NewCardWith(string title, string icon, VisualElement content)
        {
            var card = VeneerStyle.Card(title, icon, Brand);
            card.Add(content);
            return card;
        }

        private VisualElement InteractableChip(SelectableSettings selectable) =>
            BoolChip("Interactable", "check", "Whether the control reacts to input", () => selectable.interactable, v => selectable.interactable = v);

        private VisualElement RaycastChip(Func<bool> get, Action<bool> set) =>
            BoolChip("Raycast Target", "target", "Whether the graphic blocks clicks", get, set);

        private void BuildTextFront(VisualElement card, TextSettings text)
        {
            card.Add(TextRow("Text", null, () => text.text, v => text.text = v, true));
            card.Add(FloatRow("Font Size", null, () => text.fontSize, v => text.fontSize = Mathf.Max(0f, v), "pt"));
            card.Add(ChipRow(BoolChip("Auto Size", "fit", "Fit the text to the element", () => text.autoSize, v => text.autoSize = v)));
            var min = FloatRow("Min", null, () => text.fontSizeMin, v => text.fontSizeMin = Mathf.Max(0f, v), "pt");
            var max = FloatRow("Max", null, () => text.fontSizeMax, v => text.fontSizeMax = Mathf.Max(text.fontSizeMin, v), "pt");
            card.Add(min);
            card.Add(max);
            Show(min, () => text.autoSize);
            Show(max, () => text.autoSize);
            card.Add(ColorRow("Color", () => text.color, v => text.color = v));
            card.Add(SegmentedRow("Horizontal", new[] { "Left", "Center", "Right" },
                () => Array.IndexOf(HorizontalAlignments, (HorizontalAlignmentOptions)((int)text.alignment & 0xFF)),
                i => text.alignment = (TextAlignmentOptions)((int)HorizontalAlignments[i] | ((int)text.alignment & 0xFF00))));
            card.Add(SegmentedRow("Vertical", new[] { "Top", "Middle", "Bottom" },
                () => Array.IndexOf(VerticalAlignments, (VerticalAlignmentOptions)((int)text.alignment & 0xFF00)),
                i => text.alignment = (TextAlignmentOptions)((int)VerticalAlignments[i] | ((int)text.alignment & 0xFF))));
        }

        private void BuildTextDetails(VisualElement card, TextSettings text)
        {
            card.Add(ObjectRow<TMP_FontAsset>("Font Asset", "Empty: the default font of TMP Settings", () => text.font, v => text.font = v));
            card.Add(EnumRow("Font Style", null, () => text.fontStyle, v => text.fontStyle = v, true));
            card.Add(EnumRow("Wrapping", null, () => text.wrapping, v => text.wrapping = v));
            card.Add(EnumRow("Overflow", null, () => text.overflow, v => text.overflow = v));
            card.Add(ChipRow(BoolChip("Rich Text", "code", "Parse rich text tags", () => text.richText, v => text.richText = v),
                RaycastChip(() => text.raycastTarget, v => text.raycastTarget = v)));
        }

        private void BuildImageFront(VisualElement card, ImageSettings image)
        {
            card.Add(ObjectRow<Sprite>("Source Image", null, () => image.sprite, v => image.sprite = v));
            card.Add(ColorRow("Color", () => image.color, v => image.color = v));
            card.Add(EnumRow("Image Type", null, () => image.imageType, v => image.imageType = v));
        }

        private void BuildImageDetails(VisualElement card, ImageSettings image)
        {
            card.Add(ChipRow(RaycastChip(() => image.raycastTarget, v => image.raycastTarget = v)));

            var aspect = ChipRow(BoolChip("Preserve Aspect", "fit", null, () => image.preserveAspect, v => image.preserveAspect = v));
            Show(aspect, () => image.imageType == UGUIImage.Type.Simple || image.imageType == UGUIImage.Type.Filled);
            card.Add(aspect);

            var sliced = new VisualElement();
            sliced.Add(ChipRow(BoolChip("Fill Center", "grid", null, () => image.fillCenter, v => image.fillCenter = v)));
            sliced.Add(FloatRow("Pixels Per Unit Multiplier", null, () => image.pixelsPerUnitMultiplier, v => image.pixelsPerUnitMultiplier = Mathf.Max(0.01f, v)));
            Show(sliced, () => image.imageType == UGUIImage.Type.Sliced || image.imageType == UGUIImage.Type.Tiled);
            card.Add(sliced);

            var filled = new VisualElement();
            filled.Add(EnumRow("Fill Method", null, () => image.fillMethod, v => image.fillMethod = v));
            filled.Add(OriginRow(image));
            filled.Add(Row("Fill Amount", null, new Slider(0f, 1f) { showInputField = true }, () => image.fillAmount, v => image.fillAmount = v));
            var clockwise = ChipRow(BoolChip("Clockwise", "rotate", null, () => image.fillClockwise, v => image.fillClockwise = v));
            Show(clockwise, () => image.fillMethod != UGUIImage.FillMethod.Horizontal && image.fillMethod != UGUIImage.FillMethod.Vertical);
            filled.Add(clockwise);
            Show(filled, () => image.imageType == UGUIImage.Type.Filled);
            card.Add(filled);
        }

        private static string[] OriginNames(UGUIImage.FillMethod method) => method switch
        {
            UGUIImage.FillMethod.Horizontal => Enum.GetNames(typeof(UGUIImage.OriginHorizontal)),
            UGUIImage.FillMethod.Vertical => Enum.GetNames(typeof(UGUIImage.OriginVertical)),
            UGUIImage.FillMethod.Radial90 => Enum.GetNames(typeof(UGUIImage.Origin90)),
            UGUIImage.FillMethod.Radial180 => Enum.GetNames(typeof(UGUIImage.Origin180)),
            _ => Enum.GetNames(typeof(UGUIImage.Origin360))
        };

        // The Fill Origin choices belong to the Fill Method.
        private VisualElement OriginRow(ImageSettings image)
        {
            var shown = image.fillMethod;
            var names = OriginNames(shown);
            image.fillOrigin = Mathf.Clamp(image.fillOrigin, 0, names.Length - 1);
            var field = new DropdownField(new List<string>(names), image.fillOrigin);
            field.RegisterValueChangedCallback(_ =>
            {
                image.fillOrigin = Mathf.Max(0, field.index);
                AfterEdit();
            });
            AddSync(() =>
            {
                if (IsEditing(field))
                {
                    return;
                }

                if (image.fillMethod != shown)
                {
                    shown = image.fillMethod;
                    field.choices = new List<string>(OriginNames(shown));
                }

                var count = OriginNames(shown).Length;
                image.fillOrigin = Mathf.Clamp(image.fillOrigin, 0, count - 1);
                field.SetValueWithoutNotify(field.choices is List<string> list && image.fillOrigin < list.Count ? list[image.fillOrigin] : string.Empty);
            });
            return VeneerStyle.FieldRow("Fill Origin", field, null, out _);
        }

        // What every control shares: its Transition - colours, sprites or animation triggers.
        private void BuildTransition(VisualElement page, SelectableSettings s)
        {
            var card = NewCard(page, "Transition", "sliders");
            card.Add(EnumRow("Transition", null, () => s.transition, v => s.transition = v));

            var tint = new VisualElement();
            tint.Add(ColorRow("Normal Color", () => s.colors.normalColor, v => { var c = s.colors; c.normalColor = v; s.colors = c; }));
            tint.Add(ColorRow("Highlighted Color", () => s.colors.highlightedColor, v => { var c = s.colors; c.highlightedColor = v; s.colors = c; }));
            tint.Add(ColorRow("Pressed Color", () => s.colors.pressedColor, v => { var c = s.colors; c.pressedColor = v; s.colors = c; }));
            tint.Add(ColorRow("Selected Color", () => s.colors.selectedColor, v => { var c = s.colors; c.selectedColor = v; s.colors = c; }));
            tint.Add(ColorRow("Disabled Color", () => s.colors.disabledColor, v => { var c = s.colors; c.disabledColor = v; s.colors = c; }));
            tint.Add(Row("Color Multiplier", null, new Slider(1f, 5f) { showInputField = true }, () => s.colors.colorMultiplier,
                v => { var c = s.colors; c.colorMultiplier = v; s.colors = c; }));
            tint.Add(FloatRow("Fade Duration", null, () => s.colors.fadeDuration, v => { var c = s.colors; c.fadeDuration = Mathf.Max(0f, v); s.colors = c; }, "s"));
            Show(tint, () => s.transition == UGUISelectable.Transition.ColorTint);
            card.Add(tint);

            var swap = new VisualElement();
            swap.Add(ObjectRow<Sprite>("Highlighted Sprite", null, () => s.spriteState.highlightedSprite, v => { var t = s.spriteState; t.highlightedSprite = v; s.spriteState = t; }));
            swap.Add(ObjectRow<Sprite>("Pressed Sprite", null, () => s.spriteState.pressedSprite, v => { var t = s.spriteState; t.pressedSprite = v; s.spriteState = t; }));
            swap.Add(ObjectRow<Sprite>("Selected Sprite", null, () => s.spriteState.selectedSprite, v => { var t = s.spriteState; t.selectedSprite = v; s.spriteState = t; }));
            swap.Add(ObjectRow<Sprite>("Disabled Sprite", null, () => s.spriteState.disabledSprite, v => { var t = s.spriteState; t.disabledSprite = v; s.spriteState = t; }));
            Show(swap, () => s.transition == UGUISelectable.Transition.SpriteSwap);
            card.Add(swap);

            var anim = new VisualElement();
            anim.Add(TextRow("Normal Trigger", null, () => s.animationTriggers.normalTrigger, v => s.animationTriggers.normalTrigger = v));
            anim.Add(TextRow("Highlighted Trigger", null, () => s.animationTriggers.highlightedTrigger, v => s.animationTriggers.highlightedTrigger = v));
            anim.Add(TextRow("Pressed Trigger", null, () => s.animationTriggers.pressedTrigger, v => s.animationTriggers.pressedTrigger = v));
            anim.Add(TextRow("Selected Trigger", null, () => s.animationTriggers.selectedTrigger, v => s.animationTriggers.selectedTrigger = v));
            anim.Add(TextRow("Disabled Trigger", null, () => s.animationTriggers.disabledTrigger, v => s.animationTriggers.disabledTrigger = v));
            Show(anim, () => s.transition == UGUISelectable.Transition.Animation);
            card.Add(anim);
        }

        private void BuildSlider(VisualElement front, VisualElement details, EasyUINode node)
        {
            var slider = node.slider;
            var card = NewCard(front, "Slider", "sliders");
            card.Add(FloatRow("Min Value", null, () => slider.minValue, v => slider.minValue = v));
            card.Add(FloatRow("Max Value", null, () => slider.maxValue, v => slider.maxValue = v));
            card.Add(SliderRow("Value", null, () => Mathf.Min(slider.minValue, slider.maxValue), () => Mathf.Max(slider.minValue, slider.maxValue),
                () => slider.value, v => slider.value = slider.wholeNumbers ? Mathf.Round(v) : v));
            card.Add(ChipRow(InteractableChip(node.selectable)));
            var more = NewCard(details, "Slider", "sliders");
            more.Add(EnumRow("Direction", null, () => slider.direction, v => slider.direction = v));
            more.Add(ChipRow(BoolChip("Whole Numbers", "hash", null, () => slider.wholeNumbers, v => slider.wholeNumbers = v)));
            BuildTransition(details, node.selectable);
        }

        // Its options as a list - each one's text with a remove button, and Add under them - then Value, the option shown first.
        private void BuildDropdown(VisualElement page, DropdownSettings dropdown)
        {
            var card = NewCard(page, "Options", "list");
            var list = new VisualElement();
            card.Add(list);
            DynamicRegion(list, () => dropdown.options.Count.ToString(), (host, local) =>
            {
                for (var i = 0; i < dropdown.options.Count; i++)
                {
                    var index = i;
                    var line = new VisualElement();
                    line.AddToClassList("eu-option");
                    var text = new TextField { value = dropdown.options[index] };
                    text.RegisterValueChangedCallback(evt =>
                    {
                        if (index < dropdown.options.Count)
                        {
                            dropdown.options[index] = evt.newValue;
                            AfterEdit();
                        }
                    });
                    AddSync(() =>
                    {
                        if (index < dropdown.options.Count && !IsEditing(text) && text.value != dropdown.options[index])
                        {
                            text.SetValueWithoutNotify(dropdown.options[index]);
                        }
                    });
                    line.Add(text);
                    line.Add(VeneerStyle.IconButton("minus", "Remove this option", () =>
                    {
                        if (index < dropdown.options.Count)
                        {
                            dropdown.options.RemoveAt(index);
                            AfterEdit();
                        }
                    }, Brand));
                    host.Add(line);
                }
            });
            var add = VeneerStyle.Button("Add option", "plus", "Add an option", () =>
            {
                dropdown.options.Add($"Option {(char)('A' + dropdown.options.Count % 26)}");
                AfterEdit();
            }, false, Brand);
            add.style.alignSelf = Align.FlexStart;
            card.Add(add);

            var value = new DropdownField(new List<string>(dropdown.options), 0);
            value.RegisterValueChangedCallback(_ =>
            {
                dropdown.value = Mathf.Max(0, value.index);
                AfterEdit();
            });
            AddSync(() =>
            {
                if (IsEditing(value))
                {
                    return;
                }

                var current = value.choices as List<string>;
                var differs = current == null || current.Count != dropdown.options.Count;
                for (var i = 0; !differs && i < current.Count; i++)
                {
                    differs = current[i] != dropdown.options[i];
                }

                if (differs)
                {
                    value.choices = new List<string>(dropdown.options);
                }

                if (dropdown.options.Count == 0)
                {
                    value.SetValueWithoutNotify("(no options)");
                    value.SetEnabled(false);
                    return;
                }

                value.SetEnabled(true);
                dropdown.value = Mathf.Clamp(dropdown.value, 0, dropdown.options.Count - 1);
                value.SetValueWithoutNotify(dropdown.options[dropdown.value]);
            });
            card.Add(VeneerStyle.FieldRow("Value", value, "The option shown first", out _));
        }

        private void BuildScrollView(VisualElement front, VisualElement details, EasyUINode node)
        {
            var scroll = node.scrollView;
            var card = NewCard(front, "Scroll View", "list");
            card.Add(ChipRow(BoolChip("Horizontal", "arrow-right", null, () => scroll.horizontal, v => scroll.horizontal = v),
                BoolChip("Vertical", "arrow-down", null, () => scroll.vertical, v => scroll.vertical = v)));
            card.Add(EnumRow("Movement Type", null, () => scroll.movementType, v => scroll.movementType = v));

            var more = NewCard(details, "Scrolling", "move");
            var elasticity = FloatRow("Elasticity", null, () => scroll.elasticity, v => scroll.elasticity = Mathf.Max(0f, v));
            Show(elasticity, () => scroll.movementType == ScrollMovementType.Elastic);
            more.Add(elasticity);
            more.Add(ChipRow(BoolChip("Inertia", "speed", null, () => scroll.inertia, v => scroll.inertia = v)));
            var deceleration = FloatRow("Deceleration Rate", null, () => scroll.decelerationRate, v => scroll.decelerationRate = Mathf.Max(0f, v));
            Show(deceleration, () => scroll.inertia);
            more.Add(deceleration);
            more.Add(FloatRow("Scroll Sensitivity", null, () => scroll.scrollSensitivity, v => scroll.scrollSensitivity = v));

            var bars = NewCard(details, "Scrollbars", "sliders");
            BuildScrollbar(bars, node, "Horizontal Scrollbar", () => scroll.horizontalScrollbar, v => scroll.horizontalScrollbar = v,
                () => scroll.horizontalScrollbarVisibility, v => scroll.horizontalScrollbarVisibility = v,
                () => scroll.horizontalScrollbarSpacing, v => scroll.horizontalScrollbarSpacing = v);
            BuildScrollbar(bars, node, "Vertical Scrollbar", () => scroll.verticalScrollbar, v => scroll.verticalScrollbar = v,
                () => scroll.verticalScrollbarVisibility, v => scroll.verticalScrollbarVisibility = v,
                () => scroll.verticalScrollbarSpacing, v => scroll.verticalScrollbarSpacing = v);
        }

        // A scrollbar exists (as a part) while ticked; its Visibility and Spacing, as in the Scroll Rect's Inspector.
        private void BuildScrollbar(VisualElement card, EasyUINode node, string label, Func<bool> shown, Action<bool> setShown,
            Func<ScrollbarVisibility> visibility, Action<ScrollbarVisibility> setVisibility, Func<float> spacing, Action<float> setSpacing)
        {
            card.Add(ChipRow(BoolChip(label, "sliders", null, shown, on =>
            {
                setShown(on);
                // Its part comes or goes with it.
                EasyUIParts.EnsureParts(document, node);
                PruneSelection();
            })));
            var group = new VisualElement();
            group.Add(EnumRow("Visibility", null, visibility, setVisibility));
            group.Add(FloatRow("Spacing", null, spacing, setSpacing));
            Show(group, shown);
            card.Add(group);
        }

        #endregion

        #region Components

        private void BuildComponents(VisualElement page, EasyUINode node)
        {
            var fitter = node.contentSizeFitter;
            var fitterCard = ComponentCard(page, "Content Size Fitter", "fit", () => fitter.enabled, () => fitter.enabled = false);
            fitterCard.Add(EnumRow("Horizontal Fit", null, () => fitter.horizontalFit, v => fitter.horizontalFit = v));
            fitterCard.Add(EnumRow("Vertical Fit", null, () => fitter.verticalFit, v => fitter.verticalFit = v));

            var group = node.canvasGroup;
            var groupCard = ComponentCard(page, "Canvas Group", "layers", () => group.enabled, () => group.enabled = false);
            groupCard.Add(Row("Alpha", null, new Slider(0f, 1f) { showInputField = true }, () => group.alpha, v => group.alpha = v));
            groupCard.Add(ChipRow(BoolChip("Interactable", "check", null, () => group.interactable, v => group.interactable = v),
                BoolChip("Blocks Raycasts", "target", null, () => group.blocksRaycasts, v => group.blocksRaycasts = v),
                BoolChip("Ignore Parent Groups", "unlink", null, () => group.ignoreParentGroups, v => group.ignoreParentGroups = v)));

            BuildLayoutGroupCard(page, node.layoutGroup);

            var element = node.layoutElement;
            var elementCard = ComponentCard(page, "Layout Element", "grid", () => element.enabled, () => element.enabled = false);
            elementCard.Add(ChipRow(BoolChip("Ignore Layout", "unlink", null, () => element.ignoreLayout, v => element.ignoreLayout = v)));
            var sizes = new VisualElement();
            sizes.Add(OptionalFloat("Min Width", () => element.useMinWidth, v => element.useMinWidth = v, () => element.minWidth, v => element.minWidth = v));
            sizes.Add(OptionalFloat("Min Height", () => element.useMinHeight, v => element.useMinHeight = v, () => element.minHeight, v => element.minHeight = v));
            sizes.Add(OptionalFloat("Preferred Width", () => element.usePreferredWidth, v => element.usePreferredWidth = v, () => element.preferredWidth, v => element.preferredWidth = v));
            sizes.Add(OptionalFloat("Preferred Height", () => element.usePreferredHeight, v => element.usePreferredHeight = v, () => element.preferredHeight, v => element.preferredHeight = v));
            sizes.Add(OptionalFloat("Flexible Width", () => element.useFlexibleWidth, v => element.useFlexibleWidth = v, () => element.flexibleWidth, v => element.flexibleWidth = v));
            sizes.Add(OptionalFloat("Flexible Height", () => element.useFlexibleHeight, v => element.useFlexibleHeight = v, () => element.flexibleHeight, v => element.flexibleHeight = v));
            AddSync(() => sizes.SetEnabled(!element.ignoreLayout));
            elementCard.Add(sizes);
            elementCard.Add(IntRow("Layout Priority", null, () => element.layoutPriority, v => element.layoutPriority = v));

            var mask = node.mask;
            var maskCard = ComponentCard(page, "Mask", "mask", () => mask.enabled, () => mask.enabled = false);
            maskCard.Add(ChipRow(BoolChip("Show Mask Graphic", "eye", null, () => mask.showMaskGraphic, v => mask.showMaskGraphic = v)));

            var rect = node.rectMask2D;
            var rectCard = ComponentCard(page, "Rect Mask 2D", "mask", () => rect.enabled, () => rect.enabled = false);
            // Padding is kept as uGUI does: x left, y bottom, z right, w top.
            rectCard.Add(FloatRow("Padding Left", null, () => rect.padding.x, v => rect.padding.x = v));
            rectCard.Add(FloatRow("Padding Right", null, () => rect.padding.z, v => rect.padding.z = v));
            rectCard.Add(FloatRow("Padding Top", null, () => rect.padding.w, v => rect.padding.w = v));
            rectCard.Add(FloatRow("Padding Bottom", null, () => rect.padding.y, v => rect.padding.y = v));
            rectCard.Add(Row("Softness", null, new Vector2IntField(), () => rect.softness, v => rect.softness = Vector2Int.Max(v, Vector2Int.zero), true));

            Button add = null;
            add = VeneerStyle.Button("Add component", "plus", "Add a component", () => BuildAddComponentMenu(node).DropDown(add.worldBound), true, Brand);
            add.style.alignSelf = Align.FlexStart;
            page.Add(add);

            var none = VeneerStyle.Hint("No components yet. Add a Content Size Fitter, Canvas Group, layout group, Layout Element or a mask.", "info", Brand);
            page.Add(none);
            AddSync(() => VeneerStyle.SetDisplay(none, !fitter.enabled && !group.enabled && !node.layoutGroup.enabled && !element.enabled
                                                       && !mask.enabled && !rect.enabled));
        }

        // A card for an optional component: shown while it is added, with a cross in its title row that removes it.
        private VisualElement ComponentCard(VisualElement page, string title, string icon, Func<bool> added, Action remove)
        {
            var card = NewCard(page, title, icon);
            var titleRow = card[0];
            titleRow.Add(VeneerStyle.Spacer());
            titleRow.Add(VeneerStyle.IconButton("close", "Remove this component", () =>
            {
                remove();
                AfterEdit();
            }, Brand));
            Show(card, added);
            return card;
        }

        private void BuildLayoutGroupCard(VisualElement page, LayoutGroupSettings layout)
        {
            var card = ComponentCard(page, $"{layout.kind} Layout Group", "grid", () => layout.enabled, () => layout.enabled = false);
            var titleLabel = card[0].Q<Label>(className: "vn-card__title-label");
            if (titleLabel != null)
            {
                AddSync(() =>
                {
                    var text = $"{layout.kind} Layout Group".ToUpperInvariant();
                    if (titleLabel.text != text)
                    {
                        titleLabel.text = text;
                    }
                });
            }

            card.Add(IntRow("Padding Left", null, () => layout.paddingLeft, v => layout.paddingLeft = v));
            card.Add(IntRow("Padding Right", null, () => layout.paddingRight, v => layout.paddingRight = v));
            card.Add(IntRow("Padding Top", null, () => layout.paddingTop, v => layout.paddingTop = v));
            card.Add(IntRow("Padding Bottom", null, () => layout.paddingBottom, v => layout.paddingBottom = v));
            card.Add(EnumRow("Child Alignment", null, () => layout.childAlignment, v => layout.childAlignment = v));

            var grid = new VisualElement();
            grid.Add(Row("Cell Size", null, new Vector2Field(), () => layout.cellSize, v => layout.cellSize = Vector2.Max(v, Vector2.zero), true));
            grid.Add(Row("Spacing", null, new Vector2Field(), () => layout.gridSpacing, v => layout.gridSpacing = v));
            grid.Add(EnumRow("Start Corner", null, () => layout.startCorner, v => layout.startCorner = v));
            grid.Add(EnumRow("Start Axis", null, () => layout.startAxis, v => layout.startAxis = v));
            grid.Add(EnumRow("Constraint", null, () => layout.constraint, v => layout.constraint = v));
            var count = IntRow("Constraint Count", null, () => layout.constraintCount, v => layout.constraintCount = Mathf.Max(1, v));
            Show(count, () => layout.constraint != GridConstraint.Flexible);
            grid.Add(count);
            Show(grid, () => layout.kind == LayoutKind.Grid);
            card.Add(grid);

            var linear = new VisualElement();
            linear.Add(FloatRow("Spacing", null, () => layout.spacing, v => layout.spacing = v));
            linear.Add(ChipRow(BoolChip("Reverse Arrangement", "refresh", null, () => layout.reverseArrangement, v => layout.reverseArrangement = v)));
            linear.Add(ChipRow(BoolChip("Control Child Width", "move", null, () => layout.controlChildWidth, v => layout.controlChildWidth = v),
                BoolChip("Control Child Height", "move", null, () => layout.controlChildHeight, v => layout.controlChildHeight = v)));
            linear.Add(ChipRow(BoolChip("Use Child Scale Width", "scale", null, () => layout.useChildScaleWidth, v => layout.useChildScaleWidth = v),
                BoolChip("Use Child Scale Height", "scale", null, () => layout.useChildScaleHeight, v => layout.useChildScaleHeight = v)));
            linear.Add(ChipRow(BoolChip("Force Expand Width", "expand", null, () => layout.childForceExpandWidth, v => layout.childForceExpandWidth = v),
                BoolChip("Force Expand Height", "expand", null, () => layout.childForceExpandHeight, v => layout.childForceExpandHeight = v)));
            Show(linear, () => layout.kind != LayoutKind.Grid);
            card.Add(linear);
        }

        // A size used only while its box is ticked, as in a Layout Element's Inspector.
        private VisualElement OptionalFloat(string label, Func<bool> use, Action<bool> setUse, Func<float> get, Action<float> set)
        {
            var field = new FloatField { isDelayed = true };
            field.AddToClassList("vn-number");
            var row = Row(label, null, field, get, set, true);
            var toggle = new Toggle { value = use() };
            toggle.RegisterValueChangedCallback(evt =>
            {
                setUse(evt.newValue);
                field.SetEnabled(evt.newValue);
                AfterEdit();
            });
            field.SetEnabled(use());
            AddSync(() =>
            {
                if (toggle.value != use())
                {
                    toggle.SetValueWithoutNotify(use());
                }

                field.SetEnabled(use());
            });
            row.Insert(1, toggle);
            return row;
        }

        // The menu of the components not added yet (one layout group at most).
        private GenericMenu BuildAddComponentMenu(EasyUINode node)
        {
            var menu = new GenericMenu();
            AddComponentItem(menu, "Content Size Fitter", node.contentSizeFitter.enabled, () =>
            {
                node.contentSizeFitter.enabled = true;
                node.contentSizeFitter.open = true;
            });
            AddComponentItem(menu, "Canvas Group", node.canvasGroup.enabled, () =>
            {
                node.canvasGroup.enabled = true;
                node.canvasGroup.open = true;
            });

            menu.AddSeparator(string.Empty);
            foreach (LayoutKind kind in Enum.GetValues(typeof(LayoutKind)))
            {
                var chosen = kind;
                AddComponentItem(menu, $"{kind} Layout Group", node.layoutGroup.enabled, () =>
                {
                    node.layoutGroup.enabled = true;
                    node.layoutGroup.open = true;
                    node.layoutGroup.kind = chosen;
                });
            }

            menu.AddSeparator(string.Empty);
            AddComponentItem(menu, "Layout Element", node.layoutElement.enabled, () =>
            {
                node.layoutElement.enabled = true;
                node.layoutElement.open = true;
            });

            menu.AddSeparator(string.Empty);
            AddComponentItem(menu, "Mask", node.mask.enabled, () =>
            {
                node.mask.enabled = true;
                node.mask.open = true;
            });
            AddComponentItem(menu, "Rect Mask 2D", node.rectMask2D.enabled, () =>
            {
                node.rectMask2D.enabled = true;
                node.rectMask2D.open = true;
            });
            return menu;
        }

        #endregion
    }
}
#endif
