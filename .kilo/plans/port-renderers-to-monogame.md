# Port Renderers to MonoGame via a Core Command Abstraction

> **Scope:** Move the 2D/3D render-command model into `Mibo.Core` as
> backend-neutral `[<Struct>]` DUs over opaque **measure-tagged integer handles**
> (`int<Texture>`, etc.) plus an abstract buffer base. Each backend **subclasses
> the buffer so it carries its own resource registries** — no global state, no
> singletons, no partial application. All breakage stays **internal to the
> backend DSL** (`Draw.*`). Ship **raylib first** (migrate a sample, document
> breakages, get verified), then port to **MonoGame** (2D + minimal unlit 3D,
> precompiled DX/OGL shaders). New types land in a **temp `Next.*` namespace**
> alongside the untouched originals until cutover.

---

## 1. Design principles (locked)

1. **Perf-first.** Commands stay `[<Struct>]` DUs dispatched via pattern match
   (no interface boxing, no virtual calls in the hot path). The only per-draw
   cost the abstraction adds is **one dictionary lookup** when the DSL resolves a
   native handle → `int<Resource>` at `Add` time, and **one O(1) array index**
   when the renderer resolves it back.
2. **Breakage is internal to the DSL.** Users keep writing
   `Draw.sprite spriteState buffer`, `Draw.fillRect (0<RenderLayer>, Color(...)) rect buffer`,
   passing native `Texture2D`/`Shader`/`Font`/`Color`/`Rectangle` exactly as today.
   Public helper structs (`SpriteState`, `TextState`, light types, `Material3D`)
   **stay in the backend with native fields**, converted at the DSL boundary.
3. **Minimal exposed breakage.** The few real public changes are recorded in
   `CHANGELOG.md` + `docs/migration-to-vnext.md` (new "Phase 5") at cutover only.
4. **No type aliases.** Do **not** write `type ResourceHandle<'m> = int<'m>`.
   Use the measure-tagged `int<'m>` directly everywhere.
5. **No registry reassignment.** Registries are created **once** when the buffer
   is constructed (`member val X = Registry()`) and **never re-assigned**. Their
   internal dictionaries mutate (Add) — that is fine and expected. No `mutable`
   registry holders, no module-level `Current*`, no `Lazy` needed (construction
   is one-shot). The buffer is the carrier; the DSL reaches the registries
   through the buffer instance passed as its last argument.
6. **Order:** Raylib first → migrate sample + document breakages → **you verify**
   → then MonoGame. MonoGame does not start until raylib is verified.

---

## 2. Handle reliability (resolved)

Core commands reference GPU resources by `int<'m>` indices into a backend-owned
registry. The registry's forward map is keyed by an **identity token**, **never**
by the raylib struct directly (raylib structs use slow, unreliable
reflection-based equality).

| Resource     | raylib identity token (stable, fast)        | MonoGame identity token      |
|--------------|---------------------------------------------|------------------------------|
| Texture      | `Texture2D.Id : uint`                       | reference identity (class)   |
| Font         | `Font.Texture.Id : uint`                    | reference identity           |
| Shader       | `Shader.Id : uint`                          | reference identity (Effect)  |
| RenderTarget | `RenderTexture2D.Id : uint`                 | reference identity (RT2D)    |
| Mesh/Model   | *(3D phase)* `Mesh.VaoId` or reg. wrapper   | reference identity           |

Verified in-repo: `Material3D.fs:112` (`t.Id <> 0u`), `Renderer2D.fs:170`
(`cur.Id = s.Id`), `ShadowAtlas.fs:228` (`fbo.Id`). The 2D-first cut only
touches resources with clean ids.

**Caveat (documented, not solved):** raylib recycles `uint` ids after
`UnloadTexture`. If an asset is unloaded and a different texture later reuses the
id, the registry would resolve to the stale reverse entry. Mibo's `IAssets` cache
holds textures for the game lifetime, so ids stay stable in practice. Mitigation:
expose `Registry.Clear()` and document "clear registries if you dispose/reload
assets mid-session." This is inherent to lazy auto-register and acceptable for the
target use cases.

