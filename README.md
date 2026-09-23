# AtEnd

Godot 4 C# rhythm game prototype. Confirmed gameplay rules live in
[`GAME_DESIGN.md`](GAME_DESIGN.md).

## Projects

- `AtEnd.csproj`: Godot application shell.
- `src/AtEnd.Core`: engine-independent timing/BPM, six-channel input matching,
  judgment, scoring, combo, accuracy, and completion-statistics library.
- `tests/AtEnd.Core.Tests`: dependency-free console test runner for the core.
- `src/App/Input`: Godot adapter that reads physical keycodes and forwards them
  to the engine-independent input state.

Run `tools/Test.ps1` to execute the core tests, `tools/Build.ps1` to build the
Godot C# project, `tools/SmokeTest.ps1` to verify the running scene and audio
clock, `tools/Run.ps1` to launch the game, or `tools/Open-Editor.ps1` to open the
project in the Godot editor. All scripts use the repository-local development
tools under `.tools`.
