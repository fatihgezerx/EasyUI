#if HAS_VENEER
using Veneer;
using UnityEngine;

namespace EasyUI
{
    /// <summary>The Easy UI brand for the Veneer look: violet accent, logo and banner from <c>Editor/Brand</c>.</summary>
    internal static class EasyUIBrand
    {
        /// <summary>The accent class the brand style sheet answers to (the exact violet, instead of a kit preset).</summary>
        internal const string AccentClass = "easyui-accent";

        /// <summary>The cached brand instance; use it for the window and everything in it.</summary>
        internal static readonly VeneerBrand Value = VeneerBrand.FromAssets(
            name: "Easy UI",
            subtitle: "Panel editor for uGUI",
            accent: new Color32(0x8E, 0x4E, 0xC6, 255),
            logoPath: "Assets/Scripts/EasyUI/Editor/Brand/logo.png",
            bannerPath: "Assets/Scripts/EasyUI/Editor/Brand/banner.png",
            iconFolder: null,
            styleSheetPath: "Assets/Scripts/EasyUI/Editor/Brand/EasyUIBrand.uss",
            accentClass: AccentClass);
    }
}
#endif