---

## 3. Architecture after the move

```
Mibo.Core (no backend dep)  — namespace Mibo.Elmish.Next.Graphics2D / .Graphics3D
  ├── Primitives.fs         Color, Rect, Viewport, BlendMode, measures
  │                         (Texture/Font/Shader/RenderTarget/Mesh/Model/LightContext)
  ├── Command2D.fs          [<Struct>] DU over int<Resource> + neutral types
  ├── Command3D.fs          [<Struct>] DU over int<Resource> + neutral types
  └── RenderBuffer2DBase    abstract; ArrayPool + layer-sort + getLayer + Add/
      RenderBuffer3DBase    Count/indexer/Clear/Sort/Dispose (over Core Command DU)
      (registry contract helper, no native types)

Mibo.Raylib  — namespace Mibo.Elmish.Next.Graphics2D / .Graphics3D
  ├── ResourceRegistry.fs   RaylibTextureRegistry/Font/Shader/RenderTarget/LightContext
  │                         (forward Dictionary<uint,_>, reverse ResizeArray<_>)
  ├── RenderBuffer2D.fs     inherits RenderBuffer2DBase; member val registries
  ├── RenderBuffer3D.fs     inherits RenderBuffer3DBase; member val registries
  ├── Conversions.fs        toColor/toRect/toRaylibColor/toRaylibRect (byte blits)
  ├── Draw.fs / Draw3D.fs   buffer.Add(Command2D.Sprite(buffer.Textures.Register …, …))
  ├── LightDraw.fs          lighting DSL over int<LightContext>
  ├── Renderer2D<'Model>    prototype IRenderer: dispatch Core cmd → raylib calls
  ├── Renderer3D<'Model>    minimal/unlit
  └── (UNTOUCHED) Graphics2D.* / Graphics3D.*  ← originals, until cutover

Mibo.MonoGame  (AFTER raylib is verified) — namespace Mibo.Elmish.Next.Graphics2D / .Graphics3D
  ├── ResourceRegistry.fs   reference-identity keyed
  ├── RenderBuffer2D.fs / RenderBuffer3D.fs   subclasses carrying registries
  ├── Conversions.fs / Draw.fs / Renderer2D<'Model> (SpriteBatch)
  ├── Renderer3D<'Model> (minimal/unlit)
  └── content/shaders/*.fx + compiled *.mgfx (DX9_1 / OGL3_0)
```

---

## 4. The buffer-carries-registries design (core mechanism)

### 4a. Core abstract base — `Next/Graphics2D/RenderBuffer2DBase.fs`
A `[<AbstractClass>]` (abstract only as a "do not construct directly" guard; no
abstract members needed since all logic is shared). Holds the ArrayPool-backed
command array, the `int64` stable-sort keys, the closed `getLayer` match over the
Core `Command2D`, and `Add`/`Count`/indexer/`Clear`/`Sort`/`Dispose` — ported
verbatim from `src/Mibo.Raylib/Graphics2D/RenderBuffer.fs`.

```fsharp
[<AbstractClass>]
type RenderBuffer2DBase(?capacity: int) =
  let mutable items = ArrayPool<Command2D>.Shared.Rent(defaultArg capacity 1024)
  let mutable keys  = ArrayPool<int64>.Shared.Rent(defaultArg capacity 1024)
  let mutable count = 0
  // getLayer : Command2D -> int<RenderLayer>   (closed match, same as today)
  // ensureCapacity, Add(cmd), Count, Item, Clear, Sort, Dispose  — all here
```

