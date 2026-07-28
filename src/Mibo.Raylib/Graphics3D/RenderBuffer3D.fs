namespace Mibo.Elmish.Graphics3D

open System
open System.Buffers
open System.Collections.Generic

/// <summary>
/// An allocation-friendly buffer for 3D render commands.
/// </summary>
/// <remarks>
/// Commands are accumulated each frame via <see cref="M:Mibo.Elmish.Graphics3D.RenderBuffer3D.Add"/>,
/// then executed in insertion order by the active pipeline.
/// The pipeline may re-sort internally if needed for state efficiency (e.g., front-to-back,
/// material batching), but the buffer itself does not impose an order.
///
/// Uses <see cref="T:System.Buffers.ArrayPool`1"/> for the backing store to avoid per-frame
/// heap allocations.
///
/// The buffer is designed to be cleared and repopulated each frame.
/// <see cref="M:Mibo.Elmish.Graphics3D.RenderBuffer3D.Clear"/> resets the count
/// without deallocating the internal array.
/// </remarks>
type RenderBuffer3D([<Struct>] ?capacity: int) =

  let mutable items =
    ArrayPool<Command3D>.Shared.Rent(defaultValueArg capacity 1024)

  let mutable count = 0
  let mutable clearCounter = 0
  let mutable postProcessCount = 0
  let mutable depthPostProcessCount = 0
  let mutable cameraBlockCount = 0

  let ensureCapacity(needed: int) =
    if count + needed > items.Length then
      let newSize = max (items.Length * 2) (count + needed)

      let newArr = ArrayPool<Command3D>.Shared.Rent(newSize)

      Array.Copy(items, newArr, count)
      ArrayPool<Command3D>.Shared.Return(items)
      items <- newArr

  /// <summary>The number of commands currently in the buffer.</summary>
  member _.Count = count

  /// <summary>
  /// Number of <c>PostProcess</c>/<c>PostProcessWithDepth</c> commands added since the last
  /// <c>Clear</c>. Lets a pipeline skip the post-process drain (and its per-frame allocation) when
  /// the view emits none.
  /// </summary>
  member _.PostProcessCount = postProcessCount

  /// <summary>
  /// Number of <c>PostProcessWithDepth</c> commands added since the last <c>Clear</c>. When &gt; 0,
  /// the pipeline exposes the scene depth attachment to post-process actions via
  /// <c>PostProcessContext3D.Depth</c>. Zero on frames with only color-only <c>PostProcess</c>
  /// actions.
  /// </summary>
  member _.DepthPostProcessCount = depthPostProcessCount

  /// <summary>
  /// Number of <c>BeginCamera</c>/<c>BeginCameraConfig</c> commands added since the last
  /// <c>Clear</c>. Lets a pipeline skip the per-camera-block plan (and its allocations) for
  /// single-camera frames.
  /// </summary>
  member _.CameraBlockCount = cameraBlockCount

  /// <summary>Gets the command at the specified index.</summary>
  member _.Item(i: int) = items[i]

  /// <summary>Adds a render command to the buffer.</summary>
  member _.Add(cmd: Command3D) =
    ensureCapacity 1
    items[count] <- cmd

    match cmd with
    | Command3D.PostProcess _ -> postProcessCount <- postProcessCount + 1
    | Command3D.PostProcessWithDepth _ ->
      postProcessCount <- postProcessCount + 1
      depthPostProcessCount <- depthPostProcessCount + 1
    | Command3D.BeginCamera _
    | Command3D.BeginCameraConfig _ -> cameraBlockCount <- cameraBlockCount + 1
    | _ -> ()

    count <- count + 1

  /// <summary>
  /// Clears all commands from the buffer without deallocating the backing array.
  /// Call this at the start of each frame before populating with new commands.
  /// </summary>
  member _.Clear() =
    count <- 0
    postProcessCount <- 0
    depthPostProcessCount <- 0
    cameraBlockCount <- 0
    clearCounter <- clearCounter + 1

    if clearCounter >= 300 then
      clearCounter <- 0
      Array.Clear(items, 0, items.Length)

  /// <summary>
  /// Sorts commands using the provided comparer.
  /// Pipelines may call this internally to optimize draw order.
  /// </summary>
  member _.Sort(comparer: IComparer<Command3D>) =
    Array.Sort(items, 0, count, comparer)

  interface System.IDisposable with
    member _.Dispose() =
      ArrayPool<Command3D>.Shared.Return(items, clearArray = true)
      items <- Array.empty
      count <- 0


