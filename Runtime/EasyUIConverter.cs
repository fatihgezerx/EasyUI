using UnityEngine;

namespace EasyUI
{
    /// <summary>
    /// Put on the top object of a panel laid out by hand under a canvas: its Inspector has a Convert to Easy UI Data
    /// button that saves the panel as an Easy UI panel asset. The component does nothing at run time and is taken out of
    /// every build.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Easy UI/Easy UI Converter")]
    public sealed class EasyUIConverter : MonoBehaviour
    {
        // The Easy UI panel asset this panel was last converted into (its GUID); the Inspector shows it as an object
        // field, so the asset type - which lives in the editor assembly - is not referenced here.
        [HideInInspector] [SerializeField] private string panelGuid = string.Empty;

        /// <summary>The GUID of the panel asset the next conversion replaces; empty when it asks where to save.</summary>
        public string PanelGuid
        {
            get => panelGuid;
            set => panelGuid = value ?? string.Empty;
        }
    }
}
