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
  Pos X / Y, Width / Height or Left / Right / Top / Bottom, pivot - each field changed by dragging its label
  left or right too (Shift faster, Alt finer)
- Optional components from the round **+**: Content Size Fitter, Canvas Group, Horizontal / Vertical / Grid
  Layout Group (one at most), Layout Element, Mask, Rect Mask 2D
- Parts: a Scroll View's Viewport, Content and scrollbars, a Button's Text, a Toggle's Background, Checkmark
  and Label, and a Dropdown's Label and Arrow are elements of their own, to style and extend
- Roles: other systems (e.g. Inventory System) can offer roles, so they find your elements without names -
  several per element, and each can add its component or have its system set the element up when built
- Layers: an element's layer is how deep it sits, and elements on the same layer can't overlap
- Smart guides that pull edges and middles onto lined-up elements, and Shift to snap to the canvas grid
- Multi-selection, box selection, duplicate (Ctrl + D) and delete, with children
- Undo (Ctrl + Z) and redo (Ctrl + Y), up to ten steps
- A root element for every panel: the window itself, stretched over the canvas, holding everything else
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
| Select what is inside (a child, a part) | Click the selected element again |
| Add to / remove from the selection | Ctrl + click |
| Select with a box | Drag on an empty spot |
| Move the selection | Drag a selected element |
| Resize | Drag an edge or a corner |
| Snap to the grid | Hold Shift while dragging |
| Duplicate | Ctrl + D |
| Delete | Delete |
| Rename | Click the pencil |
| Undo (up to 10 steps) | Ctrl + Z |
| Redo | Ctrl + Y |
| Frame the selection (or the canvas) | F |
| Pan | Middle drag, or Alt + drag |
| Zoom | Scroll wheel |

The round info button in the bottom-right corner of the workspace lists these too.

A new element goes under the selected one, or under the root element when nothing is selected, and asks
for its name right away. Enter or the check button keeps the name, Escape leaves it as it was. The first
press on an element only selects it, so an element never moves by accident. A drag always starts from a
selected element.

Clicks go down through elements one level at a time: the first click picks the outermost element under the
pointer, and each click on the selected element picks what is inside it there (a child, or a part). With
nothing deeper there, the next click goes back to the outermost element. A drag on the selected element
still moves it, even where a child covers it. The siblings of the selection, and of its parents, are one
click away.

Labels never lie on top of each other: where two would, the selected element's shows, otherwise the outer
element's.

Selected elements are green. The element clicked last is the one the inspector shows and the one resized
by its edges.

Undo and redo go back and forth ten steps at most. A step is whatever changed the design between two
presses: a drag, a value set in the inspector, a new element and its name. Opening a folded section of the
inspector isn't one. While the window has focus, Ctrl + Z and Ctrl + Y are its own instead of Unity's; a
text field being typed in keeps its own. A new or opened design starts a history of its own.

## Layers

An element's layer is how deep it sits: 1 for the root element, 2 for its children, and so on. It is shown
in a small badge before the element's name.

Elements on different layers overlap freely, since a child sits inside its parent. Elements on the same
layer may not: a drag or a resize stops where it would run into another one, and both show yellow edges
while it presses against it. An element that overlaps one on its layer anyway (e.g. placed there from the
Rect Transform fields) turns red.

An element with a **Layout Element** whose **Ignore Layout** is ticked is out of this rule: it may lie over
the others on its layer - e.g. a background image stretched under the children of a layout group - and never
stops a drag. It still stays inside its parent.

## Parents and children

A child always stays inside its parent. Moving or resizing an element takes its children along the way a
RectTransform's anchors do: an anchored side keeps its distance from its anchor, and a stretched side keeps
its distance from the parent's edge. A child stretched across its parent narrows with it.

A drag never takes an element out of its parent, or the root element out of the canvas. One
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