// ─────────────────────────────────────────────────────────────────────────────
// Fluent Draw DSL witnesses (backing Mibo.Elmish.Graphics.Draw).
//
// One `member inline` per Core Draw member: construct the Command3D case
// directly (converting Mibo.Color where a command carries a color) — no
// dependency on the piped DSL or the Command3D builder module. 3D has no
// layer concept — camera/immediate witnesses accept and ignore the layer the
// shared Draw members pass. Augmentations must live in the buffer's own file:
// the SRTP solver only considers extension members in the type's declaration
// group. Everything is inline; the layer erases.
// ─────────────────────────────────────────────────────────────────────────────

open System.Numerics
open Raylib_cs
open Mibo.Animation
open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Elmish.Graphics3D.Pipelines
open Mibo

/// <summary>SRTP witnesses backing <see cref="T:Mibo.Elmish.Graphics.Draw"/> on the raylib 3D buffer.</summary>
type RenderBuffer3D with

  // ── Geometry ──

  member inline b.AddDrawMesh
    (mesh: Mesh, transform: Matrix4x4, material: Material3D)
    =
    b.Add(Command3D.DrawMesh(mesh, transform, material))

  member inline b.AddDrawInstanced
    (
      mesh: Mesh,
      transforms: Matrix4x4[],
      material: Material3D,
      instanceCount: int,
      colors: Raylib_cs.Color[] voption
    ) =
    match colors with
    | ValueSome _ ->
      raise(
        System.NotSupportedException(
          "Per-instance colors are only supported on the MonoGame backend"
        )
      )
    | ValueNone ->
      b.Add(
        Command3D.DrawMeshInstanced(mesh, transforms, material, instanceCount)
      )

  member inline b.AddDrawModel(model: Model, transform: Matrix4x4) =
    b.Add(Command3D.DrawModel(model, transform))

  member inline b.AddDrawModelWith
    (model: Model, transform: Matrix4x4, material: Material3D)
    =
    b.Add(
      Command3D.DrawModelWith(model, transform, MaterialOverride.All material)
    )

  member inline b.AddDrawModelWithPerMesh
    (
      model: Model,
      transform: Matrix4x4,
      [<InlineIfLambda>] resolver: int -> Material3D
    ) =
    b.Add(
      Command3D.DrawModelWith(
        model,
        transform,
        MaterialOverride.PerMesh resolver
      )
    )

  /// raylib animated draw: applies the state's bone pose to its embedded model
  /// (raylib's UpdateModelAnimation path), then draws the model.
  member inline b.AddAnimatedModel
    (state: Animation3DState, transform: Matrix4x4)
    =
    Animation3DState.applyToModel state
    b.Add(Command3D.DrawModel(state.Model, transform))

  member inline b.AddAnimatedModelWith
    (state: Animation3DState, transform: Matrix4x4, material: Material3D)
    =
    Animation3DState.applyToModel state

    b.Add(
      Command3D.DrawModelWith(
        state.Model,
        transform,
        MaterialOverride.All material
      )
    )

  member inline b.AddAnimatedModelWithPerMesh
    (
      state: Animation3DState,
      transform: Matrix4x4,
      [<InlineIfLambda>] resolver: int -> Material3D
    ) =
    Animation3DState.applyToModel state

    b.Add(
      Command3D.DrawModelWith(
        state.Model,
        transform,
        MaterialOverride.PerMesh resolver
      )
    )

  member inline b.AddSkinnedMesh
    (mesh: Mesh, transform: Matrix4x4, material: Material3D, bones: Matrix4x4[])
    =
    b.Add(Command3D.DrawSkinnedMesh(mesh, transform, material, bones))

  // ── Billboards & Lines ──

  member inline b.AddBillboard
    (
      texture: Texture2D,
      position: Vector3,
      size: Vector2,
      color: Color,
      rotation: float32,
      sourceRect: Rectangle,
      blend: BlendMode voption
    ) =
    b.Add(
      Command3D.DrawBillboard {
        Texture = texture
        Position = position
        Size = size
        Color = RaylibColor.toRaylibColor color
        Rotation = rotation
        SourceRect = sourceRect
        Blend = defaultValueArg blend BlendMode.Alpha
      }
    )

  member inline b.AddBillboardBatch
    (
      textures: Texture2D[],
      positions: Vector3[],
      sizes: Vector2[],
      colors: Raylib_cs.Color[],
      count: int,
      rotations: float32[],
      sourceRects: Rectangle[],
      blend: BlendMode voption
    ) =
    b.Add(
      Command3D.DrawBillboardBatch {
        Textures = textures
        Positions = positions
        Sizes = sizes
        Colors = colors
        Rotations = rotations
        SourceRects = sourceRects
        Blend = defaultValueArg blend BlendMode.Alpha
        Count = count
      }
    )

  member inline b.AddLine3D(start: Vector3, finish: Vector3, color: Color) =
    b.Add(Command3D.DrawLine3D(start, finish, RaylibColor.toRaylibColor color))

  // ── Camera (layer ignored — 3D has no layers) ──

  member inline b.AddBeginCamera(camera: Camera3D, _layer: int<RenderLayer>) =
    b.Add(Command3D.BeginCamera camera)

  member inline b.AddBeginCameraConfig
    (config: Camera3DConfig, _layer: int<RenderLayer>)
    =
    b.Add(Command3D.BeginCameraConfig config)

  member inline b.AddEndCamera(_layer: int<RenderLayer>) =
    b.Add Command3D.EndCamera

  // ── Shadows & Effect Scopes ──

  member inline b.AddSetShadowOrigin(origin: Vector3) =
    b.Add(Command3D.SetShadowOrigin origin)

  member inline b.AddEnableShadows3D() = b.Add Command3D.EnableShadows

  member inline b.AddDisableShadows3D() = b.Add Command3D.DisableShadows

  member inline b.AddBeginEffect(shader: Shader) =
    b.Add(Command3D.BeginEffect shader)

  member inline b.AddEndEffect() = b.Add Command3D.EndEffect

  // ── Lights (Core types, pass-through) ──

  member inline b.AddSetAmbientLight(light: AmbientLight3D) =
    b.Add(Command3D.SetAmbientLight light)

  member inline b.AddDirectionalLight(light: DirectionalLight3D) =
    b.Add(Command3D.AddDirectionalLight light)

  member inline b.AddPointLight(light: PointLight3D) =
    b.Add(Command3D.AddPointLight light)

  member inline b.AddSpotLight(light: SpotLight3D) =
    b.Add(Command3D.AddSpotLight light)

  // ── Escape Hatches ──

  member inline b.AddDrawImmediate
    ([<InlineIfLambda>] action: SceneContext -> unit, _layer: int<RenderLayer>)
    =
    b.Add(Command3D.DrawImmediate action)

  member inline b.AddPostProcess
    ([<InlineIfLambda>] action: PostProcessContext3D -> unit)
    =
    b.Add(Command3D.PostProcess action)

  member inline b.AddPostProcessWithDepth
    ([<InlineIfLambda>] action: PostProcessContext3D -> unit)
    =
    b.Add(Command3D.PostProcessWithDepth action)
