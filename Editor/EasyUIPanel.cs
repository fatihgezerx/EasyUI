using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// A saved Easy UI panel: the design as last saved from the <see cref="EasyUIWindow"/>, opened from there
    /// again to keep editing it.
    /// </summary>
    public sealed class EasyUIPanel : ScriptableObject
    {
        [SerializeField] private EasyUIDocument document = new();

        public EasyUIDocument Document => document;

        internal void Store(EasyUIDocument design) => document = design.Clone();
    }
}