A **Mask** needs a graphic to clip to: an element without one (e.g. Empty) gets a plain Image for it when
built. A **Rect Mask 2D** clips to the element's rect, with Padding and Softness.

## Layout in the workspace

Layout components work in the workspace as they will in the game, worked out the way uGUI works them out:

- A **Content Size Fitter** sizes its element (around its pivot) to its min or preferred size, e.g. a
  Content with a Horizontal Layout Group grows with its children.
- A **Horizontal / Vertical Layout Group** places its children one after another, with its padding,
  spacing, child alignment and reverse arrangement; with **Control Child Size** it sizes them too, using
  **Child Force Expand** and their Layout Elements' flexible sizes to share out spare room.
- A **Grid Layout Group** puts every child in a cell of its Cell Size, by its start corner, start axis and
  constraint.
- A **Layout Element** gives an element's min, preferred and flexible sizes (or keeps it out of layout
  with Ignore Layout).

An element a layout group places, or a fitter sizes, snaps back if it is dragged or its fields are changed:
its Rect Transform is driven, as in Unity, and the inspector says by what. Preferred sizes are uGUI's: a
Layout Element's, a layout group's, an Image's sprite size, a Raw Image's texture size. A text keeps its
current size, since uGUI measures its text and the workspace can't.

## Parts

Unity builds a Scroll View, Button, Toggle or Dropdown out of several objects. In EasyUI those objects are
**parts**: elements of their own, made with their element and laid out as Unity lays them out.

| Element | Parts |
|---|---|
| Scroll View | Viewport > Content, Scrollbar Horizontal > Handle, Scrollbar Vertical > Handle |
| Button | Text |
| Toggle | Background > Checkmark, Label |
| Dropdown | Label, Arrow |

A part can be selected, renamed, restyled (its Image or Text section), given components with **+** and
children of its own - e.g. a Grid Layout Group and slots in a Scroll View's Content. It goes with its
element: it can't be dragged, resized on the canvas, duplicated or deleted on its own, and the layer rules
leave it out (a scrollbar sits over its Viewport, a Content may be taller than it). Its Rect Transform is
still edited in the inspector, except a scrollbar's Handle, which its Scrollbar sizes at runtime.

An element looks like one piece until it is selected; then its parts show as thin outlines. To select a
part, click the selected element again, as for a child: each click goes one part deeper (Scroll View, then
Viewport, then Content). The **Parts** menu in the inspector selects any part directly. A Scroll View's scrollbars exist
while they are ticked in its settings, and elements added under a Scroll View go into its Content.

Panels saved before parts existed get theirs the first time they are opened or built: a Button's text
becomes its Text part, and what was under a Scroll View moves into its Content.

## Roles

Another system can tell EasyUI what it needs from a panel by offering **roles**, e.g. Inventory System's
*Slot Container* or *Examine View*, listed under the system's name (*Inventory*). The inspector then shows
the element's **Roles** at the top, each with a cross that takes it off, and **Add Role**: a menu of the
roles that fit the element's type (a text's roles on a Text, an image's on an Image...), plus your own roles
(below). The roles show in brackets after the element's name.

