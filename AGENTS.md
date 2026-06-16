# Mibo.Raylib

Mibo.Raylib is a port of the Mibo micro-framework from MonoGame to raylib-cs, designed to allow F# developers to write games using familiar patterns for all kinds of game genres and sizes.

Mibo aims to solve 90% of use cases for enabling developers to focus on game logic rather than boilerplate code, providing guidelines and architecture for structuring game game code, handling input, rendering, asset management, and time management among others.

General setup and usage instructions can be found in the [README.md](README.md) file.

## Imperatives

1. **NEVER PUSH WITHOUT PERMISSION.** Always ask before pushing to the remote.
2. **NEVER FORCE PUSH.** Tell the user they have to force push instead of you.
3. **Always run `dotnet fantomas .` before committing code.** Format all F# files before staging.
4. **Never use `Option.get` or `ValueOption.get`.** Always pattern match (`match`, `function`, `if ... then`) or use safe alternatives (`Option.defaultValue`, `Option.map`, `Array.choose`, etc.) to handle option values. Unchecked `.get` calls crash at runtime on `None`.
5. Pull requests made with the `gh` command should use a markdown file as the PR body, not inline escaped markdown strings.

## Project Structure

All of the projects live in the `src` folder:

- `Mibo.Core`: Backend-agnostic core (Cmd/Sub/System/GameTime/RenderBuffer/Program/IRenderer/GameContext). No raylib dependency.
- `Mibo.Raylib`: Main library project (raylib backend; depends on `Mibo.Core`)
- `Mibo.MonoGame`: MonoGame backend (depends on `Mibo.Core`)

Samples live in a separate repository: [Mibo.Samples](https://github.com/AngelMunoz/Mibo.Samples)

The documentation site is built using [FsDocs](https://fsprojects.github.io/FSharp.Formatting/) and lives in the `docs` folder.

## Project Considerations

The core abstractions of the library MUST NOT incur performance penalties for users. Abstractions should aim to be zero-cost or close to zero-cost.

- Prefer structs over classes unless struct size is too large.
- Prefer object expressions to classes.
- Avoid heap allocations in hot paths.
- If implementing an interface, use a struct instead of a class or object expression if that object is going to be in a hot path
- Favor arrays and spans over lists.
- Favor ArrayPool over allocating new arrays where possible.
- Favor functional programming patterns but allow mutable state when necessary for performance.
- Public API should be ergonomic and easy to use.
- Public API should be well documented with XML comments.
- Public API should follow elmish-friendly patterns where applicable.

## Changelog Management

We follow https://github.com/ionide/KeepAChangelog guidelines

Changelog Format:

```markdown
# Changelog

## [Unreleased]

Content that is pending for release goes here.

## [1.0.0] - 2026.01.13

### Added

- Initial release
```

Each section may contain the following categories:

- Added
- Changed
- Deprecated
- Removed
- Fixed
- Security

When adding entries to the changelog, make sure to follow format and categories.

## raylib-cs / F# Quirks

### DisableRuntimeMarshalling + void\* Bug

The project uses `[<DisableRuntimeMarshalling>]`. This affects how `SetShaderValue` and similar FFI calls work:

- **DO NOT** pass raw `int`, `float32`, `Vector3` etc. directly as `void*` arguments. The runtime treats the value itself as a pointer address (e.g., passing `1` reads from address `0x1`, causing access violations).
- **ALWAYS** use `fixed + NativePtr.toVoidPtr` for scalar/vec3/vec4 uniforms:

```fsharp
let setShaderInt (shader: Shader) (loc: int) (value: int) =
    use p = fixed &value
    Raylib.SetShaderValue(shader, loc, NativePtr.toVoidPtr p, ShaderUniformDataType.Int)
```

- **EXCEPTION**: `SetShaderValueMatrix` takes `Matrix4x4` directly (not `void*`) — this works correctly.
- **EXCEPTION**: `Rlgl.SetUniform` (raw rlgl) also requires `fixed + NativePtr.toVoidPtr`.

### Matrix Conventions

#### System.Numerics vs raylib struct layout

`Matrix4x4` in System.Numerics stores rows contiguously in memory: `m00 m01 m02 m03 m10 m11 m12 m13 ...`

raylib's `rlMatrix` stores columns contiguously: `m0 m4 m8 m12 m1 m5 m9 m13 ...`

`rlMatrixToFloatV` reorders from raylib's struct layout to GLSL column-major order (fields m0→m1→m2→...→m15). This means:

- `SetShaderValueMatrix` and the batch's `glUniformMatrix4fv` both go through `rlMatrixToFloatV` — **they match**.
- Do NOT manually blit a `Matrix4x4` to float arrays — use `rlMatrixToFloatV` for correct conversion.

#### Vector4.Transform vs GLSL mat\*vec

- `Vector4.Transform(v, M)` computes `v * M^T` (transposed convention)
- GLSL `M * v` computes `M * v` (standard convention)
- These give **different results** on the same matrix. Do not mix them.
- For uniform passing, `SetShaderValueMatrix` handles conversion via `rlMatrixToFloatV` — this is correct regardless.

#### VP Matrix Capture

When capturing the View-Projection matrix for shadow mapping:

- **MUST** capture inside `BeginMode3D` using `Rlgl.GetMatrixModelview() * Rlgl.GetMatrixProjection()`
- This matches what the batch computes for `mvp` (for identity model transforms)
- Precomputing VP outside `BeginMode3D` or using `Matrix4x4.CreateLookAt * CreatePerspectiveFieldOfView` may produce different results due to raylib's internal matrix adjustments
