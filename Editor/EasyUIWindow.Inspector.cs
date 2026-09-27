using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    // The floating panel in the top-right corner, like Shader Graph's Graph Inspector: shown while an element is
    // selected. Every element has a Rect Transform, then the components of its kind (see EasyUIWindow.Elements.cs);
    // the round "+" adds components - Content Size Fitter, Canvas Group, a Horizontal / Vertical / Grid Layout
    // Group (one at most), Layout Element, Mask, Rect Mask 2D - each removed again with the cross in its header. Every section folds
    // away from its header; the panel keeps a fixed largest height and scrolls past it.
    internal sealed partial class EasyUIWindow
    {
        private const float PanelWidth = 300f;
        private const float PanelMargin = 10f;
        private const float PanelMaxHeight = 600f;
        private const float PanelLabelWidth = 130f;
        private const float RectBlockHeight = 70f;
        private const float AnchorBlockWidth = 76f;
        private const float AddButtonSize = 28f;
        private const float RowGap = 3f;
        private const float PivotGap = 10f;
        private const float ToggleColumn = 72f;
        private const float RemoveRoleWidth = 20f;

        private static readonly Color PanelColor = new(0.21f, 0.21f, 0.21f, 0.97f);
        private static readonly Color PanelBorderColor = new(0.09f, 0.09f, 0.09f, 1f);
        private static readonly Color SectionLineColor = new(1f, 1f, 1f, 0.12f);
        private static readonly Color AnchorButtonColor = new(0.17f, 0.17f, 0.17f, 1f);
        private static readonly Color AnchorButtonBorderColor = new(0.1f, 0.1f, 0.1f, 1f);

        // The panel's rect in window pixels at its last draw, so clicks and scrolls on it stay off the workspace;
        // and how tall its contents were, which it sizes to (up to its largest height, then it scrolls).
        private Rect _panelRect;
        private float _panelContentHeight;
        private Vector2 _panelScroll;

        private Texture2D _panelTexture;
        private Texture2D _addButtonTexture;
        private Texture2D _addButtonHoverTexture;
        private GUIStyle _panelStyle;
        private GUIStyle _panelPaddingStyle;
        private GUIStyle _panelTitleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _centeredMiniLabel;
        private GUIStyle _addButtonStyle;

        private GUIStyle PanelStyle
        {
            get
            {
                if (_panelTexture == null)
                {
                    _panelTexture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
                    _panelTexture.SetPixel(0, 0, PanelColor);
                    _panelTexture.Apply();
                    _panelStyle = null;
                }

                return _panelStyle ??= new GUIStyle { normal = { background = _panelTexture } };
            }
        }

        private GUIStyle PanelPaddingStyle => _panelPaddingStyle ??= new GUIStyle { padding = new RectOffset(10, 10, 8, 10) };

        private GUIStyle PanelTitleStyle => _panelTitleStyle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13 };

        private GUIStyle CenteredMiniLabel => _centeredMiniLabel ??= new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };

        private GUIStyle SectionStyle => _sectionStyle ??= new GUIStyle(EditorStyles.foldout) { fontStyle = FontStyle.Bold };

        // A round button with a "+" in it: a filled circle that lightens under the pointer.
        private GUIStyle AddButtonStyle
        {
            get
            {
                if (_addButtonTexture == null || _addButtonHoverTexture == null)
                {
                    _addButtonTexture = CircleTexture(new Color(0.3f, 0.3f, 0.3f, 1f));
                    _addButtonHoverTexture = CircleTexture(new Color(0.38f, 0.38f, 0.38f, 1f));
                    _addButtonStyle = null;
                }

                return _addButtonStyle ??= new GUIStyle
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 18,
                    fontStyle = FontStyle.Bold,
                    normal = { background = _addButtonTexture, textColor = new Color(1f, 1f, 1f, 0.85f) },
                    hover = { background = _addButtonHoverTexture, textColor = Color.white },
                    active = { background = _addButtonTexture, textColor = Color.white }
                };
            }
        }

        private static Texture2D CircleTexture(Color color)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            var center = (size - 1) * 0.5f;
            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var edge = Mathf.Clamp01(size * 0.5f - Vector2.Distance(new Vector2(x, y), new Vector2(center, center)));
                    texture.SetPixel(x, y, new Color(color.r, color.g, color.b, color.a * edge));
                }
            }

            texture.Apply();
            return texture;
        }

        private void DestroyPanelTextures()
        {
            DestroyTexture(ref _panelTexture);
            DestroyTexture(ref _addButtonTexture);
            DestroyTexture(ref _addButtonHoverTexture);
        }

        private bool IsOverPanel(Vector2 pointer) => _panelShown && _panelRect.Contains(pointer);

        private void DrawInspector()
        {
            var node = document.Find(_panelNodeId);
            if (node == null)
            {
                return;
            }

            var maxHeight = Mathf.Max(Mathf.Min(_view.height - PanelMargin * 2f, PanelMaxHeight), 40f);
            var height = _panelContentHeight > 0f ? Mathf.Min(_panelContentHeight, maxHeight) : maxHeight;
            var area = new Rect(_view.xMax - PanelWidth - PanelMargin, _view.y + PanelMargin, PanelWidth, height);

            GUILayout.BeginArea(area, PanelStyle);
            _panelScroll = GUILayout.BeginScrollView(_panelScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar, GUIStyle.none);
            var content = EditorGUILayout.BeginVertical(PanelPaddingStyle);

            var labelWidth = EditorGUIUtility.labelWidth;
            var wideMode = EditorGUIUtility.wideMode;
            EditorGUIUtility.labelWidth = PanelLabelWidth;
            EditorGUIUtility.wideMode = true;

            GUILayout.Label(NameOf(node), PanelTitleStyle);
            GUILayout.Label(Subtitle(node), EditorStyles.miniLabel);
            DrawRoleRow(node);
            DrawPartsRow(node);

            if (Section("Rect Transform", ref node.rectTransformOpen))
            {
                if (node.IsPart && EasyUIParts.IsDriven(node.part))
                {
                    EditorGUILayout.HelpBox("Sized by its Scrollbar at runtime.", MessageType.None);
                }
                else
                {
                    var driven = DrivenBy(node);
                    if (driven != null)
                    {
                        EditorGUILayout.LabelField(driven, EditorStyles.wordWrappedMiniLabel);
                    }

                    DrawRectTransformSection(node);
                }
            }

            DrawElementSections(node);

            DrawComponents(node);
            DrawAddComponentButton(node);

            EditorGUIUtility.labelWidth = labelWidth;
            EditorGUIUtility.wideMode = wideMode;
            EditorGUILayout.EndVertical();

            // The contents changed height (e.g. a component added): size the panel to them on the next draw.
            if (Event.current.type == EventType.Repaint && !Mathf.Approximately(content.height, _panelContentHeight))
            {
                _panelContentHeight = content.height;
                Repaint();
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (Event.current.type == EventType.Repaint)
            {
                DrawBorder(area, PanelBorderColor, 1f);
                _panelRect = area;
            }
        }

        // What sets the element's rect besides you (see EasyUIWindow.Layout.cs), or null: changing it by hand
        // doesn't stick, as in Unity.
        private string DrivenBy(EasyUINode node)
        {
            string text = null;
            var parent = document.Find(node.parentId);
            if (parent != null && parent.layoutGroup.enabled && !(node.layoutElement.enabled && node.layoutElement.ignoreLayout))
            {
                text = $"Placed by {NameOf(parent)}'s {parent.layoutGroup.kind} Layout Group.";
            }

            var fitter = node.contentSizeFitter;
            if (fitter.enabled && (fitter.horizontalFit != FitMode.Unconstrained || fitter.verticalFit != FitMode.Unconstrained))
            {
                text = (text != null ? text + " " : string.Empty) + "Sized by its Content Size Fitter.";
            }

            return text;
        }

        // "Image  -  layer 2", or for a part "Viewport of Scroll View  -  layer 2".
        private string Subtitle(EasyUINode node)
        {
            var kind = node.IsPart
                ? $"{EasyUIParts.DisplayName(node.part)} of {NameOf(EasyUIParts.OwnerOf(document, node))}"
                : EasyUINode.DisplayName(node.type);
            return $"{kind}  -  layer {document.DepthOf(node)}";
        }

        // What the element is to other systems (see EasyUIRoles.cs): its roles, one per line with a cross that takes
        // it off, and Add Role - a menu of the roles that fit its type only (text roles on a Text, image roles on an
        // Image...), and Add Role..., which makes a role of your own from a script. An element can have several
        // roles; one that conflicts with a role it has, or needs one it hasn't, is shown but can't be picked. A
        // unique role is held by one element at most, so picking it here takes it from any other.
        private void DrawRoleRow(EasyUINode node)
        {
            GUILayout.Space(4f);
            var row = EditorGUILayout.GetControlRect();
            var field = EditorGUI.PrefixLabel(row, new GUIContent("Roles", "What this element is to other systems, e.g. the inventory's slot template. It can have several."));
            if (EditorGUI.DropdownButton(field, new GUIContent("Add Role"), FocusType.Passive))
            {
                ShowRoleMenu(node, field);
            }

            foreach (var roleId in node.roles)
            {
                var role = EasyUIRoles.Find(roleId);
                var line = EditorGUILayout.GetControlRect();
                var remove = new Rect(line.xMax - RemoveRoleWidth, line.y, RemoveRoleWidth, line.height);
                var label = new Rect(line.x, line.y, line.width - RemoveRoleWidth - 4f, line.height);
                var text = role != null ? role.MenuPath : $"Missing ({roleId})";
                EditorGUI.LabelField(label, new GUIContent(text, role != null ? role.Description : "Its system (or script) is no longer in the project."),
                    role != null ? EditorStyles.boldLabel : EditorStyles.miniLabel);

                if (GUI.Button(remove, new GUIContent("×", "Take this role off"), EditorStyles.miniButton))
                {
                    RemoveRole(node, roleId);

                    // The list just changed: end this event before the rest of it is laid out.
                    Repaint();
                    GUIUtility.ExitGUI();
                }

                if (role != null && !string.IsNullOrEmpty(role.Description))
                {
                    EditorGUILayout.LabelField(role.Description, EditorStyles.wordWrappedMiniLabel);
                }
            }
        }

        // Add Role... first, then the systems' roles that fit the element, then your own (see EasyUICustomRoles.cs),
        // which fit every element. The roles it has are ticked; picking one of them takes it off.
        private void ShowRoleMenu(EasyUINode node, Rect field)
        {
            var id = node.id;
            var screen = GUIUtility.GUIToScreenRect(field);
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Role..."), false, () => EasyUIAddRoleWindow.Open(screen, roleId => AddRole(id, roleId)));

            foreach (var custom in new[] { false, true })
            {
                var separated = false;
                foreach (var role in EasyUIRoles.All)
                {
                    if (EasyUICustomRoles.IsCustom(role.Id) != custom || !role.Fits(node.type))
                    {
                        continue;
                    }

                    if (!separated)
                    {
                        menu.AddSeparator(string.Empty);
                        separated = true;
                    }

                    var roleId = role.Id;
                    if (node.HasRole(roleId))
                    {
                        menu.AddItem(new GUIContent(role.MenuPath), true, () => RemoveRole(document.Find(id), roleId));
                        continue;
                    }

                    var whyNot = EasyUIRoles.WhyNot(node, role);
                    if (whyNot != null)
                    {
                        menu.AddDisabledItem(new GUIContent($"{role.MenuPath}  ({whyNot})"));
                        continue;
                    }

                    var holder = role.Unique ? EasyUIRoles.FindNode(document, roleId) : null;
                    var title = holder != null ? $"{role.MenuPath}  (on {NameOf(holder)})" : role.MenuPath;
                    menu.AddItem(new GUIContent(title), false, () => AddRole(id, roleId));
                }
            }

            menu.DropDown(field);
        }

        private void AddRole(int id, string roleId)
        {
            var node = document.Find(id);
            var role = EasyUIRoles.Find(roleId);
            if (node == null || node.HasRole(roleId) || (role != null && EasyUIRoles.WhyNot(node, role) != null))
            {
                return;
            }

            if (role != null && role.Unique)
            {
                foreach (var other in document.nodes)
                {
                    if (other != node && other.HasRole(roleId))
                    {
                        RemoveRole(other, roleId);
                    }
                }
            }

            node.roles.Add(roleId);
            Repaint();
        }

        // Takes the role off, and with it every role of the element that needed it (and those that needed them).
        private void RemoveRole(EasyUINode node, string roleId)
        {
            if (node == null || !node.roles.Remove(roleId))
            {
                return;
            }

            for (var i = 0; i < node.roles.Count; i++)
            {
                var role = EasyUIRoles.Find(node.roles[i]);
                if (role != null && MissesRequired(node, role))
                {
                    node.roles.RemoveAt(i);
                    i = -1;
                }
            }

            Repaint();
        }

        private static bool MissesRequired(EasyUINode node, EasyUIRole role)
        {
            foreach (var required in role.Requires)
            {
                if (!node.HasRole(required))
                {
                    return true;
                }
            }

            return false;
        }

        // On a composite element or one of its parts: a menu of the element and every part of it, to select one
        // without clicking down to it.
        private void DrawPartsRow(EasyUINode node)
        {
            var owner = EasyUIParts.OwnerOf(document, node);
            if (!EasyUIParts.IsComposite(owner.type))
            {
                return;
            }

            GUILayout.Space(2f);
            var row = EditorGUILayout.GetControlRect();
            var field = EditorGUI.PrefixLabel(row, new GUIContent("Parts", "Select a part of this element. Clicking the selected element again also goes one part deeper."));
            var shown = node.IsPart ? EasyUIParts.DisplayName(node.part) : "Select a part";
            if (!EditorGUI.DropdownButton(field, new GUIContent(shown), FocusType.Passive))
            {
                return;
            }

            var menu = new GenericMenu();
            var ownerId = owner.id;
            menu.AddItem(new GUIContent($"{NameOf(owner)} (the element)"), node == owner, () => SelectFromPanel(ownerId));
            menu.AddSeparator(string.Empty);
            AddPartItems(menu, owner, string.Empty, node);
            menu.DropDown(field);
        }

        private void AddPartItems(GenericMenu menu, EasyUINode parent, string path, EasyUINode current)
        {
            foreach (var child in document.nodes)
            {
                if (child.parentId != parent.id || !child.IsPart)
                {
                    continue;
                }

                var id = child.id;
                var title = path + EasyUIParts.DisplayName(child.part);
                menu.AddItem(new GUIContent(title), child == current, () => SelectFromPanel(id));
                AddPartItems(menu, child, title + " > ", current);
            }
        }

        private void SelectFromPanel(int id)
        {
            ConfirmRename();
            SelectOnly(id);
            Repaint();
        }

        // A section's header: its title with an arrow - down while open, right while folded - and a line under
        // it. A click anywhere on it folds or opens the section. Returns whether it is open. With `removable`,
        // a small cross on the right sets `removed` instead.
        private bool Section(string title, ref bool open) => Section(title, ref open, false, out _);

        private bool Section(string title, ref bool open, bool removable, out bool removed)
        {
            GUILayout.Space(6f);
            var row = EditorGUILayout.GetControlRect();
            removed = false;

            if (removable)
            {
                var cross = new Rect(row.xMax - 18f, row.y, 18f, row.height);
                removed = GUI.Button(cross, new GUIContent("✕", "Remove"), EditorStyles.miniLabel);
                row.xMax -= 20f;
            }

            open = EditorGUI.Foldout(row, open, title, true, SectionStyle);
            var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(line, SectionLineColor);
            GUILayout.Space(4f);
            return open && !removed;
        }

        #region Rect Transform

        // Like a RectTransform's Inspector: the anchor preset on the left (click it for the presets), and the
        // fields that preset uses on the right - Pos X / Width and Pos Y / Height on an anchored axis, Left /
        // Right and Top / Bottom on a stretched one. As in Unity, all of them are measured in the parent (the
        // canvas, for the panel's own elements): positions from the anchor to the pivot, y up.
        private void DrawRectTransformSection(EasyUINode node)
        {
            var block = GUILayoutUtility.GetRect(0f, RectBlockHeight, GUILayout.ExpandWidth(true));
            DrawAnchorPresetButton(node, block);

            var parent = ParentRect(node);
            var rect = node.Rect;
            var stretchX = node.anchorH == HorizontalAnchor.Stretch;
            var stretchY = node.anchorV == VerticalAnchor.Stretch;
            var anchorX = parent.x + AnchorPresets.Fraction(node.anchorH) * parent.width;
            var anchorUp = AnchorPresets.Fraction(node.anchorV) * parent.height;
            var pivotUp = parent.yMax - (rect.y + rect.height * (1f - node.pivot.y));

            // Row 1: Pos X / Left, Pos Y / Top. Row 2: Width / Right, Height / Bottom.
            var x1 = stretchX ? rect.xMin - parent.xMin : rect.x + rect.width * node.pivot.x - anchorX;
            var y1 = stretchY ? rect.yMin - parent.yMin : pivotUp - anchorUp;
            var x2 = stretchX ? parent.xMax - rect.xMax : rect.width;
            var y2 = stretchY ? parent.yMax - rect.yMax : rect.height;

            var fields = new Rect(block.x + AnchorBlockWidth, block.y, block.width - AnchorBlockWidth, block.height);
            var column = (fields.width - 6f) * 0.5f;
            EditorGUI.BeginChangeCheck();
            x1 = LabeledFloat(new Rect(fields.x, fields.y, column, 32f), stretchX ? "Left" : "Pos X", x1);
            y1 = LabeledFloat(new Rect(fields.x + column + 6f, fields.y, column, 32f), stretchY ? "Top" : "Pos Y", y1);
            x2 = LabeledFloat(new Rect(fields.x, fields.y + 36f, column, 32f), stretchX ? "Right" : "Width", x2);
            y2 = LabeledFloat(new Rect(fields.x + column + 6f, fields.y + 36f, column, 32f), stretchY ? "Bottom" : "Height", y2);

            if (EditorGUI.EndChangeCheck())
            {
                // The size is settled (never below 0) before the position is worked out from it, so the pivot
                // stays exactly where Pos X / Pos Y put it - also while a new size is being typed digit by digit.
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
                    // A new height grows around the pivot.
                    height = Mathf.Max(y2, 0f);
                    yMin = parent.yMax - (y1 + anchorUp) - height * (1f - node.pivot.y);
                }

                SetRect(node, new Rect(xMin, yMin, width, height));
            }

            // Changing the pivot keeps the rect where it is; only the positions read differently.
            GUILayout.Space(PivotGap);
            EditorGUI.BeginChangeCheck();
            var pivot = EditorGUILayout.Vector2Field("Pivot", node.pivot);
            if (EditorGUI.EndChangeCheck())
            {
                node.pivot = new Vector2(Mathf.Clamp01(pivot.x), Mathf.Clamp01(pivot.y));
            }
        }

        // The preset icon with its axis names around it, as in a RectTransform's Inspector; a click opens the presets.
        private void DrawAnchorPresetButton(EasyUINode node, Rect block)
        {
            var icon = new Rect(block.x + 18f, block.y + 16f, 48f, 48f);
            GUI.Label(new Rect(icon.x, block.y, icon.width, 16f), AnchorPresets.Name(node.anchorH), CenteredMiniLabel);

            var matrix = GUI.matrix;
            var sideLabel = new Rect(block.x, icon.y, 16f, icon.height);
            GUIUtility.RotateAroundPivot(-90f, sideLabel.center);
            GUI.Label(new Rect(sideLabel.center.x - icon.height * 0.5f, sideLabel.center.y - 8f, icon.height, 16f),
                AnchorPresets.Name(node.anchorV), CenteredMiniLabel);
            GUI.matrix = matrix;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(icon, AnchorButtonColor);
            }

            AnchorPresets.DrawIcon(icon, node.anchorH, node.anchorV, false);
            AnchorPresets.DrawOutline(icon, AnchorButtonBorderColor);

            if (GUI.Button(icon, GUIContent.none, GUIStyle.none))
            {
                var id = node.id;
                PopupWindow.Show(icon, new AnchorPresetPopup(node.anchorH, node.anchorV,
                    (horizontal, vertical, setPivot, setPosition) => ApplyAnchorPreset(id, horizontal, vertical, setPivot, setPosition)));
            }
        }

        // A preset from the popup: new anchors; with Shift the pivot moves to them too, with Alt the element goes
        // there (on a stretched axis: across the whole parent).
        private void ApplyAnchorPreset(int id, HorizontalAnchor? horizontal, VerticalAnchor? vertical, bool setPivot, bool setPosition)
        {
            var node = document.Find(id);
            if (node == null)
            {
                return;
            }

            var parent = ParentRect(node);
            var rect = node.Rect;

            if (horizontal.HasValue)
            {
                node.anchorH = horizontal.Value;
                if (setPivot)
                {
                    node.pivot.x = AnchorPresets.Fraction(node.anchorH);
                }

                if (setPosition)
                {
                    if (node.anchorH == HorizontalAnchor.Stretch)
                    {
                        rect.xMin = parent.xMin;
                        rect.xMax = parent.xMax;
                    }
                    else
                    {
                        rect.x = parent.x + AnchorPresets.Fraction(node.anchorH) * parent.width - rect.width * node.pivot.x;
                    }
                }
            }

            if (vertical.HasValue)
            {
                node.anchorV = vertical.Value;
                if (setPivot)
                {
                    node.pivot.y = AnchorPresets.Fraction(node.anchorV);
                }

                if (setPosition)
                {
                    if (node.anchorV == VerticalAnchor.Stretch)
                    {
                        rect.yMin = parent.yMin;
                        rect.yMax = parent.yMax;
                    }
                    else
                    {
                        // Unity's Pos Y 0: the pivot on the anchor's height.
                        rect.y = parent.yMax - AnchorPresets.Fraction(node.anchorV) * parent.height - rect.height * (1f - node.pivot.y);
                    }
                }
            }

            SetRect(node, rect);
            Repaint();
        }

        // A small label above its field, as a RectTransform's Inspector lays out Pos X, Width and the like.
        // The label is a drag handle, as a label beside a number field is in Unity's Inspector: press on it and
        // drag left or right to change the value.
        private static float LabeledFloat(Rect rect, string label, float value)
        {
            var labelRect = new Rect(rect.x, rect.y, rect.width, 14f);
            GUI.Label(labelRect, label, EditorStyles.miniLabel);
            value = DragValue(labelRect, value);
            return EditorGUI.FloatField(new Rect(rect.x, rect.y + 14f, rect.width, 18f), value);
        }

        // Unity's drag sensitivity for a float: faster for larger values; Shift four times faster, Alt four
        // times slower. Rounded to the step, so dragging gives tidy numbers.
        private static float DragValue(Rect area, float value)
        {
            var id = GUIUtility.GetControlID(FocusType.Passive, area);
            var e = Event.current;
            EditorGUIUtility.AddCursorRect(area, MouseCursor.SlideArrow);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && area.Contains(e.mousePosition):
                    GUIUtility.hotControl = id;
                    GUIUtility.keyboardControl = 0;
                    EditorGUIUtility.editingTextField = false;
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    var step = Mathf.Max(1f, Mathf.Sqrt(Mathf.Abs(value))) * 0.03f;
                    if (e.shift)
                    {
                        step *= 4f;
                    }

                    if (e.alt)
                    {
                        step *= 0.25f;
                    }

                    value += HandleUtility.niceMouseDelta * step;
                    value = Mathf.Round(value / step) * step;
                    value = Mathf.Round(value * 1000f) / 1000f;
                    GUI.changed = true;
                    e.Use();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }

            return value;
        }

        #endregion

        #region Scroll View

        // The Scroll Rect's own fields, as in its Inspector (its viewport, content and scrollbars are built, so
        // they aren't picked here).
        private void DrawScrollViewSection(EasyUINode node)
        {
            var scroll = node.scrollView;
            ToggleRow("Horizontal", ref scroll.horizontal);
            ToggleRow("Vertical", ref scroll.vertical);

            scroll.movementType = (ScrollMovementType)EditorGUILayout.EnumPopup("Movement Type", scroll.movementType);
            GUILayout.Space(RowGap);
            if (scroll.movementType == ScrollMovementType.Elastic)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    scroll.elasticity = Mathf.Max(0f, EditorGUILayout.FloatField("Elasticity", scroll.elasticity));
                    GUILayout.Space(RowGap);
                }
            }

            ToggleRow("Inertia", ref scroll.inertia);
            if (scroll.inertia)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    scroll.decelerationRate = Mathf.Max(0f, EditorGUILayout.FloatField("Deceleration Rate", scroll.decelerationRate));
                    GUILayout.Space(RowGap);
                }
            }

            scroll.scrollSensitivity = EditorGUILayout.FloatField("Scroll Sensitivity", scroll.scrollSensitivity);
            GUILayout.Space(RowGap + 4f);

            var horizontal = scroll.horizontalScrollbar;
            var vertical = scroll.verticalScrollbar;
            DrawScrollbar("Horizontal Scrollbar", ref scroll.horizontalScrollbar, ref scroll.horizontalScrollbarVisibility,
                ref scroll.horizontalScrollbarSpacing);
            DrawScrollbar("Vertical Scrollbar", ref scroll.verticalScrollbar, ref scroll.verticalScrollbarVisibility,
                ref scroll.verticalScrollbarSpacing);

            // A scrollbar ticked or unticked: its part comes or goes with it.
            if (horizontal != scroll.horizontalScrollbar || vertical != scroll.verticalScrollbar)
            {
                EasyUIParts.EnsureParts(document, node);
                PruneSelection();
            }
        }

        // A scrollbar exists (as a part) while ticked; its Visibility and Spacing, as in the Scroll Rect's Inspector.
        private static void DrawScrollbar(string label, ref bool shown, ref ScrollbarVisibility visibility, ref float spacing)
        {
            ToggleRow(label, ref shown);
            if (!shown)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                visibility = (ScrollbarVisibility)EditorGUILayout.EnumPopup("Visibility", visibility);
                GUILayout.Space(RowGap);
                spacing = EditorGUILayout.FloatField("Spacing", spacing);
                GUILayout.Space(RowGap);
            }
        }

        #endregion

        #region Components

        private void DrawComponents(EasyUINode node)
        {
            var fitter = node.contentSizeFitter;
            if (fitter.enabled)
            {
                var open = Section("Content Size Fitter", ref fitter.open, true, out var removed);
                fitter.enabled = !removed;
                if (open)
                {
                    fitter.horizontalFit = (FitMode)EditorGUILayout.EnumPopup("Horizontal Fit", fitter.horizontalFit);
                    GUILayout.Space(RowGap);
                    fitter.verticalFit = (FitMode)EditorGUILayout.EnumPopup("Vertical Fit", fitter.verticalFit);
                    GUILayout.Space(RowGap);
                }
            }

            var group = node.canvasGroup;
            if (group.enabled)
            {
                var open = Section("Canvas Group", ref group.open, true, out var removed);
                group.enabled = !removed;
                if (open)
                {
                    group.alpha = EditorGUILayout.Slider("Alpha", group.alpha, 0f, 1f);
                    GUILayout.Space(RowGap);
                    ToggleRow("Interactable", ref group.interactable);
                    ToggleRow("Blocks Raycasts", ref group.blocksRaycasts);
                    ToggleRow("Ignore Parent Groups", ref group.ignoreParentGroups);
                }
            }

            var layout = node.layoutGroup;
            if (layout.enabled)
            {
                var open = Section($"{layout.kind} Layout Group", ref layout.open, true, out var removed);
                layout.enabled = !removed;
                if (open)
                {
                    DrawLayoutGroup(layout);
                }
            }

            var element = node.layoutElement;
            if (element.enabled)
            {
                var open = Section("Layout Element", ref element.open, true, out var removed);
                element.enabled = !removed;
                if (open)
                {
                    ToggleRow("Ignore Layout", ref element.ignoreLayout);
                    using (new EditorGUI.DisabledScope(element.ignoreLayout))
                    {
                        OptionalFloat("Min Width", ref element.useMinWidth, ref element.minWidth);
                        OptionalFloat("Min Height", ref element.useMinHeight, ref element.minHeight);
                        OptionalFloat("Preferred Width", ref element.usePreferredWidth, ref element.preferredWidth);
                        OptionalFloat("Preferred Height", ref element.usePreferredHeight, ref element.preferredHeight);
                        OptionalFloat("Flexible Width", ref element.useFlexibleWidth, ref element.flexibleWidth);
                        OptionalFloat("Flexible Height", ref element.useFlexibleHeight, ref element.flexibleHeight);
                    }

                    element.layoutPriority = EditorGUILayout.IntField("Layout Priority", element.layoutPriority);
                    GUILayout.Space(RowGap);
                }
            }

            var mask = node.mask;
            if (mask.enabled)
            {
                var open = Section("Mask", ref mask.open, true, out var removed);
                mask.enabled = !removed;
                if (open)
                {
                    ToggleRow("Show Mask Graphic", ref mask.showMaskGraphic);
                }
            }

            var rectMask = node.rectMask2D;
            if (rectMask.enabled)
            {
                var open = Section("Rect Mask 2D", ref rectMask.open, true, out var removed);
                rectMask.enabled = !removed;
                if (open)
                {
                    DrawRectMask2D(rectMask);
                }
            }
        }

        // Padding (kept as uGUI does: x left, y bottom, z right, w top), then Softness.
        private static void DrawRectMask2D(RectMask2DSettings mask)
        {
            mask.paddingOpen = EditorGUILayout.Foldout(mask.paddingOpen, "Padding", true);
            GUILayout.Space(RowGap);
            if (mask.paddingOpen)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    mask.padding.x = EditorGUILayout.FloatField("Left", mask.padding.x);
                    GUILayout.Space(RowGap);
                    mask.padding.z = EditorGUILayout.FloatField("Right", mask.padding.z);
                    GUILayout.Space(RowGap);
                    mask.padding.w = EditorGUILayout.FloatField("Top", mask.padding.w);
                    GUILayout.Space(RowGap);
                    mask.padding.y = EditorGUILayout.FloatField("Bottom", mask.padding.y);
                    GUILayout.Space(RowGap);
                }
            }

            mask.softness = Vector2Int.Max(EditorGUILayout.Vector2IntField("Softness", mask.softness), Vector2Int.zero);
            GUILayout.Space(RowGap);
        }

        private void DrawLayoutGroup(LayoutGroupSettings layout)
        {
            layout.paddingOpen = EditorGUILayout.Foldout(layout.paddingOpen, "Padding", true);
            GUILayout.Space(RowGap);
            if (layout.paddingOpen)
            {
                using (new EditorGUI.IndentLevelScope())
                {
                    layout.paddingLeft = EditorGUILayout.IntField("Left", layout.paddingLeft);
                    GUILayout.Space(RowGap);
                    layout.paddingRight = EditorGUILayout.IntField("Right", layout.paddingRight);
                    GUILayout.Space(RowGap);
                    layout.paddingTop = EditorGUILayout.IntField("Top", layout.paddingTop);
                    GUILayout.Space(RowGap);
                    layout.paddingBottom = EditorGUILayout.IntField("Bottom", layout.paddingBottom);
                    GUILayout.Space(RowGap);
                }
            }

            if (layout.kind == LayoutKind.Grid)
            {
                layout.cellSize = Vector2.Max(EditorGUILayout.Vector2Field("Cell Size", layout.cellSize), Vector2.zero);
                GUILayout.Space(RowGap);
                layout.gridSpacing = EditorGUILayout.Vector2Field("Spacing", layout.gridSpacing);
                GUILayout.Space(RowGap);
                layout.startCorner = (GridStartCorner)EditorGUILayout.EnumPopup("Start Corner", layout.startCorner);
                GUILayout.Space(RowGap);
                layout.startAxis = (GridStartAxis)EditorGUILayout.EnumPopup("Start Axis", layout.startAxis);
                GUILayout.Space(RowGap);
                layout.childAlignment = (TextAnchor)EditorGUILayout.EnumPopup("Child Alignment", layout.childAlignment);
                GUILayout.Space(RowGap);
                layout.constraint = (GridConstraint)EditorGUILayout.EnumPopup("Constraint", layout.constraint);
                if (layout.constraint != GridConstraint.Flexible)
                {
                    GUILayout.Space(RowGap);
                    layout.constraintCount = Mathf.Max(1, EditorGUILayout.IntField("Constraint Count", layout.constraintCount));
                }

                GUILayout.Space(RowGap);
                return;
            }

            layout.spacing = EditorGUILayout.FloatField("Spacing", layout.spacing);
            GUILayout.Space(RowGap);
            layout.childAlignment = (TextAnchor)EditorGUILayout.EnumPopup("Child Alignment", layout.childAlignment);
            GUILayout.Space(RowGap);
            ToggleRow("Reverse Arrangement", ref layout.reverseArrangement);
            ToggleRow("Control Child Size", ref layout.controlChildWidth, ref layout.controlChildHeight);
            ToggleRow("Use Child Scale", ref layout.useChildScaleWidth, ref layout.useChildScaleHeight);
            ToggleRow("Child Force Expand", ref layout.childForceExpandWidth, ref layout.childForceExpandHeight);
        }

        // The round "+" under the components: a menu of the ones not added yet (one layout group at most).
        private void DrawAddComponentButton(EasyUINode node)
        {
            GUILayout.Space(8f);
            var row = GUILayoutUtility.GetRect(AddButtonSize, AddButtonSize, GUILayout.ExpandWidth(true));
            var button = new Rect(row.center.x - AddButtonSize * 0.5f, row.y, AddButtonSize, AddButtonSize);
            if (!GUI.Button(button, new GUIContent("+", "Add a component"), AddButtonStyle))
            {
                GUILayout.Space(2f);
                return;
            }

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
            foreach (LayoutKind kind in System.Enum.GetValues(typeof(LayoutKind)))
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

            menu.DropDown(button);
            GUILayout.Space(2f);
        }

        private void AddComponentItem(GenericMenu menu, string title, bool added, GenericMenu.MenuFunction add)
        {
            if (added)
            {
                menu.AddDisabledItem(new GUIContent(title));
                return;
            }

            menu.AddItem(new GUIContent(title), false, () =>
            {
                add();
                Repaint();
            });
        }

        #endregion

        #region Rows

        // One toggle row, drawn like the Width / Height pairs so every box lines up under the other.
        private static void ToggleRow(string label, ref bool value)
        {
            var row = ToggleRowRect(label);
            value = EditorGUI.ToggleLeft(new Rect(row.x, row.y, ToggleColumn, row.height), GUIContent.none, value);
        }

        private static void ToggleRow(string label, ref bool width, ref bool height)
        {
            var row = ToggleRowRect(label);
            width = EditorGUI.ToggleLeft(new Rect(row.x, row.y, ToggleColumn, row.height), "Width", width);
            height = EditorGUI.ToggleLeft(new Rect(row.x + ToggleColumn, row.y, ToggleColumn, row.height), "Height", height);
        }

        // Lays out a row with its label, and returns the part right of the label.
        private static Rect ToggleRowRect(string label)
        {
            var row = EditorGUILayout.GetControlRect();
            GUILayout.Space(RowGap);
            return EditorGUI.PrefixLabel(row, new GUIContent(label));
        }

        // A size used only while its box is ticked, as in a Layout Element's Inspector.
        private static void OptionalFloat(string label, ref bool use, ref float value)
        {
            var row = ToggleRowRect(label);
            use = EditorGUI.Toggle(new Rect(row.x, row.y, 18f, row.height), use);
            using (new EditorGUI.DisabledScope(!use))
            {
                value = EditorGUI.FloatField(new Rect(row.x + 20f, row.y, row.width - 20f, row.height), value);
            }
        }

        #endregion
    }
}
