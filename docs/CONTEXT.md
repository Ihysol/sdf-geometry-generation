# Context Glossary

## FPS Edit Mode
A Scene View transformation (not a separate window) where orbit/pan controls are replaced by first-person WASD + mouse look. Entered/exited with `E`. The user navigates through the volume and modifies geometry via a center-screen cursor. _Avoid_ "game mode" or "play mode" — this is an editor camera, not runtime playback.

## Dot Cursor
A center-screen reticle that raycasts forward (clamped to min/max distance) to show where edits will apply. Shape adapts: square in cell-snap mode, circle in freeform mode. _Avoid_ "crosshair" or "reticle" — it's a projection point, not a weapon sight.

## Cell-Snap Mode
Dot cursor snaps to grid-aligned cells instead of freeform positions. Edits affect discrete volume cells rather than radial brush regions. Toggled within FPS Edit Mode. _Avoid_ "voxel mode" — the underlying buffer is SDF, not voxels.

## Brush Mode (Freeform)
Dot cursor operates freely in 3D space with a radial influence zone. Modifications follow smooth falloff curves rather than grid boundaries. Default state on entering FPS Edit Mode.

## Vertex Selection (`Q`)
Pressing `Q` while hovering a cell corner highlights and selects exactly one vertex for fine-grained displacement. _Avoid_ "point editing" — it operates on grid vertices, not arbitrary points.

## Multi-Selection Drag (Middle-click)
Holding the mouse wheel button and dragging extends the selection to multiple adjacent corners or edges. Scroll moves all selected vertices simultaneously along their respective normals.

## Face Modifier (`F`)
Pressing `F` selects all corners/edges on the currently hovered cell face. Scroll pushes or pulls the entire face uniformly. _Avoid_ "plane push" — it modifies grid geometry, not infinite planes.

## CellOperation
A persistent edit operation that creates or destroys discrete grid cells. Replays cleanly during rebuilds by marking affected cells as filled or empty. _Avoid_ "voxel edit" — operates on SDF grid cells, not voxels.

## VertexDisplacementOperation
A persistent edit operation that displaces one or more grid vertices along their normals. Replays by applying the same displacement vector relative to the cell's current position. Deferred until vertex editing is implemented.

## Selection Highlighting (C)
Wireframe overlay for cells; colored dots at selected vertices/edges. May evolve toward Cube 2 style later but starts with mixed approach for clarity across edit targets. _Avoid_ "face painting" — it's structural selection, not material coloring.

## Movement Controls (B)
Free WASD movement by default; hold Shift to snap camera position to cell boundaries for precision placement. _Avoid_ "grid walk" — snapping is optional, not mandatory.

## Multi-Cell Selection (Left-click drag)
Holding left-click and dragging selects a rectangular region of cells. Scroll modifies all selected cells simultaneously. _Avoid_ "brush selection" — it's grid-aligned, not radial.

## Group Move (Right-click drag)
Holding right-click on selected cells translates the entire group along with the mouse movement. Modifies cell positions rather than pushing geometry. _Avoid_ "drag-to-move" — it operates on multiple cells as a unit, not individual objects.

## Left-Click Behavior
In brush mode: does nothing (scroll is the only modification input). In cell-snap mode: drag to select multiple cells. Future: may support texture painting in brush mode. _Avoid_ "click-to-edit" — scroll drives all modifications.

## Texture Painting (Future)
Potential future feature for brush mode: left-click drags apply material/texture changes instead of geometry edits. Not yet implemented or designed.