An element can have several roles - e.g. a text that is both a prompt's label and excluded from
translation, or a slot that is both clickable and draggable. A role may **conflict** with another (the two
can't be on one element) or **require** one (e.g. *Clickable Slot* needs *Slot Template*): the menu shows
such a role greyed out, saying why, and taking a role off also takes off those that required it. Most
roles are held by one element at most, so giving one to another takes it from the first; a system may
offer roles several elements share, telling them apart by where they are (e.g. Inventory System's *Item
Icon*: in a slot, in a notification, or on its own).

When the panel is built from **GameObject > UI (Canvas) > Easy UI**, each element gets its roles'
components, then every system sets up what it knows - its views, wired to each other - by its roles, never
by name, so elements can be named freely. EasyUI never references those systems:

- `IEasyUIRoleProvider` offers roles (`EasyUIRole`: id, menu path, description, element types, unique or
  not, and optionally a `Component` to add, `Conflicts` and `Requires`).
- `IEasyUIBuildHandler` is told of every build (`EasyUIBuild`: the design, the root, the canvas, and the
  object of every element by role), after the roles' components were added.
- `EasyUIPanelBuilder.Create` builds a panel as the menu does; `EasyUIPanelBuilder.Build` builds it without
  the handlers and returns every element's object by its id.

### Roles of your own

**Add Role...**, at the top of the Add Role menu, makes a role from a script: pick the script (a
MonoBehaviour), optionally a **Group**, and press **Apply**. The role is named after the script's class,
given to the element, and listed in the Role menu of every element from then on (they are kept per project,
in `ProjectSettings/EasyUIRoles.asset`). A group is a submenu of the Role menu: type a new one, or pick one
there is (a system's, e.g. *Inventory*, or yours) from the button beside the field. A name typed like an
existing group, whatever its case, is that group - there is never a second one of the same name. When the panel is built, an element with it gets that script as a
component. Several elements can share one, and the same window lists the roles made so far, to remove
them. So a system of your own - an inventory you wrote yourself, say - can mark its elements without any
code for EasyUI.

## The root element

Every design starts with its **root element**: an Empty stretched over the whole canvas, which holds every
other element and is itself the window - what gets built. It is where a system's window role goes (e.g.
Inventory System's *Inventory Panel*). It can be moved, resized, re-anchored, renamed and given roles, but
not deleted or duplicated, and elements added with nothing selected go in it. Without a name of its own, it
is called after the panel. When the canvas changes size (e.g. the Game view's), it follows as its anchors
say - stretched, it keeps covering the canvas - and everything in it follows it.

Panels saved by an older EasyUI get one the first time they are opened or built: a Panel's elements are
put in a new root over the whole canvas, and a Popup's Popup element becomes its root.

## Saving

The top bar holds the design's name and three buttons:

- **Open**: a new design, or any saved one.
- **Clear**: removes every element but the root, after asking. Ctrl + Z brings them back.
- **Save**: asks where to store the panel, as an `EasyUIPanel` asset. The dialog offers the panel's name
  and starts in the folder of the panel being edited, or else in the folder saved to last (remembered per
  project). Saving over another panel asks before replacing it. The panel takes the file's name.

The canvas's size is saved with the panel, so its elements can be anchored to a parent of any size when
it is built.

Whatever replaces the design being edited - a new or saved design from Open - asks first when it has
changes that aren't saved, and goes ahead at once when it hasn't.

## Adding a panel to a scene

Every saved panel is listed under **GameObject > UI (Canvas) > Easy UI** (also in the Hierarchy's
right-click menu), named after its asset. Choosing one builds the panel:

- under the selected object when it is inside a canvas, otherwise under the scene's canvas. Without a
  canvas, one is made, with an EventSystem (using the Input System's UI module when the project uses the
  Input System).
- as its root element, placed where it was drawn (a new root: stretched over its parent), with every
  other element under it. Each element is made the way
  Unity's own **UI (Canvas)** menu makes its kind, then set up as designed: anchors, pivot, its components'
  settings and the components added to it. Its parts are the objects Unity made for them, set up the same
  way. Toggle labels are TextMeshPro, like every other text.
- each element with its roles' components; then every system with roles in the panel sets it up (see
  Roles).
- in one undo step.

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

**2. Open `Tools > Easy UI`** and name your design in the top bar.

**3. Right-click on the canvas** to add elements. Select one to add its children under it. Drag, resize
and set them up in the inspector on the right.

**4. Press Save** and choose where to keep the panel. **Open** brings it back later.

**5. Build it into your scene:** right-click the canvas in the Hierarchy, then **UI (Canvas) > Easy UI >
<Panel Name>**.

## License

[MIT License](LICENSE)
