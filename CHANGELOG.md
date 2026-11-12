# Changelog

## Convert from the scene, Name field
- New `Runtime/` (`EasyUI.Runtime`) with the `EasyUIConverter` component: on the top object of a panel laid out by hand
  under a canvas, its Inspector (`Editor/Convert/EasyUIConverterEditor`) has a Convert to Easy UI Data button and an Easy UI
  Panel field. `EasyUISceneConverter` reads the object tree into an `EasyUIDocument` (kinds, rect, anchors, settings,
  parts, optional components, roles whose component belongs to one role) and saves it as an `EasyUIPanel`. Not kept is
  listed in one console warning. The component is stripped from builds (`IProcessSceneWithReport`).
- Converting into an existing panel keeps its roles (`EasyUIRoleCarry`): roles without a component of their own can't be read from the
  scene, so the old design's roles go back to the elements with the same name and place; the ones that found none are listed.
- A disabled graphic (Image, Raw Image, Text) or one under an inactive object of the panel is converted fully transparent.
- An inactive object gets Ignore Layout (Layout Element) so it no longer takes room in its parent's layout group when the panel is built.
- Conversion keeps what a rebuilt panel got wrong: a Button/Dropdown/Input Field with no Image is built invisible (no white box), a Button
  or Toggle with no label gets no default "Button" text, a Slider keeps its five pieces with their place and Image (`SliderSettings.styled`, `SliderPieceSettings`: Background, Fill Area, Fill, Handle
  Slide Area, Handle; Custom Look in both Inspectors), an Input Field keeps its text and placeholder look and where its text area, text and
  placeholder sit (`InputFieldSettings.styled`, `textStyle`, `placeholderStyle`, `viewport*`, `text*`, `placeholder*`; Custom Text Style in both Inspectors), and a root called "Panel (Copy) (1)" is left unnamed.
- Image, Raw Image and Text keep **Maskable** (`ImageSettings.maskable`, `RawImageSettings.maskable`, `TextSettings.maskable`; a chip in both
  Inspectors), read from the scene and applied when built - so e.g. a background a Cutout Mask must not hide stays out of it.
- The builder puts every element in the order the design has them: Unity's own pieces of a control (a Button's Text) were made first and
  stayed ahead of what the design added, which swapped an icon and a label in a layout group.
- Disabled Layout Group, Content Size Fitter, Layout Element, Canvas Group, Mask and Rect Mask 2D components are not read.
- `ScrollViewSettings.background` (Inspector: Background, in both the Studio and the IMGUI window): Unity's Scroll View background
  is read from the scene and applied when built; off, its Image is transparent (a panel saved before keeps it on).
- `EasyUI.Editor` now references `EasyUI.Runtime`; `EasyUIWindow.SaveFolderKey` is internal.
- The Inspector (Studio and IMGUI) has a Name field above the Rect Transform; the pencil on the canvas and the field share
  `SetName` (an empty name gives a part its own default name, not its type's).

## Editor (studio look)
- `Tools > Easy UI` is a UI Toolkit window on the Veneer when the kit is installed (`HAS_VENEER`): title bar with
  logo, state pill and toolbar buttons, a hierarchy / workspace / Inspector split, and the Inspector as cards on three tabs
  (Element, Details, Components). The hierarchy tree and the canvas workspace stay IMGUI inside `IMGUIContainer`s.
- Without the kit the classic IMGUI window is used unchanged.
- New `Editor/Setup` (`EasyUI.Setup`): `DependencyGuard` sets `HAS_VENEER` and offers to install the kit.
- New brand: `Editor/Brand/EasyUIBrand.cs` and `.uss` (violet #8E4EC6).
- Role extension point (`IEasyUIRoleProvider`, `IEasyUIBuildHandler`, `EasyUIBuild`, `EasyUIRoles`, `EasyUICustomRoles`) is unchanged.
