# Changelog

## Editor (studio look)
- `Tools > Easy UI` is a UI Toolkit window on the Veneer when the kit is installed (`HAS_VENEER`): title bar with
  logo, state pill and toolbar buttons, a hierarchy / workspace / Inspector split, and the Inspector as cards on three tabs
  (Element, Details, Components). The hierarchy tree and the canvas workspace stay IMGUI inside `IMGUIContainer`s.
- Without the kit the classic IMGUI window is used unchanged.
- New `Editor/Setup` (`EasyUI.Setup`): `DependencyGuard` sets `HAS_VENEER` and offers to install the kit.
- New brand: `Editor/Brand/EasyUIBrand.cs` and `.uss` (violet #8E4EC6).
- Role extension point (`IEasyUIRoleProvider`, `IEasyUIBuildHandler`, `EasyUIBuild`, `EasyUIRoles`, `EasyUICustomRoles`) is unchanged.
