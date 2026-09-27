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

        /// <summary>The design, always up to date (see <see cref="EasyUIDocument.Upgrade"/>) - the asset itself changes only when saved again.</summary>
        public EasyUIDocument Document => document;

        private void OnEnable()
        {
            var canvas = document.canvasSize.x > 0f && document.canvasSize.y > 0f ? document.canvasSize : EasyUIDocument.FallbackCanvasSize;
            document.Upgrade(canvas);
        }

        internal void Store(EasyUIDocument design) => document = design.Clone();
    }
}
