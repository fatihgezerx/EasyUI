# EasyUI

A Shader-Graph-like editor window for laying out uGUI panels in Unity: add, move, resize and set up
elements on a canvas-sized workspace, without touching the Hierarchy.

## Overview

`Tools > Easy UI` opens a dockable workspace: an endless grid with your scene's canvas drawn on it as a
brighter area, at the canvas's real size (e.g. 1920 x 1080 with a Canvas Scaler). A panel is designed on
it element by element. Right-click to add one, drag it into place, and set up its Rect Transform and
components in the inspector on the right, laid out like Unity's own.

Every element is a uGUI object with a Rect Transform, and parents and children behave like theirs: a child
sits inside its parent and follows it as its anchors say. Saved panels are assets you can open again to
keep editing, and each one appears in the Create menu under **UI (Canvas) > Easy UI**, which builds it into
the scene.

EasyUI is an editor tool only. It depends on nothing but uGUI and TextMeshPro and adds nothing to your
builds.

## Features

- Ten element types from the right-click menu, with search: Empty, Text (TextMeshPro), Image, Raw Image,
  Button, Toggle, Slider, Dropdown, Input Field, Scroll View
- Unity's default size, sprites and settings for every element, as `GameObject > UI` makes it
- Rect Transform editing like Unity's: anchor presets (Shift sets the pivot, Alt moves the element),
  Pos X / Y, Width / Height or Left / Right / Top / Bottom, pivot
- Optional components from the round **+**: Content Size Fitter, Canvas Group, Horizontal / Vertical / Grid
  Layout Group (one at most), Layout Element
- Layers: an element's layer is how deep it sits, and elements on the same layer can't overlap
- Smart guides that pull edges and middles onto lined-up elements, and Shift to snap to the canvas grid
- Multi-selection, box selection, duplicate (Ctrl + D) and delete, with children
- Inline renaming with a pencil icon, and names that fade out when they don't fit
- A red outline for anything out of place: off the canvas, outside its parent, or over another element
  on its layer
- Save / Open panels as `EasyUIPanel` assets, in any folder
- Every saved panel in `GameObject > UI (Canvas) > Easy UI`, building it as plain uGUI objects

## The workspace

| Action | Input |
|---|---|
| Add an element | Right-click |
| Select | Click |
| Add to / remove from the selection | Ctrl + click |
| Select with a box | Drag on an empty spot |
| Move the selection | Drag a selected element |
| Resize | Drag an edge or a corner |
| Snap to the grid | Hold Shift while dragging |
| Duplicate | Ctrl + D |
| Delete | Delete |
| Rename | Click the pencil |
| Frame the selection (or the canvas) | F |
| Pan | Middle drag, or Alt + drag |
| Zoom | Scroll wheel |

The round info button in the bottom-right corner of the workspace lists these too.

A new element goes under the selected one, or under the panel itself when nothing is selected, and asks
for its name right away. Enter or the check button keeps the name, Escape leaves it as it was. The first
press on an element only selects it, so an element never moves by accident. A drag always starts from a
selected element.

Selected elements are green. The element clicked last is the one the inspector shows and the one resized
by its edges.

## Layers

An element's layer is how deep it sits: 1 for the panel's own children, 2 for theirs, and so on. It is
shown in a small badge before the element's name.

Elements on different layers overlap freely, since a child sits inside its parent. Elements on the same
layer may not: a drag or a resize stops where it would run into another one, and both show yellow edges
while it presses against it. An element that overlaps one on its layer anyway (e.g. placed there from the
Rect Transform fields) turns red.

## Parents and children

A child always stays inside its parent. Moving or resizing an element takes its children along the way a
RectTransform's anchors do: an anchored side keeps its distance from its anchor, and a stretched side keeps
its distance from the parent's edge. A child stretched across its parent narrows with it.

A drag never takes an element out of its parent, or out of the canvas for the panel's own elements. One
that ends up outside anyway (its parent shrunk, or its fields put it there) turns red.

## Guides and snapping