### 4b. Backend subclass carries the registries — raylib `RenderBuffer2D.fs`
```fsharp
type RenderBuffer2D(?capacity: int) =
  inherit RenderBuffer2DBase(?capacity = capacity)
  member val Textures      = RaylibTextureRegistry()      // created once
  member val Fonts         = RaylibFontRegistry()         // never re-assigned
  member val Shaders       = RaylibShaderRegistry()
  member val RenderTargets = RaylibRenderTargetRegistry()
  member val LightContexts = LightContextRegistry()
```
Each `member val` is assigned once at construction. The buffer is owned by the
renderer and only `.Clear()`-ed per frame, so registry entries persist across
frames (same texture → same index every frame).

### 4c. The DSL reaches registries through the buffer (no global state)
```fsharp
module Draw =
  // No resource fields ⇒ no registry needed.
  let inline fillRect (layer, color: Raylib_cs.Color) (rect: Raylib_cs.Rectangle)
      (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.FillRect(toRect rect, toColor color, layer))
    buffer

  // Resource field ⇒ resolve via the buffer's own registry.
  let inline sprite (s: SpriteState) (buffer: RenderBuffer2D) =
    buffer.Add(Command2D.Sprite(
      buffer.Textures.Register s.Texture,
      toRect s.Dest, toRect s.Source, s.Origin, s.Rotation,
      toColor s.Color, s.Layer))
    buffer
```
The old `Command2D.*` factory module is **gone** — `Draw.*` builds DU cases
inline. `LightCommands`/`LightDraw` fold in the same way. The `buffer` argument
already threaded through every `Draw.*` call is the single source of registries.

### 4d. Renderer resolves handles back at dispatch
The prototype `Renderer2D<'Model>` owns `let buffer = RenderBuffer2D(...)`. Its
dispatch loop matches the Core `Command2D`, resolves via the same buffer, and
issues the identical raylib calls as today:
```fsharp
| Command2D.Sprite(h, dest, source, origin, rot, color, _) ->
  Raylib.DrawTexturePro(buffer.Textures.Resolve h, toRaylibRect source,
                        toRaylibRect dest, origin, rot, toRaylibColor color)
```

---

## 5. Phase A — Core neutral primitives + command DUs + abstract buffer base

Files added to `Mibo.Core.fsproj` (before `Layout/`). Namespace
`Mibo.Elmish.Next.Graphics2D` / `.Next.Graphics3D` (temp, avoids clash with the
still-present raylib `Mibo.Elmish.Graphics2D.*`).

### A1. `Next/Graphics2D/Primitives.fs`
- Measure decls: `[<Measure>] type Texture / Font / Shader / RenderTarget / Mesh / Model / LightContext`
  (render-layer measure `RenderLayer` already exists — keep it).
- `Color` (byte struct, isomorphic with raylib/MG Color) + `module Color` helpers.
- `Rect` (float32 struct), `Viewport` (int struct).
- Neutral `BlendMode` enum.
- Use `System.Numerics.Vector2/3/4` + `Matrix4x4` (existing convention).

### A2. `Next/Graphics2D/Command2D.fs`
Mirror the shape of the existing `Command2D` but:
- `Texture2D`→`int<Texture>`, `Font`→`int<Font>`, `Shader`→`int<Shader>`,
  `RenderTexture2D`→`int<RenderTarget>`.
- `Raylib_cs.Color`→`Color`, `Raylib_cs.Rectangle`→`Rect`, `Camera2D`/`Camera2DConfig`/
  `BlendMode`→neutral equivalents.
