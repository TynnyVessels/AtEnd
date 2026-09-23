# AtEnd

Godot 4 C# rhythm game prototype. Confirmed gameplay rules live in
[`GAME_DESIGN.md`](GAME_DESIGN.md).

## Projects

- `AtEnd.csproj`: Godot application shell.
- `src/AtEnd.Core`: engine-independent timing/BPM, judgment, scoring, combo,
  accuracy, and completion-statistics library.
- `tests/AtEnd.Core.Tests`: dependency-free console test runner for the core.

Run `tools/Test.ps1` to execute the core tests and `tools/Build.ps1` to build the
Godot C# project. Both scripts use the repository-local development tools under
`.tools`.
