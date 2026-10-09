# Tests (`tests/`)

Loaded by itself when work touches `tests/`. Run:
`dotnet test tests/ForestOverlay.Tests/ForestOverlay.Tests.csproj`.

Files under test are **linked into** the test project and compiled against a
tiny `UnityEngine` shim (`tests/.../UnityShim.cs`). BepInEx's stub is not used:
its method bodies are empty, so `Vector3.Distance` would return 0.

The shim implements `Vector3`, `Vector2`, `Mathf` only. **If it ever needs
`Quaternion` or `Transform`, that means the code under test is not pure and
should be refactored — not that the shim should grow.** Only pure files can be
linked; anything touching MonoBehaviour or reflection into the game cannot
(`Data/DumpText` reflects over plain objects by name - its tests pass
stand-in types). The one
filesystem exception is `patcher/PendingSwap.cs`, tested against real temp
folders because it is the code that can break an install.
