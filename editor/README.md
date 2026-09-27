# AtEnd Chart Editor

This directory is a standalone Godot 4 C# project for chart authoring. It is
not compiled into or launched from the AtEnd game project.

The editor references `../src/AtEnd.Core` so the game and editor use the same
chart format, timing model, and validation rules without sharing Godot UI code.

## Commands

- `tools/Build.ps1`: build the standalone editor.
- `tools/Run.ps1`: build and run the editor application.
- `tools/Open-Editor.ps1`: open this project in the Godot editor.
- `tools/SmokeTest.ps1`: verify that the editor project starts independently.

## Current milestone

The editor scans the repository `songs` directory, isolates invalid packages,
lets the user choose a song and chart, loads OGG audio, and provides playback,
pause, seeking, and a vertical read-only 18-lane timeline. The left side of the
window is reserved for chart preview, while controls and future editing tools
live on the right. Time flows from bottom to top; the mouse wheel scrolls
through the chart, Ctrl plus the mouse wheel changes only the vertical time
scale, and a left click seeks. The vertical progress slider sits at the right
edge of the preview, while the frequently used play button remains outside the
workspace tabs.
Timeline colors distinguish
white Rel notes from red, green, and blue Drm notes. Holds are rendered as
translucent ribbons that follow lane and width changes, with diamond-shaped
judge points. Playback stays outside the chart workspace so it is always
available.

Editing and saving are intentionally not enabled yet. The next milestone adds
an in-memory editable chart document with undo/redo before any file can be
overwritten.
