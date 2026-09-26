using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// <c>Tools &gt; Easy UI</c>: a dockable, Shader-Graph-like workspace for designing a uGUI panel. It shows the
    /// scene's canvas as a brighter area on an endless grid - sized like the canvas (e.g. 1920 x 1080 with a
    /// Canvas Scaler) - with the panel's elements on it.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>Right-click to add an element (Text, Image, Button, Scroll View...). It goes under the selected
    /// element, or under the panel itself when nothing is selected, and asks for its name right away.</item>
    /// <item>Click an element to select it (green); Ctrl + click to add or remove one; drag from an empty spot to
    /// select every element a box touches (an element is selected when the click is released, so pressing one
    /// never moves it). Drag a selected element to move the selection, drag an edge or corner
    /// to resize it; Ctrl + D duplicates the selection and Delete removes it, with their children. Children follow their parent as their
    /// anchors say and never leave it - see <c>EasyUIWindow.Selection.cs</c>.</item>
    /// <item>An element's layer is how deep it sits: 1 for the panel's own children, 2 for theirs... Elements on
    /// the same layer can't overlap - see <c>EasyUIWindow.Layers.cs</c>.</item>
    /// <item>Hold Shift while moving or resizing to snap to the canvas lines; blue guides show and pull onto
    /// lined-up edges - see <c>EasyUIWindow.Guides.cs</c>.</item>
    /// <item>The selected element's Rect Transform and components are edited in the panel on the right - see
    /// <c>EasyUIWindow.Inspector.cs</c>. The top bar names, saves and clears the panel.</item>
    /// <item>Pan with the middle mouse button (or Alt + left drag), zoom with the scroll wheel, press F to fit
    /// the selection - or, with nothing selected, the canvas - in the view. The round info button in the
    /// bottom-right corner lists all of these - see <c>EasyUIWindow.Help.cs</c>.</item>
    /// </list>
    /// A change to the canvas (including the Game view's size) is drawn within a tenth of a second - checked
    /// in <see cref="OnInspectorUpdate"/>, which only repaints when it actually changed.
    /// </remarks>
    internal sealed partial class EasyUIWindow : EditorWindow
    {
        private const string Title = "Easy UI";
        private const float MinZoom = 0.05f;
        private const float MaxZoom = 2f;
        private const float FrameMargin = 40f;

        // Grid steps, in canvas units, when the canvas's size gives no better ones (see UpdateGridSteps).
        private const float DefaultMajorStep = 100f;
        private const float MinorStepTarget = 20f;
        private const float MinLineGap = 6f;

        // The smallest an element can be dragged to on the canvas, in canvas units - any smaller and its edges
        // couldn't be grabbed. Its Rect Transform fields go down to 0.
        private const float MinElementSize = 8f;

        // The pivot ring's size, and how close to an edge the pointer must be to grab it; window pixels.
        private const float PivotSize = 14f;
        private const float EdgeGrab = 6f;

        private static readonly Color BackgroundColor = new(0.157f, 0.157f, 0.157f);
        private static readonly Color MinorLineColor = new(0.12f, 0.12f, 0.12f);
        private static readonly Color MajorLineColor = new(0.09f, 0.09f, 0.09f);
        private static readonly Color CanvasColor = new(0.2f, 0.2f, 0.21f);
        private static readonly Color CanvasMinorLineColor = new(1f, 1f, 1f, 0.07f);
        private static readonly Color CanvasMajorLineColor = new(1f, 1f, 1f, 0.2f);
        private static readonly Color CanvasBorderColor = new(1f, 1f, 1f, 0.75f);
        private static readonly Color IdleFillColor = new(0.35f, 0.55f, 0.9f, 0.18f);
        private static readonly Color IdleBorderColor = new(0.35f, 0.55f, 0.9f, 0.9f);
        private static readonly Color SelectedFillColor = new(0.3f, 0.85f, 0.4f, 0.22f);
        private static readonly Color SelectedBorderColor = new(0.3f, 0.9f, 0.4f, 1f);
        private static readonly Color InvalidFillColor = new(0.95f, 0.3f, 0.3f, 0.2f);
        private static readonly Color InvalidBorderColor = new(1f, 0.35f, 0.35f, 1f);
        private static readonly Color HeldBackBorderColor = new(1f, 0.85f, 0.2f, 1f);

        [SerializeField] private EasyUIDocument document = new();
        [SerializeField] private Vector2 pan;
        [SerializeField] private float zoom = 0.4f;
        [SerializeField] private bool framed;

        private Canvas _canvas;
        private bool _canvasSearched;
        private bool _frameRequested;
        private Vector2 _canvasSize;
        private Vector2 _drawnCanvasSize;
        private int _drawnCanvasId;
        private Rect _view;

        // The grid's steps: a strong line every _majorStep, a thin one every _minorStep - which Shift snaps to.
        private float _majorStep = DefaultMajorStep;
        private float _minorStep = MinorStepTarget;
        private Vector2 _gridStepsFor;

        private int _selectedId;
        private int _panelNodeId;
        private bool _panelShown;
        private bool _warnNoCanvas;
        private bool _panning;
        private Drag _drag;
        private Edges _dragEdges;
        private Edges _hoverEdges;
        private Vector2 _dragStartPointer;
        private Rect _dragStartRect;
        private readonly List<EasyUINode> _drawOrder = new();
        private UnityEditor.IMGUI.Controls.AdvancedDropdownState _createDropdownState;

        private Texture2D _pivotTexture;
        private GUIStyle _labelStyle;
        private GUIStyle _invalidLabelStyle;

        private enum Drag
        {
            None,
            Select,
            Move,
            Resize,
            Marquee
        }

        [System.Flags]
        private enum Edges
        {
            None = 0,
            Left = 1,
            Right = 2,
            Top = 4,
            Bottom = 8
        }

        private const Edges TopLeft = Edges.Top | Edges.Left;
        private const Edges TopRight = Edges.Top | Edges.Right;
        private const Edges BottomLeft = Edges.Bottom | Edges.Left;
        private const Edges BottomRight = Edges.Bottom | Edges.Right;

        private GUIStyle LabelStyle => _labelStyle ??= new GUIStyle(EditorStyles.boldLabel)
        {
            clipping = TextClipping.Clip,
            normal = { textColor = new Color(1f, 1f, 1f, 0.85f) }
        };

        private GUIStyle InvalidLabelStyle => _invalidLabelStyle ??= new GUIStyle(LabelStyle)
        {
            normal = { textColor = new Color(1f, 0.45f, 0.45f, 1f) }
        };

        // A blue ring with a white rim around a see-through dark middle, drawn once and kept.
        private Texture2D PivotTexture
        {
            get
            {
                if (_pivotTexture != null)
                {
                    return _pivotTexture;
                }

                const int size = 32;
                _pivotTexture = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
                var center = (size - 1) * 0.5f;
                for (var y = 0; y < size; y++)
                {
                    for (var x = 0; x < size; x++)
                    {
                        var distance = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                        Color color;
                        if (distance > 15.5f)
                        {
                            color = Color.clear;
                        }
                        else if (distance > 13f)
                        {
                            color = new Color(1f, 1f, 1f, Mathf.Clamp01(15.5f - distance));
                        }
                        else if (distance > 8.5f)
                        {
                            color = new Color(0.2f, 0.55f, 1f, 1f);
                        }
                        else
                        {
                            color = new Color(0.05f, 0.1f, 0.2f, 0.5f);
                        }

                        _pivotTexture.SetPixel(x, y, color);
                    }
                }

                _pivotTexture.Apply();
                return _pivotTexture;
            }
        }

        [MenuItem("Tools/Easy UI")]
        private static void Open()
        {
            var window = GetWindow<EasyUIWindow>(Title);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent(Title);
            wantsMouseMove = true;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            EasyUIParts.EnsureParts(document);
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            DestroyTexture(ref _pivotTexture);
            DestroyPanelTextures();
            DestroyLabelTextures();
            DestroyInfoTextures();
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture != null)
            {
                DestroyImmediate(texture);
                texture = null;
            }
        }

        // A canvas may have been added, removed or replaced: look again on the next check.
        private void OnHierarchyChanged()
        {
            _canvasSearched = false;
            Repaint();
        }

        // Ten times a second: repaint only if the canvas, or its size, changed since the last draw.
        private void OnInspectorUpdate()
        {
            var canvas = FindCanvas();
            var id = canvas != null ? canvas.GetInstanceID() : 0;
            if (id != _drawnCanvasId || CanvasSizeOf(canvas) != _drawnCanvasSize)
            {
                Repaint();
            }
        }

        private void OnGUI()
        {
            // IMGUI lays out every event in two passes (Layout, then the event itself), and both must make the
            // same layout calls. So everything that decides what is laid out - the warning, the panel - is
            // settled in the Layout pass and kept for the rest of the event, even if a click changes it.
            var e = Event.current;
            var canvas = FindCanvas();
            _canvasSize = CanvasSizeOf(canvas);
            _drawnCanvasSize = _canvasSize;
            UpdateGridSteps();
            _drawnCanvasId = canvas != null ? canvas.GetInstanceID() : 0;

            if (e.type == EventType.Layout)
            {
                _warnNoCanvas = canvas == null;
                _panelShown = canvas != null && document.Find(_selectedId) != null;
                _panelNodeId = _selectedId;
            }

            DrawToolbar();

            if (_warnNoCanvas)
            {
                EditorGUILayout.HelpBox("There is no Canvas in the scene. Add one (GameObject > UI > Canvas): " +
                                        "this editor shows its area, where the panel goes.", MessageType.Warning);
            }

            // The Layout pass only hands back a placeholder rect, so the view keeps its last real one there.
            var view = GUILayoutUtility.GetRect(0f, 0f, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            if (e.type != EventType.Layout)
            {
                _view = view;
            }

            var hasCanvas = canvas != null && _canvasSize != Vector2.zero;
            if ((!framed || _frameRequested) && e.type == EventType.Repaint && hasCanvas)
            {
                // The first time, the canvas; on F, the selected element, or the canvas without one.
                _frameRequested = false;
                Frame(framed && SelectionBounds(out var selection) ? selection : CanvasRect);
            }

            if (!hasCanvas)
            {
                ClearSelection();
                _drag = Drag.None;
            }

            // Layout components first (they move and size elements), then which labels show, for this event's
            // clicks and drawing alike.
            UpdateLayout();
            UpdateLabels();
            HandleInput(hasCanvas);
            UpdateCursor(hasCanvas);

            if (e.type == EventType.Repaint)
            {
                DrawWorkspace(canvas, hasCanvas);
            }

            if (hasCanvas)
            {
                DrawRenameField();
            }

            if (_panelShown)
            {
                DrawInspector();
            }
        }

        #region View

        // Canvas units -> window pixels: `pan` is the canvas point at the middle of the view.
        private Vector2 ToScreen(Vector2 point) => _view.center + (point - pan) * zoom;

        private Vector2 ToCanvas(Vector2 screen) => pan + (screen - _view.center) / zoom;

        private Rect ToScreen(Rect rect) => new(ToScreen(rect.min), rect.size * zoom);

        private Rect CanvasRect => new(Vector2.zero, _canvasSize);

        // Grid steps that fit the canvas exactly, so no half square is left at its right or bottom edge: the
        // strong step is the largest length that divides both sides (1920 x 1080 -> 120: 16 x 9 squares),
        // halved while it is over 200; the thin step splits it into parts of about 20. A canvas whose sides have
        // no such length in common (e.g. a free-aspect Game view) keeps the default steps.
        private void UpdateGridSteps()
        {
            if (_canvasSize == _gridStepsFor)
            {
                return;
            }

            _gridStepsFor = _canvasSize;
            _majorStep = DefaultMajorStep;

            var width = Mathf.RoundToInt(_canvasSize.x);
            var height = Mathf.RoundToInt(_canvasSize.y);
            if (width > 0 && height > 0 && Mathf.Approximately(width, _canvasSize.x) && Mathf.Approximately(height, _canvasSize.y))
            {
                var common = GreatestCommonDivisor(width, height);
                while (common > 200 && common % 2 == 0)
                {
                    common /= 2;
                }

                if (common >= 60 && common <= 200)
                {
                    _majorStep = common;
                }
            }

            _minorStep = _majorStep / Mathf.Max(1, Mathf.RoundToInt(_majorStep / MinorStepTarget));
        }

        private static int GreatestCommonDivisor(int a, int b)
        {
            while (b != 0)
            {
                (a, b) = (b, a % b);
            }

            return a;
        }

        // Centers `target` (canvas units) in the view, zoomed to fit it with a margin all round.
        private void Frame(Rect target)
        {
            if (target.width <= 0f || target.height <= 0f || _view.width <= 0f)
            {
                return;
            }

            pan = target.center;
            zoom = Mathf.Clamp(Mathf.Min((_view.width - FrameMargin * 2f) / target.width,
                (_view.height - FrameMargin * 2f) / target.height), MinZoom, MaxZoom);
            framed = true;
            Repaint();
        }

        #endregion

        #region Elements

        // Parents before their children, siblings in the order they were added: the order elements are drawn in.
        private List<EasyUINode> DrawOrder()
        {
            _drawOrder.Clear();
            AddChildren(0);
            return _drawOrder;
        }

        private void AddChildren(int parentId)
        {
            foreach (var node in document.nodes)
            {
                // Elements whose parent is gone count as the panel's own.
                var parent = node.parentId;
                if (parent != 0 && document.Find(parent) == null)
                {
                    parent = 0;
                }

                if (parent == parentId && _drawOrder.Count <= document.nodes.Count && !_drawOrder.Contains(node))
                {
                    _drawOrder.Add(node);
                    AddChildren(node.id);
                }
            }
        }

        // The element under the pointer (window pixels), topmost - drawn last - first. A part only counts while
        // its parent is selected (see EasyUIWindow.Parts.cs).
        private EasyUINode NodeAt(Vector2 pointer)
        {
            var order = DrawOrder();
            for (var i = order.Count - 1; i >= 0; i--)
            {
                if (IsClickable(order[i]) && ToScreen(order[i].Rect).Contains(pointer))
                {
                    return order[i];
                }
            }

            return null;
        }

        // The rect the element's anchors and positions are measured in: its parent's, or the canvas.
        private Rect ParentRect(EasyUINode node)
        {
            var parent = document.Find(node.parentId);
            return parent != null ? parent.Rect : CanvasRect;
        }

        /// <summary>
        /// Moves and resizes the element (never below a size of 0), its children following as their anchors
        /// say. With <paramref name="keepInside"/>, it is moved inside its parent - or the canvas - if it would
        /// stick out; otherwise it is placed as asked, and drawn red if it is out.
        /// </summary>
        private void SetRect(EasyUINode node, Rect rect, bool keepInside = false)
        {
            var size = Vector2.Max(rect.size, Vector2.zero);
            var position = rect.position;
            if (keepInside)
            {
                var bounds = ParentRect(node);
                position = Vector2.Min(Vector2.Max(position, bounds.min), Vector2.Max(bounds.max - size, bounds.min));
            }

            ApplyRect(node, new Rect(position, size));
            Touch(node);
        }

        // Adds an element of `type` with its top-left corner at `at` (canvas units), under `parentId` (0: the
        // panel) - with its parts, if it has any - selects it and asks for its name.
        private void AddNode(EasyUIElementType type, int parentId, Vector2 at)
        {
            var node = new EasyUINode
            {
                id = document.nextId++,
                parentId = parentId,
                type = type,
                size = EasyUINode.DefaultSize(type)
            };

            ApplyDefaultLook(node);
            document.nodes.Add(node);
            SetRect(node, new Rect(at, node.size), true);
            EasyUIParts.EnsureParts(document, node);
            SelectOnly(node.id);
            StartRename(node);
            Repaint();
        }

        // Removes every selected element and everything under them.
        private void DeleteSelection()
        {
            var roots = new List<EasyUINode>(SelectionRoots());
            foreach (var root in roots)
            {
                document.nodes.RemoveAll(other => document.IsUnder(other, root));
            }

            PruneSelection();
            if (document.Find(_renamingId) == null)
            {
                _renamingId = 0;
            }

            Repaint();
        }

        #endregion

        #region Input

        private void HandleInput(bool hasCanvas)
        {
            var e = Event.current;
            var id = GUIUtility.GetControlID(FocusType.Passive);

            // The info button (and its panel) takes its own clicks; any other click closes the panel.
            if (e.type == EventType.MouseDown && e.button == 0 && _view.Contains(e.mousePosition) && !IsOverPanel(e.mousePosition)
                && HandleInfoPress(e.mousePosition))
            {
                e.Use();
                return;
            }

            var inView = _view.Contains(e.mousePosition) && !IsOverPanel(e.mousePosition) && !IsOverRenameField(e.mousePosition)
                         && !IsOverInfo(e.mousePosition);

            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when e.button == 0 && !e.alt && inView && hasCanvas:
                    // A click on the workspace confirms a name being typed, and lets go of any field of the panel on
                    // the right, so Delete and F reach the workspace again.
                    ConfirmRename();
                    EndTextEditing();

                    var pencil = PencilAt(e.mousePosition);
                    if (pencil != null)
                    {
                        SelectOnly(pencil.id);
                        StartRename(pencil);
                    }
                    else
                    {
                        BeginDrag(e.mousePosition, e.control || e.command);
                        GUIUtility.hotControl = id;
                    }

                    e.Use();
                    Repaint();
                    break;

                case EventType.ContextClick when inView && hasCanvas:
                    ConfirmRename();
                    ShowAddMenu(e.mousePosition);
                    e.Use();
                    break;

                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    ContinueDrag(e.mousePosition, e.shift);
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;

                    // An element not selected yet is selected on release (still under the pointer): pressing it
                    // doesn't move it, so a drag always starts from a deliberate second press.
                    if (_drag == Drag.Select)
                    {
                        var released = NodeAt(e.mousePosition);
                        if (_drillId != 0)
                        {
                            // A click on a selected part with nothing deeper: back to the outermost element.
                            SelectOnly(_drillId);
                        }
                        else if (released != null && released.id == _pressedId)
                        {
                            SelectOnly(released.id);
                        }
                    }

                    // A click (no drag) inside a selected element goes one level deeper; one on one of several
                    // selected elements selects just that one.
                    if (_drag == Drag.Move && !_moved && _drillId != 0)
                    {
                        SelectOnly(_drillId);
                    }
                    else if (_drag == Drag.Move && !_moved && _selected.Count > 1)
                    {
                        SelectOnly(_pressedId);
                    }

                    _drag = Drag.None;
                    _blockers.Clear();
                    _guides.Clear();
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseDrag when IsPanning(e):
                    _panning = true;
                    pan -= e.delta / zoom;
                    e.Use();
                    Repaint();
                    break;

                case EventType.MouseUp when _panning:
                    _panning = false;
                    Repaint();
                    break;

                case EventType.ScrollWheel when inView:
                    // Zooms around the mouse: the canvas point under it stays under it.
                    var anchor = ToCanvas(e.mousePosition);
                    zoom = Mathf.Clamp(zoom * (1f - e.delta.y * 0.05f), MinZoom, MaxZoom);
                    pan += anchor - ToCanvas(e.mousePosition);
                    e.Use();
                    Repaint();
                    break;

                case EventType.KeyDown when !EditorGUIUtility.editingTextField && e.keyCode == KeyCode.F:
                    _frameRequested = true;
                    e.Use();
                    Repaint();
                    break;

                case EventType.KeyDown when !EditorGUIUtility.editingTextField && _selected.Count > 0
                                            && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace):
                    DeleteSelection();
                    e.Use();
                    break;

                // Ctrl + D and Delete reach an editor window as the Duplicate and (Soft)Delete commands: claim them
                // while something is selected, then carry them out.
                case EventType.ValidateCommand when IsSelectionCommand(e.commandName) && _selected.Count > 0 && !EditorGUIUtility.editingTextField:
                    e.Use();
                    break;

                case EventType.ExecuteCommand when IsSelectionCommand(e.commandName) && _selected.Count > 0 && !EditorGUIUtility.editingTextField:
                    if (e.commandName == "Duplicate")
                    {
                        DuplicateSelection();
                    }
                    else
                    {
                        DeleteSelection();
                    }

                    e.Use();
                    break;

                case EventType.MouseMove when hasCanvas:
                    // Only what is under the pointer matters here, for the cursor and highlights: repaint when it changes.
                    var selected = _selected.Count == 1 ? document.Find(_selectedId) : null;
                    var hover = selected != null && !selected.IsPart && inView ? EdgesAt(ToScreen(selected.Rect), e.mousePosition) : Edges.None;
                    var hoverPencil = inView ? PencilAt(e.mousePosition) : null;
                    var hoverPencilId = hoverPencil != null ? hoverPencil.id : 0;
                    var hoverInfo = InfoButtonRect(_view).Contains(e.mousePosition);
                    if (hover != _hoverEdges || hoverPencilId != _hoverPencilId || hoverInfo != _hoverInfo)
                    {
                        _hoverEdges = hover;
                        _hoverPencilId = hoverPencilId;
                        _hoverInfo = hoverInfo;
                        Repaint();
                    }

                    break;
            }
        }

        // Middle drag, or Alt + left drag - unless something else holds the mouse (e.g. a value dragged in the
        // panel on the right with Alt, for fine steps).
        private static bool IsPanning(Event e) => GUIUtility.hotControl == 0 && (e.button == 2 || (e.button == 0 && e.alt));

        private static bool IsSelectionCommand(string command) => command is "Duplicate" or "Delete" or "SoftDelete";

        // The right-click menu, like Shader Graph's "Create Node": search or browse for an element to add at the
        // click - under the selected one (a Scroll View's: in its Content), or under the panel when nothing is selected.
        private void ShowAddMenu(Vector2 pointer)
        {
            var parent = EasyUIParts.ChildParent(document, document.Find(_selectedId));
            var parentId = parent != null ? parent.id : 0;
            var at = ToCanvas(pointer);
            _createDropdownState ??= new UnityEditor.IMGUI.Controls.AdvancedDropdownState();
            new ElementDropdown(_createDropdownState, type =>
            {
                // The dropdown is a window of its own: give the keyboard back to this one, for the new name.
                Focus();
                AddNode(type, parentId, at);
            }).Show(new Rect(pointer, Vector2.zero));
        }

        // A press on the edge of the one selected element resizes it. A press on a selected element moves the
        // selection; so does one on something inside a selected element, which a click without a drag selects
        // instead (one level deeper, see IsClickable). A press on an element not selected nor inside one only
        // picks it, to select on release (see HandleInput) - so nothing moves until it is pressed again. With Ctrl,
        // a press only adds or removes that element. A press on an empty spot starts a selection box, clearing the
        // selection unless Ctrl is held.
        private void BeginDrag(Vector2 pointer, bool additive)
        {
            _dragStartPointer = ToCanvas(pointer);
            _dragEdges = Edges.None;
            _drag = Drag.None;
            _drillId = 0;

            var selected = _selected.Count == 1 ? document.Find(_selectedId) : null;
            var edges = !additive && selected != null && !selected.IsPart ? EdgesAt(ToScreen(selected.Rect), pointer) : Edges.None;
            if (edges != Edges.None)
            {
                _dragStartRect = selected.Rect;
                _dragEdges = edges;
                _leadId = selected.id;
                _drag = Drag.Resize;
                return;
            }

            var hit = NodeAt(pointer);
            if (hit == null)
            {
                if (!additive)
                {
                    ClearSelection();
                }

                StartMarquee(pointer, additive);
                return;
            }

            if (additive)
            {
                ToggleSelected(hit.id);
                return;
            }

            if (!IsSelected(hit))
            {
                var holder = SelectedAncestor(hit);
                if (holder != null && !holder.IsPart)
                {
                    _selectedId = holder.id;
                    StartMove(holder);
                    _drillId = hit.id;
                    return;
                }

                _pressedId = hit.id;
                _drag = Drag.Select;
                return;
            }

            _selectedId = hit.id;

            // Nothing deeper under the pointer (a child would have been hit): a click without a drag starts over
            // from the outermost element there. Not while several are selected: a click then keeps just this one.
            if (_selected.Count == 1)
            {
                _drillId = RootOf(hit).id;
            }

            if (!hit.IsPart)
            {
                StartMove(hit);
            }
            else if (_drillId != 0)
            {
                _drag = Drag.Select;
            }
        }

        private void ContinueDrag(Vector2 pointer, bool snap)
        {
            var delta = ToCanvas(pointer) - _dragStartPointer;
            switch (_drag)
            {
                case Drag.Marquee:
                    UpdateMarquee(pointer);
                    break;

                case Drag.Move:
                    MoveSelection(delta, snap);
                    break;

                case Drag.Resize:
                {
                    var node = document.Find(_leadId);
                    if (node == null)
                    {
                        return;
                    }

                    // Resized within its parent (or the canvas).
                    var bounds = ParentRect(node);
                    var min = _dragStartRect.min;
                    var max = _dragStartRect.max;
                    var smallest = MinElementSize;

                    if ((_dragEdges & Edges.Left) != 0)
                    {
                        min.x = Mathf.Clamp(SnapIf(min.x + delta.x, snap), bounds.xMin, max.x - smallest);
                    }

                    if ((_dragEdges & Edges.Right) != 0)
                    {
                        max.x = Mathf.Clamp(SnapIf(max.x + delta.x, snap), min.x + smallest, bounds.xMax);
                    }

                    if ((_dragEdges & Edges.Top) != 0)
                    {
                        min.y = Mathf.Clamp(SnapIf(min.y + delta.y, snap), bounds.yMin, max.y - smallest);
                    }

                    if ((_dragEdges & Edges.Bottom) != 0)
                    {
                        max.y = Mathf.Clamp(SnapIf(max.y + delta.y, snap), min.y + smallest, bounds.yMax);
                    }

                    var wanted = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
                    if (!snap)
                    {
                        wanted = PullEdgesToLines(node, wanted, _dragEdges);
                    }

                    SetRect(node, BlockResize(node, node.Rect, wanted, _dragEdges));
                    UpdateGuides(node);
                    break;
                }
            }
        }

        private Vector2 Snap(Vector2 value) => new(SnapIf(value.x, true), SnapIf(value.y, true));

        private float SnapIf(float value, bool snap) => snap ? Mathf.Round(value / _minorStep) * _minorStep : value;

        // The edges of `rect` (window pixels) within grabbing distance of `pointer`; corners give two.
        private static Edges EdgesAt(Rect rect, Vector2 pointer)
        {
            var grab = new Rect(rect.x - EdgeGrab, rect.y - EdgeGrab, rect.width + EdgeGrab * 2f, rect.height + EdgeGrab * 2f);
            if (!grab.Contains(pointer))
            {
                return Edges.None;
            }

            var edges = Edges.None;
            if (Mathf.Abs(pointer.x - rect.xMin) <= EdgeGrab)
            {
                edges |= Edges.Left;
            }
            else if (Mathf.Abs(pointer.x - rect.xMax) <= EdgeGrab)
            {
                edges |= Edges.Right;
            }

            if (Mathf.Abs(pointer.y - rect.yMin) <= EdgeGrab)
            {
                edges |= Edges.Top;
            }
            else if (Mathf.Abs(pointer.y - rect.yMax) <= EdgeGrab)
            {
                edges |= Edges.Bottom;
            }

            return edges;
        }

        // A hand while moving or panning; the matching resize arrows over the selected element's edges and while
        // resizing; a pointing hand over the name pencils.
        private void UpdateCursor(bool hasCanvas)
        {
            if (_drag == Drag.Move || _panning)
            {
                EditorGUIUtility.AddCursorRect(_view, MouseCursor.Pan);
                return;
            }

            if (_drag == Drag.Resize)
            {
                EditorGUIUtility.AddCursorRect(_view, CursorFor(_dragEdges));
                return;
            }

            if (!hasCanvas)
            {
                return;
            }

            var selected = _selected.Count == 1 ? document.Find(_selectedId) : null;
            if (selected != null && _hoverEdges != Edges.None)
            {
                var rect = ToScreen(selected.Rect);
                var grab = new Rect(rect.x - EdgeGrab, rect.y - EdgeGrab, rect.width + EdgeGrab * 2f, rect.height + EdgeGrab * 2f);
                EditorGUIUtility.AddCursorRect(grab, CursorFor(_hoverEdges));
            }

            foreach (var node in document.nodes)
            {
                if (HasLabel(node))
                {
                    EditorGUIUtility.AddCursorRect(PencilRect(node), MouseCursor.Link);
                }
            }

            EditorGUIUtility.AddCursorRect(InfoButtonRect(_view), MouseCursor.Link);
        }

        private static MouseCursor CursorFor(Edges edges) => edges switch
        {
            TopLeft or BottomRight => MouseCursor.ResizeUpLeft,
            TopRight or BottomLeft => MouseCursor.ResizeUpRight,
            Edges.Left or Edges.Right => MouseCursor.ResizeHorizontal,
            _ => MouseCursor.ResizeVertical
        };

        #endregion

        #region Drawing

        private void DrawWorkspace(Canvas canvas, bool hasCanvas)
        {
            // Inside the clip, coordinates start at the view's top-left corner, so the view is moved there
            // while drawing (ToScreen works from its center) and put back after.
            GUI.BeginClip(_view);
            var view = new Rect(Vector2.zero, _view.size);
            var offset = _view.position;
            _view.position = Vector2.zero;

            EditorGUI.DrawRect(view, BackgroundColor);
            DrawLines(view, _minorStep, MinorLineColor);
            DrawLines(view, _majorStep, MajorLineColor);

            if (hasCanvas)
            {
                // The canvas: brighter, with whiter lines, so it reads as the space the UI goes in.
                var area = ToScreen(CanvasRect);
                EditorGUI.DrawRect(area, CanvasColor);
                var visible = Intersect(area, view);
                DrawLines(visible, _minorStep, CanvasMinorLineColor);
                DrawLines(visible, _majorStep, CanvasMajorLineColor);
                DrawBorder(area, CanvasBorderColor, 2f);
                GUI.Label(new Rect(area.x, area.y - 22f, 600f, 20f),
                    $"{document.panelName}  -  {canvas.name}  ({_canvasSize.x:0} x {_canvasSize.y:0})", LabelStyle);

                foreach (var node in DrawOrder())
                {
                    DrawNode(node);
                }

                if (_drag == Drag.Move || _drag == Drag.Resize)
                {
                    DrawGuides();
                }

                if (_drag == Drag.Marquee)
                {
                    DrawMarquee();
                }
            }

            DrawInfo(view);

            _view.position = offset;
            GUI.EndClip();
        }

        // Blue, or green while selected; all red while out of place - partly off the canvas (never seen in game)
        // or over another element on its layer; yellow edges while a drag presses it against another on its layer.
        // A part is a thin outline while it shows (see EasyUIWindow.Parts.cs), green while selected.
        private void DrawNode(EasyUINode node)
        {
            if (node.IsPart)
            {
                DrawPart(node);
                return;
            }

            var rect = node.Rect;
            var selected = IsSelected(node);
            var problem = Problem(node);
            var invalid = problem != null;
            var heldBack = IsHeldBack(node);
            var screen = ToScreen(rect);

            EditorGUI.DrawRect(screen, invalid ? InvalidFillColor : selected ? SelectedFillColor : IdleFillColor);
            var borderColor = heldBack ? HeldBackBorderColor : invalid ? InvalidBorderColor : selected ? SelectedBorderColor : IdleBorderColor;
            DrawBorder(screen, borderColor, selected || invalid || heldBack ? 2f : 1f);

            if (HasLabel(node))
            {
                DrawNodeLabel(node, screen, problem);
            }

            DrawPivot(rect, node.pivot);
        }

        private void DrawPart(EasyUINode node)
        {
            if (!IsRevealed(node))
            {
                return;
            }

            var screen = ToScreen(node.Rect);
            if (IsSelected(node))
            {
                EditorGUI.DrawRect(screen, SelectedFillColor);
                DrawBorder(screen, SelectedBorderColor, 2f);
                DrawNodeLabel(node, screen, null);
                DrawPivot(node.Rect, node.pivot);
                return;
            }

            DrawBorder(screen, PartBorderColor, 1f);
        }

        // Why the element is out of place, to show after its name - or null when it isn't. Parts are never out of
        // place: Unity lays them out.
        private string Problem(EasyUINode node)
        {
            if (node.IsPart)
            {
                return null;
            }

            if (!IsInside(node.Rect, CanvasRect))
            {
                return "outside the canvas";
            }

            var parent = document.Find(node.parentId);
            if (parent != null && !IsInside(node.Rect, parent.Rect))
            {
                return $"outside {NameOf(parent)}";
            }

            return IsInConflict(node, out var other) ? $"overlaps {NameOf(other)} on layer {document.DepthOf(node)}" : null;
        }

        private string NameOf(EasyUINode node) =>
            string.IsNullOrEmpty(node.name) ? EasyUINode.DefaultName(node.type) : node.name;

        // The pivot, as a ring like the Sprite Editor's (pivot y counts up, as in Unity).
        private void DrawPivot(Rect rect, Vector2 pivot)
        {
            var point = ToScreen(new Vector2(rect.x + rect.width * pivot.x, rect.y + rect.height * (1f - pivot.y)));
            GUI.DrawTexture(new Rect(point.x - PivotSize * 0.5f, point.y - PivotSize * 0.5f, PivotSize, PivotSize), PivotTexture);
        }

        // Lines every `step` canvas units, drawn only inside `area`; too dense to read at this zoom, none at all.
        private void DrawLines(Rect area, float step, Color color)
        {
            if (area.width <= 0f || area.height <= 0f || step * zoom < MinLineGap)
            {
                return;
            }

            var min = ToCanvas(area.min);
            var max = ToCanvas(area.max);

            for (var x = Mathf.Ceil(min.x / step) * step; x <= max.x; x += step)
            {
                var screenX = Mathf.Round(ToScreen(new Vector2(x, 0f)).x);
                EditorGUI.DrawRect(new Rect(screenX, area.y, 1f, area.height), color);
            }

            for (var y = Mathf.Ceil(min.y / step) * step; y <= max.y; y += step)
            {
                var screenY = Mathf.Round(ToScreen(new Vector2(0f, y)).y);
                EditorGUI.DrawRect(new Rect(area.x, screenY, area.width, 1f), color);
            }
        }

        private static void DrawBorder(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
        }

        private static Rect Intersect(Rect a, Rect b) =>
            Rect.MinMaxRect(Mathf.Max(a.xMin, b.xMin), Mathf.Max(a.yMin, b.yMin), Mathf.Min(a.xMax, b.xMax), Mathf.Min(a.yMax, b.yMax));

        #endregion

        #region Scene

        // The scene's first screen-space root canvas: the one the game's UI is drawn on. Searched once, and
        // again only after the hierarchy changed (a canvas added, removed, or a scene opened).
        private Canvas FindCanvas()
        {
            if (_canvasSearched)
            {
                return _canvas;
            }

            _canvasSearched = true;
            _canvas = null;
            foreach (var canvas in FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.InstanceID))
            {
                if (canvas.isRootCanvas && canvas.renderMode != RenderMode.WorldSpace)
                {
                    _canvas = canvas;
                    break;
                }
            }

            return _canvas;
        }

        // The root canvas's own rect follows the Game view's size and the Canvas Scaler.
        private static Vector2 CanvasSizeOf(Canvas canvas)
        {
            if (canvas == null)
            {
                return Vector2.zero;
            }

            var size = ((RectTransform)canvas.transform).rect.size;
            return size.x > 0f && size.y > 0f ? size : Vector2.zero;
        }

        #endregion
    }
}
