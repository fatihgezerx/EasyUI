using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace EasyUI
{
    // Undo (Ctrl + Z) and redo (Ctrl + Y), up to MaxSteps each way.
    //
    // A step is the whole design as it was, kept as JSON: whatever changed it - a drag, a field of the panel on the
    // right, a new element and its name - one snapshot brings it back. Nothing has to record itself: when the next
    // press begins (and on undo / redo), the design is compared with the last one kept, and if it changed, that
    // one becomes a step. A drag, a name being typed or a value being dragged is one step, taken once it is over.
    // How the panel on the right is folded isn't part of the design, so a foldout alone makes no step.
    //
    // The shortcuts belong to this window: while it has focus they replace Unity's own undo and redo, and a text
    // field being typed in keeps its own.
    internal sealed partial class EasyUIWindow
    {
        private const int MaxSteps = 10;

        private static readonly Regex FoldoutField = new("\"(?:open|paddingOpen|rectTransformOpen)\":(?:true|false),?", RegexOptions.Compiled);

        // Oldest first; the last one is the next undone / redone.
        private readonly List<string> _undoSteps = new(MaxSteps);
        private readonly List<string> _redoSteps = new(MaxSteps);

        // The design as last kept, and the same without its foldouts, to compare with; null until kept.
        private string _kept;
        private string _keptDesign;

        [Shortcut("Easy UI/Undo", typeof(EasyUIWindow), KeyCode.Z, ShortcutModifiers.Action)]
        private static void UndoShortcut(ShortcutArguments args)
        {
            if (args.context is EasyUIWindow window)
            {
                window.UndoStep();
            }
        }

        [Shortcut("Easy UI/Redo", typeof(EasyUIWindow), KeyCode.Y, ShortcutModifiers.Action)]
        private static void RedoShortcut(ShortcutArguments args)
        {
            if (args.context is EasyUIWindow window)
            {
                window.RedoStep();
            }
        }

        // A new or opened panel starts a history of its own.
        private void ClearHistory()
        {
            _undoSteps.Clear();
            _redoSteps.Clear();
            _kept = null;
            _keptDesign = null;
        }

        // Called on every event once the layout has settled: the design is kept the first time, and checked again
        // when a press begins. Not in the middle of something - a drag, a name or a field being typed - which
        // becomes a step only once it is over.
        private void TrackHistory()
        {
            var type = Event.current.type;
            if (_kept != null && type != EventType.MouseDown && type != EventType.KeyDown)
            {
                return;
            }

            if (_drag == Drag.None && !_panning && _renamingId == 0 && GUIUtility.hotControl == 0 && !EditorGUIUtility.editingTextField)
            {
                KeepDesign();
            }
        }

        // Makes the design as last kept a step, if it has changed since.
        private void KeepDesign()
        {
            var current = EditorJsonUtility.ToJson(document);
            var design = WithoutFoldouts(current);
            if (_kept != null && design != _keptDesign)
            {
                Push(_undoSteps, _kept);
                _redoSteps.Clear();
            }

            _kept = current;
            _keptDesign = design;
        }

        private void UndoStep() => Step(_undoSteps, _redoSteps);

        private void RedoStep() => Step(_redoSteps, _undoSteps);

        // Goes back to the last step of `from`, which the design as it is now goes on the end of `to` for.
        private void Step(List<string> from, List<string> to)
        {
            if (_drag != Drag.None || GUIUtility.hotControl != 0)
            {
                return;
            }

            ConfirmRename();
            KeepDesign();
            if (from.Count == 0)
            {
                return;
            }

            var json = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            Push(to, _kept);

            var restored = new EasyUIDocument();
            EditorJsonUtility.FromJsonOverwrite(json, restored);
            document = restored;
            _kept = json;
            _keptDesign = WithoutFoldouts(json);

            PruneSelection();
            _hoverEdges = Edges.None;
            _hoverPencilId = 0;
            Repaint();
        }

        // How the panel on the right is folded isn't part of the design.
        private static string WithoutFoldouts(string json) => FoldoutField.Replace(json, string.Empty);

        // The oldest step goes once there are MaxSteps.
        private static void Push(List<string> steps, string json)
        {
            if (steps.Count == MaxSteps)
            {
                steps.RemoveAt(0);
            }

            steps.Add(json);
        }
    }
}