While an element is dragged or resized, a thin blue line shows wherever one of its edges or its middle
lines up with an edge or the middle of another element or of the canvas. Coming within a few pixels of such
a line pulls the element onto it.

Hold **Shift** to snap to the canvas grid instead. The grid's steps fit the canvas exactly (1920 x 1080 is
16 x 9 large squares), so no half square is left at its edges.

## The inspector

The floating panel in the top-right corner shows the selected element: its **Rect Transform**, then the
components its type is built with, laid out like their own Inspectors.

| Element | Components |
|---|---|
| Empty | - |
| Text | Text |
| Image | Image |
| Raw Image | Raw Image |
| Button | Image (its background), Button |
| Toggle | Toggle |
| Slider | Slider |
| Dropdown | Image, Dropdown (with its options list) |
| Input Field | Image, Input Field |
| Scroll View | Scroll Rect settings and which scrollbars to build |

Every control starts with what all controls share: Interactable and its Transition (colors, sprites or
animation triggers). The round **+** under the components adds optional ones, each removed again with the
cross in its header. Every section folds away from its header.

## Saving

The top bar holds the panel's name and four buttons:

- **Open**: a new panel, or any saved one.
- **Templates**: ready-made panels to start from. None ship yet.
- **Clear**: removes every element, after asking.
- **Save**: asks where to store the panel, as an `EasyUIPanel` asset. The dialog offers the panel's name
  and starts in the folder of the panel being edited, or else in the folder saved to last (remembered per
  project). Saving over another panel asks before replacing it. The panel takes the file's name.

The canvas's size is saved with the panel, so its elements can be anchored to a parent of any size when
it is built.

## Adding a panel to a scene

Every saved panel is listed under **GameObject > UI (Canvas) > Easy UI** (also in the Hierarchy's
right-click menu), named after its asset. Choosing one builds the panel:

- under the selected object when it is inside a canvas, otherwise under the scene's canvas. Without a
  canvas, one is made, with an EventSystem (using the Input System's UI module when the project uses the
  Input System).
- as an object stretched over its parent, with every element under it. Each element is made the way
  Unity's own **UI (Canvas)** menu makes its kind, then set up as designed: anchors, pivot, its components'
  settings and the components added to it. Toggle labels are TextMeshPro, like every other text.
- in one undo step.

A Scroll View's children go into its Content, which starts as large as the view. Its Content Size Fitter
and Layout Group go on the Content too, since that is what they size and lay out.

The built objects keep no link to the panel asset: from then on they are the scene's own, so add your
scripts and change them freely. Saving the panel again doesn't touch objects already built.

Unity's menus come from code, so the list is a generated script, `Editor/Generated/EasyUIPanelMenu.cs`. It
is written again whenever a panel is saved, imported, renamed, moved or deleted, only when it changes, and
removed when no panel is left. It belongs to your project, so the repository ignores it.

## Setup

### Requirements

- Unity 2021.3 LTS or newer
- uGUI and TextMeshPro (`com.unity.ugui`), included by default. Texts without a font use the default font
  of TMP Settings, so import **TMP Essential Resources** once (`Window > TextMeshPro`).

### Installation

Clone or download this repository, then copy its contents into `Assets/Scripts/EasyUI/` (or anywhere under
`Assets/`). It compiles in its own editor-only assembly (`EasyUI.Editor`) and needs no other setup.

## Quick Start

**1. Add a Canvas to your scene** (`GameObject > UI > Canvas`). EasyUI shows its area, and its size, as the
space the panel goes in. Without one, the window tells you to add one.

**2. Open `Tools > Easy UI`** and name your panel in the top bar.

**3. Right-click on the canvas** to add elements. Select one to add its children under it. Drag, resize
and set them up in the inspector on the right.

**4. Press Save** and choose where to keep the panel. **Open** brings it back later.

**5. Build it into your scene:** right-click the canvas in the Hierarchy, then **UI (Canvas) > Easy UI >
<Panel Name>**.

## License

[MIT License](LICENSE)
