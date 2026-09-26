using UnityEngine;

namespace EasyUI
{
    // Clicking down through elements, and how parts (see EasyUIParts.cs) behave in the workspace. The first click
    // picks the outermost element; a click on the selected element goes one level deeper each time (a child, or a
    // part: Scroll View, then Viewport, then Content) and, with nothing deeper there, back to the outermost one;
    // a drag on it still moves it. A composite element looks like one piece until it is selected; then its parts
    // show as thin outlines. A part is selected but never dragged, resized, duplicated or deleted on its own, and
    // the layer rules leave it out.
    internal sealed partial class EasyUIWindow
    {
        private static readonly Color PartBorderColor = new(0.75f, 0.6f, 1f, 0.9f);

        // Drawn while it, its parent, or something inside it is selected.
        private bool IsRevealed(EasyUINode node)
        {
            if (!node.IsPart || IsSelected(node))
            {
                return true;
            }

            var parent = document.Find(node.parentId);
            if (parent != null && IsSelected(parent))
            {
                return true;
            }

            foreach (var id in _selected)
            {
                var selected = document.Find(id);
                if (selected != null && document.IsUnder(selected, node))
                {
                    return true;
                }
            }

            return false;
        }

        // Clickable when it is one of the panel's own elements, or its parent is selected or holds something
        // selected: so the first click picks the outermost element, each click on the selection goes one level
        // deeper, and the siblings of what is selected (and of its parents) stay one click away. Parts follow the
        // same rule, so a composite element is picked whole first.
        private bool IsClickable(EasyUINode node)
        {
            var parent = document.Find(node.parentId);
            if (parent == null)
            {
                return !node.IsPart;
            }

            foreach (var id in _selected)
            {
                var selected = document.Find(id);
                if (selected != null && document.IsUnder(selected, parent))
                {
                    return true;
                }
            }

            return false;
        }

        // The panel's own element `node` is in (itself when it is one).
        private EasyUINode RootOf(EasyUINode node)
        {
            var current = node;
            for (var guard = 0; guard < 64; guard++)
            {
                var parent = document.Find(current.parentId);
                if (parent == null)
                {
                    return current;
                }

                current = parent;
            }

            return current;
        }

        // The nearest selected element above `node`, or null.
        private EasyUINode SelectedAncestor(EasyUINode node)
        {
            var current = document.Find(node.parentId);
            for (var guard = 0; current != null && guard < 64; guard++)
            {
                if (IsSelected(current))
                {
                    return current;
                }

                current = document.Find(current.parentId);
            }

            return null;
        }
    }
}