- Lighting commands carry `int<LightContext>` (opaque token; see §6).
- Keep `[<RequireQualifiedAccess; Struct>]` and `layer: int<RenderLayer>` on every
  case (so the base's single-pass `getLayer` keeps working).
- **No factory module in Core** — the backend DSL constructs cases directly.

### A3. `Next/Graphics2D/RenderBuffer2DBase.fs` (and the 3D analog)
Port `RenderBuffer2D`/`RenderBuffer3D` as abstract bases (§4a). Both `IDisposable`.

### A4. Deliverable check
- `dotnet build src/Mibo.Core` succeeds; no raylib/MonoGame refs.
- Unit test: construct Core `Command2D.Sprite(...)` with `int<Texture>`, add to a
  test subclass of the base, assert `Count`, layer sort, stable ordering.

---

## 6. Phase B — Raylib backend: registries + buffer subclass + DSL

**Original raylib `Renderer2D`/`Renderer3D` are untouched** this phase.

### B1. Registries — `Mibo.Raylib/Next/ResourceRegistry.fs`
```fsharp
type RaylibTextureRegistry() =
  let fwd = Dictionary<uint, int<Texture>>()
  let rev = ResizeArray<Raylib_cs.Texture2D>()
  member _.Register(t: Texture2D) =
    match fwd.TryGetValue t.Id with
    | true, h -> h
    | _ -> let h = rev.Count * 1<Texture> in rev.Add t; fwd[t.Id] <- h; h
  member _.Resolve(h: int<Texture>) = rev[int h]
  member _.Clear() = fwd.Clear(); rev.Clear()
```
Repeat for Font (token `Font.Texture.Id`), Shader (`Shader.Id`), RenderTarget
(`RenderTexture2D.Id`). `LightContextRegistry` keys by the `LightContext2D` object
reference (it's a class) → `int<LightContext>`; resolves back to the live context.

### B2. Buffer subclass + conversions + DSL
- `Next/Conversions.fs`: `toColor`/`toRect` and inverses (byte/value blits; raylib
  and Core Color share byte layout).
- `Next/Graphics2D/RenderBuffer2D.fs`: subclass per §4b.
- `Next/Graphics2D/Draw.fs`: per §4c, one inline fn per Command2D case.
- `Next/Graphics2D/LightDraw.fs`: lighting DSL over `buffer.LightContexts`.
- `Next/Graphics3D/Draw3D.fs`: same pattern for 3D.

### B3. Deliverable check
- Raylib project builds with **both** old `Graphics2D` and new `Next.Graphics2D`.
- Unit test (fake registry via the injectable `DrawInternal`-style test seam — see
  §10): feed native `SpriteState`/`Color`/`Rect` through `Draw.sprite`, assert the
  emitted Core command's payload round-trips.

---

## 7. Phase C — Raylib prototype renderers + sample migration

### C1. `Next/Graphics2D/Renderer2D<'Model>`
Port the orchestration shell of `Renderer2D`
(`src/Mibo.Raylib/Graphics2D/Renderer2D.fs`) but dispatch the **Core** `Command2D`,
resolving handles from `buffer.Textures` etc. Reuse `PostProcess2D` +
`IRenderTargetPool` (raylib-side). Public API mirrors `Renderer2D.create`/
`createWith` so a sample opts in:
`Program.withRenderer (fun () -> Next.Graphics2D.Renderer2D.create view)`.

### C2. Lighting token
`LightContext2D` owns raylib shaders/uniforms — it cannot move to Core. Core
lighting commands carry `int<LightContext>`; `buffer.LightContexts` resolves it.
`LightDraw.*` registers the context (idempotent, keyed by ref) when the user
passes it. `LightContext2D` stays fully backend-owned.

### C3. Migrate `PlatformerSample` → `Next.Graphics2D`
- Switch `open` + `Draw`/`LightDraw`/`Renderer2D` to the `Next.Graphics2D` path.
- The DSL signatures are unchanged (native args), so the diff is mostly `open`
  lines + `Renderer2D.create` resolution. This is the "surface rough edges" gate.

### C4. Document breakages as they appear
Add entries under `CHANGELOG.md` `[Unreleased]` and a draft "Phase 5" section in
`docs/migration-to-vnext.md`. **Expected breakages** (internal, recorded):
- `Command2D.*` factory module removed (use `Draw.*`).
- Anyone reaching into `Command2D` payload types sees `int<Resource>` instead of
  native handles (rare — most users go through the DSL).
- `Renderer2D`/`Renderer3D` public constructors (`create`/`createWith`) unchanged.

### C5. **GATE: you verify** raylib renders correctly. MonoGame does not start
until you sign off.

---

## 8. Phase D — MonoGame backend (after verification)

### D1. Registries (reference-identity keyed)
MonoGame resources are classes → the forward `Dictionary<Texture2D, int<Texture>>`
uses `ReferenceEquals`. Same shape as §B1.

### D2. Buffer subclass + DSL + renderer
- `Mibo.MonoGame/Next/Graphics2D/RenderBuffer2D.fs` (subclass, registries).
- `Draw.fs` converting from `Microsoft.Xna.Framework.*`.
- `Renderer2D<'Model>` dispatches Core `Command2D` to `SpriteBatch`:
  - `Sprite`→`Draw(tex, dest, src, color, …)`; `Text`→`DrawString`.
  - `FillRect`/`Circle`/`Poly`/`Line` etc. → a tiny tinted-1px-texture fill +
    `PrimitiveBatch` (SpriteBatch has no filled circles/polygons).
  - `BeginCamera` → view matrix from `Camera2DState` → `SpriteBatch.Begin(transformMatrix=…)`.
  - `SetBlend` → map neutral `BlendMode`→`BlendState` (flush `End()`/`Begin()`).
  - Lighting/shadows: render unlit initially (follow-up).

### D3. Shaders (precompiled) — `Mibo.MonoGame/content/shaders/`
MonoGame cannot compile GLSL at runtime. Author MonoGame FX (HLSL), compile via
**2MGFX**:

| Profile  | Vertex target        | Pixel target         |
|----------|----------------------|----------------------|
| DirectX  | `vs_4_0_level_9_1`   | `ps_4_0_level_9_1`   |
| OpenGL   | `vs_3_0`             | `ps_3_0`             |

```
2mgfx Tint.fx -o compiled/Tint.dx.mgfx  /Platform:Windows
2mgfx Tint.fx -o compiled/Tint.ogl.mgfx /Platform:DesktopGL
```
Check in **both** `.fx` source and compiled `.mgfx` (no build-time compiler dep).
Pick the right file at runtime by backend; load via `Content.Load<Effect>`.
Initial set: `tint` + a SpriteBatch-replacement effect only. Full PBR is follow-up.

### D4. Minimal 3D (MonoGame)
Unlit/basic only: `DrawMesh`/`DrawModel`/`DrawLine3D` → `BasicEffect`/
`ModelMesh.Draw`. No PBR/shadows/skinning/instancing this effort (§10 follow-up).

### D5. Sample
Add `samples/MonoGame2DSample` to validate DSL + renderer end-to-end.

---

## 9. Phase E — Cutover (recorded, minimal breakage)

1. Delete old `src/Mibo.Raylib/Graphics2D/*` + `Graphics3D/*` originals.
2. Global rename `Mibo.Elmish.Next.Graphics2D` → `Mibo.Elmish.Graphics2D`
   (and `.Graphics3D`). `dotnet fantomas .`.
3. Update `Layout3D/Renderer3D.fs` (raylib instanced bridge) to the new
   `Command3D`/`RenderBuffer3D` (it consumes `Mibo.Elmish.Graphics3D`).
4. Finalize `CHANGELOG.md` + `docs/migration-to-vnext.md` Phase 5.
5. All migrated samples use the canonical namespace.

---

## 10. Testing strategy

- **Core unit tests** (`Mibo.Core.Tests`): construct Core `Command2D`/`Command3D`
  with `int<Resource>`; add via a test subclass of the abstract base; assert
  counts, layer sort, stable order. No backend refs.
- **Registry tests** (per backend): `Register` idempotency, `Resolve` round-trip,
  same native → same index, distinct → distinct. Expose a test seam by making
  `Draw` functions callable with an explicit buffer instance (they already take
  the buffer as last arg) — supply a buffer whose registries are pre-seeded.
- **DSL conversion tests**: native `SpriteState`/`Color`/`Rect` →
  `Draw.*` → assert the emitted Core command's resolved payload matches the input.
- **Smoke/visual**: migrated `PlatformerSample` (raylib) + new `MonoGame2DSample`
  render without exceptions and match expected output for a seeded frame.

---

## 11. File-level checklist (implementation order — raylib first)

**Phase A (Core):** add to `Mibo.Core.fsproj` before `Layout/`:
- `Next/Graphics2D/Primitives.fs`
- `Next/Graphics2D/Command2D.fs`
- `Next/Graphics2D/RenderBuffer2DBase.fs`
- `Next/Graphics3D/Command3D.fs`
- `Next/Graphics3D/RenderBuffer3DBase.fs`

**Phase B (raylib DSL):** add to `Mibo.Raylib.fsproj` after existing Graphics:
- `Next/Conversions.fs`
- `Next/ResourceRegistry.fs` (Texture/Font/Shader/RenderTarget/LightContext)
- `Next/Graphics2D/RenderBuffer2D.fs` (subclass)
- `Next/Graphics3D/RenderBuffer3D.fs` (subclass)
- `Next/Graphics2D/Draw.fs`, `Next/Graphics2D/LightDraw.fs`
- `Next/Graphics3D/Draw3D.fs`

**Phase C (raylib prototype + sample):**
- `Next/Graphics2D/Renderer2D.fs` (prototype `IRenderer`)
- `Next/Graphics3D/Renderer3D.fs` (minimal)
- Migrate `samples/PlatformerSample/*` → `Next.Graphics2D`
- Draft `CHANGELOG.md` + `docs/migration-to-vnext.md` Phase 5

**⏸ VERIFY (you) — MonoGame does not begin until sign-off.**

**Phase D (MonoGame):** add to `Mibo.MonoGame.fsproj`:
- `Next/Conversions.fs`, `Next/ResourceRegistry.fs`
- `Next/Graphics2D/RenderBuffer2D.fs`, `Draw.fs`, `Renderer2D.fs`
- `Next/Graphics3D/Renderer3D.fs` (minimal)
- `content/shaders/*.fx` + compiled `*.mgfx`
- `samples/MonoGame2DSample/*`

**Phase E (cutover):** delete originals, global namespace rename, update
`Layout3D/Renderer3D.fs`, finalize docs, `dotnet fantomas .`.

---

## 12. Gotchas & raylib quirks to carry forward

- **Never key a `Dictionary` by raylib value-type structs** (reflection equality).
  Always extract the `uint` id token (§2).
- **No type aliases** — use `int<Texture>` etc. directly.
- **No registry reassignment** — `member val` once at construction; internal
  dictionaries may mutate. No `mutable` registry holders, no module globals, no `Lazy`.
- **`[<DisableRuntimeMarshalling>]`** + `void*` FFI (AGENTS.md): prototype 3D
  shader uploads keep `fixed &value; NativePtr.toVoidPtr p`; `SetShaderValueMatrix`
  is the exception.
- **Matrix conventions** (AGENTS.md): VP capture inside `BeginMode3D`; don't mix
  `Vector4.Transform` with GLSL `mat*vec`.
- **`RenderBuffer2DBase.getLayer`** is a closed match over `Command2D` — every new
  Core case must expose `layer` for single-pass sort.
- **id-recycling caveat** (§2): clear registries if assets are disposed/reloaded mid-session.
- **F# style**: `dotnet fantomas .` before commits; no `Option.get`/`ValueOption.get`;
  prefer structs/arrays/spans; no comments unless requested.
- **Public API + XML docs** on every Core type (AGENTS.md).
