using System;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// The right-click menu of the <see cref="EasyUIWindow"/>, like Shader Graph's "Create Node": a titled list of
    /// every element (there are few, so no categories), with a search field on top that narrows it as you type.
    /// </summary>
    internal sealed class ElementDropdown : AdvancedDropdown
    {
        private readonly Action<EasyUIElementType> _picked;

        public ElementDropdown(AdvancedDropdownState state, Action<EasyUIElementType> picked) : base(state)
        {
            _picked = picked;
            minimumSize = new Vector2(260f, 320f);
        }

        protected override AdvancedDropdownItem BuildRoot()
        {
            var root = new AdvancedDropdownItem("Create Element");
            foreach (EasyUIElementType type in Enum.GetValues(typeof(EasyUIElementType)))
            {
                root.AddChild(new ElementItem(type));
            }

            return root;
        }

        protected override void ItemSelected(AdvancedDropdownItem item)
        {
            if (item is ElementItem element)
            {
                _picked(element.Type);
            }
        }

        private sealed class ElementItem : AdvancedDropdownItem
        {
            public ElementItem(EasyUIElementType type) : base(EasyUINode.DisplayName(type)) => Type = type;

            public EasyUIElementType Type { get; }
        }
    }
}
