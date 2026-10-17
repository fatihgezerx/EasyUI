#if HAS_VENEER
using System;
using Veneer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace EasyUI
{
    // The studio shell of the Easy UI window (only compiled with the Veneer; otherwise the classic IMGUI
    // OnGUI of EasyUIWindow.cs runs). The window chrome is UI Toolkit: a title bar (logo, state pill, toolbar), a split
    // body - hierarchy on the left, workspace in the middle, Inspector on the right (EasyUIWindow.VeneerInspector.cs).
    //
    // Two parts stay IMGUI, each inside an IMGUIContainer, because they are immediate-mode code that has no UI Toolkit
    // equivalent worth the risk of changing how it behaves:
    //   - the hierarchy tree (rows, eye, fold arrows, selection, scrolling): EasyUIWindow.Hierarchy.cs, rows only;
    //   - the workspace (canvas, drag/resize/marquee, guides, zoom/pan, labels, rename field, info panel): everything
    //     the old OnGUI did between the toolbar and the Inspector, unchanged.
    // The old IMGUI toolbar and Inspector code is still compiled but no longer called.
    internal sealed partial class EasyUIWindow
    {
        private const float StudioInspectorWidth = 330f;
        private const float StudioSplitterWidth = 6f;

        // True while the workspace's IMGUI handler runs; text editing can only be ended from inside it.
        private static bool InWorkspaceGui;
        private static bool EndEditingPending;

        private IMGUIContainer workspace;
        private IMGUIContainer hierarchyView;
        private VisualElement hierarchyPanel;
        private VisualElement inspectorHost;
        private VisualElement noCanvasHint;
        private Label countLabel;
        private Label canvasPill;
        private Button showAllButton;
        private Button undoButton;
        private Button redoButton;
        private Button openButton;
        private TextField nameField;
        private bool syncScheduled;
        private bool unsavedCached;
        private double lastUnsavedCheck;
        private float splitterStart;
        private float splitterPointerStart;
        private bool splitterDragging;

        /// <inheritdoc />
        protected override VeneerBrand Brand => EasyUIBrand.Value;

        /// <inheritdoc />
        protected override string WindowTitle => Title;

        // Repaints every part of the window: the IMGUI containers do not redraw on their own, and the Inspector has to
        // follow whatever just changed. Hides EditorWindow.Repaint on purpose - every existing call site uses it.
        private new void Repaint()
        {
            base.Repaint();
            workspace?.MarkDirtyRepaint();
            hierarchyView?.MarkDirtyRepaint();
            RequestSync();
        }

        /// <inheritdoc />
        protected override void BuildToolbar(VisualElement toolbar)
        {
            nameField = new TextField { value = document.panelName, tooltip = "The panel's name" };
            nameField.AddToClassList("eu-name");
            nameField.RegisterValueChangedCallback(evt => document.panelName = evt.newValue);
            toolbar.Add(nameField);

            canvasPill = VeneerStyle.Pill(string.Empty, "idle");
            canvasPill.tooltip = "Size of the scene canvas the panel is designed on";
            VeneerStyle.SetDisplay(canvasPill, false);
            toolbar.Add(canvasPill);

            openButton = VeneerStyle.Button("Open", "folder", "Open a saved panel, or start a new one", () =>
            {
                // A name being typed is part of the design the menu may replace.
                ConfirmRename();
                ShowOpenMenu(openButton.worldBound);
            }, false, Brand);
            toolbar.Add(openButton);

            toolbar.Add(VeneerStyle.Button("Save", "save", "Choose where to save this panel", () =>
            {
                SavePanel();
                StudioPoll();
            }, true, Brand));

            undoButton = VeneerStyle.IconButton("undo", "Undo (Ctrl + Z, up to ten steps)", () => UndoStep(), Brand);
            redoButton = VeneerStyle.IconButton("redo", "Redo (Ctrl + Y)", () => RedoStep(), Brand);
            toolbar.Add(undoButton);
            toolbar.Add(redoButton);
            toolbar.Add(VeneerStyle.IconButton("fit", "Frame the selection, or the canvas (F)", () =>
            {
                _frameRequested = true;
                Repaint();
            }, Brand));
            toolbar.Add(VeneerStyle.IconButton("trash", "Remove every element from this panel", ClearPanel, Brand));
            toolbar.Add(VeneerStyle.IconButton("help", "Show the mouse and key actions of the workspace", () =>
            {
                _infoOpen = !_infoOpen;
                Repaint();
            }, Brand));
        }

        /// <inheritdoc />
        protected override void BuildContent(VisualElement content)
        {
            RootElement.AddToClassList("eu-window");
            _panelShown = false;
            _panelNodeId = 0;
            syncScheduled = false;
            unsavedCached = false;
            lastUnsavedCheck = 0d;

            noCanvasHint = VeneerStyle.Hint("There is no Canvas in the scene. Add one (GameObject > UI > Canvas): this editor shows its area, where the panel goes.",
                "warn", Brand);
            content.Add(noCanvasHint);

            var body = new VisualElement();
            body.AddToClassList("eu-body");
            content.Add(body);

            hierarchyPanel = BuildHierarchyPanel();
            body.Add(hierarchyPanel);

            var splitter = new VisualElement();
            splitter.AddToClassList("eu-splitter");
            splitter.RegisterCallback<PointerDownEvent>(OnSplitterDown);
            splitter.RegisterCallback<PointerMoveEvent>(OnSplitterMove);
            splitter.RegisterCallback<PointerUpEvent>(OnSplitterUp);
            body.Add(splitter);

            var host = new VisualElement();
            host.AddToClassList("eu-workspace");
            workspace = new IMGUIContainer(OnWorkspaceGUI) { focusable = true };
            workspace.style.flexGrow = 1f;
            // A press in the workspace takes the keyboard from any field of the Inspector, so Delete and F reach it again.
            workspace.RegisterCallback<PointerDownEvent>(_ => workspace.Focus());
            host.Add(workspace);
            body.Add(host);

            inspectorHost = new VisualElement();
            inspectorHost.AddToClassList("eu-inspector");
            body.Add(inspectorHost);

            // A press anywhere starts a new undo step if the design changed since the last one (as the IMGUI window did).
            RootElement.RegisterCallback<PointerDownEvent>(_ => TrackPress(), TrickleDown.TrickleDown);

            ResetInspectorState();
            RebuildInspector();
            StudioPoll();
        }

        private VisualElement BuildHierarchyPanel()
        {
            var panel = new VisualElement();
            panel.AddToClassList("eu-panel");
            panel.style.width = hierarchyWidth;

            var header = new VisualElement();
            header.AddToClassList("eu-panel__header");
            header.Add(VeneerStyle.IconElement("list", "vn-icon--accent", Brand));
            var title = new Label("HIERARCHY");
            title.AddToClassList("eu-panel__title");
            header.Add(title);
            countLabel = VeneerStyle.MutedLabel(string.Empty);
            header.Add(countLabel);
            header.Add(VeneerStyle.Spacer());
            showAllButton = VeneerStyle.Button("Show all", "eye", "Show every hidden element", () =>
            {
                hiddenIds.Clear();
                UpdateHidden();
                Repaint();
            }, false, Brand);
            VeneerStyle.SetDisplay(showAllButton, false);
            header.Add(showAllButton);
            panel.Add(header);

            hierarchyView = new IMGUIContainer(OnHierarchyGUI);
            hierarchyView.style.flexGrow = 1f;
            panel.Add(hierarchyView);
            return panel;
        }

        #region Splitter

        private void OnSplitterDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }

            splitterDragging = true;
            splitterStart = hierarchyWidth;
            splitterPointerStart = evt.position.x;
            ((VisualElement)evt.currentTarget).CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnSplitterMove(PointerMoveEvent evt)
        {
            if (!splitterDragging)
            {
                return;
            }

            var body = hierarchyPanel.parent;
            var most = Mathf.Max(HierarchyMinWidth, body.layout.width - HierarchyMinView - StudioInspectorWidth - StudioSplitterWidth * 2f);
            hierarchyWidth = Mathf.Clamp(splitterStart + evt.position.x - splitterPointerStart, HierarchyMinWidth, most);
            hierarchyPanel.style.width = hierarchyWidth;
            evt.StopPropagation();
        }

        private void OnSplitterUp(PointerUpEvent evt)
        {
            if (!splitterDragging)
            {
                return;
            }

            splitterDragging = false;
            ((VisualElement)evt.currentTarget).ReleasePointer(evt.pointerId);
            evt.StopPropagation();
        }

        #endregion

        #region IMGUI parts

        // Everything the old OnGUI did between the toolbar and the Inspector, with the workspace's own space as the view.
        private void OnWorkspaceGUI()
        {
            InWorkspaceGui = true;
            try
            {
                var e = Event.current;
                var raw = e.rawType;

                // A keyboard-type control (never focused) makes the container count as focusable, so Delete, F and the
                // Duplicate command reach the workspace once it was clicked.
                GUIUtility.GetControlID(FocusType.Keyboard);
                if (EndEditingPending)
                {
                    EndEditingPending = false;
                    EndTextEditing();
                }

                var canvas = FindCanvas();
                _canvasSize = CanvasSizeOf(canvas);
                _drawnCanvasSize = _canvasSize;
                FollowCanvas();
                UpdateGridSteps();
                _drawnCanvasId = canvas != null ? canvas.GetInstanceID() : 0;
                _view = new Rect(Vector2.zero, workspace.contentRect.size);

                var hasCanvas = canvas != null && _canvasSize != Vector2.zero;
                if ((!framed || _frameRequested) && e.type == EventType.Repaint && hasCanvas)
                {
                    _frameRequested = false;
                    Frame(framed && SelectionBounds(out var selection) ? selection : CanvasRect);
                }

                if (!hasCanvas)
                {
                    ClearSelection();
                    _drag = Drag.None;
                }

                UpdateHidden();
                UpdateLayout();
                TrackHistory();
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

                if (raw == EventType.MouseDown || raw == EventType.MouseDrag || raw == EventType.MouseUp
                    || raw == EventType.KeyDown || raw == EventType.ExecuteCommand || raw == EventType.ScrollWheel)
                {
                    RequestSync();
                }
            }
            finally
            {
                InWorkspaceGui = false;
            }
        }

        // The tree's rows, in the container's own space (the header and the splitter are UI Toolkit now).
        private void OnHierarchyGUI()
        {
            var area = new Rect(Vector2.zero, hierarchyView.contentRect.size);
            if (area.width <= 0f || area.height <= 0f)
            {
                return;
            }

            var e = Event.current;
            UpdateHidden();

            // Selecting in the workspace shows the row: everything above it unfolds.
            if (_selectedId != _revealedFor)
            {
                _revealedFor = _selectedId;
                Unfold(document.Find(_selectedId));
            }

            BuildRows();
            var hasPointer = area.Contains(e.mousePosition);
            var content = new Rect(0f, 0f, Mathf.Max(ContentWidth(), area.width - 16f), _rows.Count * RowHeight);
            _hierarchyScroll = GUI.BeginScrollView(area, _hierarchyScroll, content);

            var first = Mathf.Clamp(Mathf.FloorToInt(_hierarchyScroll.y / RowHeight), 0, Mathf.Max(_rows.Count - 1, 0));
            var last = Mathf.Min(_rows.Count - 1, first + Mathf.CeilToInt(area.height / RowHeight) + 1);
            for (var i = first; i <= last; i++)
            {
                DrawRow(_rows[i], i, content.width, e, hasPointer);
            }

            GUI.EndScrollView();
        }

        #endregion

        #region State sync

        // A press in the UI Toolkit parts: the design is checked for changes before the press does anything.
        private void TrackPress()
        {
            if (_drag == Drag.None && !_panning && _renamingId == 0 && GUIUtility.hotControl == 0 && !EditorGUIUtility.editingTextField)
            {
                KeepDesign();
            }
        }

        // Updates the Inspector soon (30 ms): coalesces the many Repaint calls of one interaction into one sync.
        private void RequestSync()
        {
            if (syncScheduled || RootElement == null)
            {
                return;
            }

            syncScheduled = true;
            RootElement.schedule.Execute(() =>
            {
                syncScheduled = false;
                SyncInspector();
            }).ExecuteLater(30);
        }

        // About ten times a second (OnInspectorUpdate): title bar state, toolbar buttons, hierarchy header, Inspector values.
        private void StudioPoll()
        {
            if (RootElement == null || nameField == null)
            {
                return;
            }

            var canvas = FindCanvas();
            _warnNoCanvas = canvas == null;
            VeneerStyle.SetDisplay(noCanvasHint, _warnNoCanvas);

            var now = EditorApplication.timeSinceStartup;
            if (now - lastUnsavedCheck > 0.5d)
            {
                lastUnsavedCheck = now;
                unsavedCached = HasUnsavedChanges();
            }

            if (_warnNoCanvas)
            {
                SetPill("No canvas", "error");
            }
            else if (unsavedCached)
            {
                SetPill("Unsaved changes", "warn");
            }
            else
            {
                SetPill("Saved", "ok");
            }

            var size = CanvasSizeOf(canvas);
            VeneerStyle.SetDisplay(canvasPill, size != Vector2.zero);
            if (size != Vector2.zero)
            {
                VeneerStyle.SetPill(canvasPill, "idle", $"{size.x:0} x {size.y:0}");
            }

            if (!IsEditing(nameField) && nameField.value != document.panelName)
            {
                nameField.SetValueWithoutNotify(document.panelName);
            }

            undoButton.SetEnabled(_undoSteps.Count > 0);
            redoButton.SetEnabled(_redoSteps.Count > 0);
            VeneerStyle.SetDisplay(showAllButton, hiddenIds.Count > 0);
            countLabel.text = document.nodes.Count + (document.nodes.Count == 1 ? " element" : " elements");
            SyncInspector();
        }

        #endregion
    }
}
#endif
