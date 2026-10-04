module Mibo.MonoGame.Tests.Layout3D

#nowarn "44"

open Expecto
open System
open Microsoft.Xna.Framework
open Mibo.Layout
open Mibo.Layout3D
open Mibo.Elmish.Graphics3D
open Mibo.Elmish.Graphics

// Device-free helpers for instanced-renderer command-sequence tests.
// We assert command KIND and order only — never shader/mesh identity — so
// no GL/D3D context is needed and these run headless in CI. PrimitiveMesh and
// Effect are default-constructed (null buffers); they are never dereferenced.
let private dummyMesh() : PrimitiveMesh = Unchecked.defaultof<_>
let private dummyMat() : Material3D = Unchecked.defaultof<_>

let private dummyEffect() : Microsoft.Xna.Framework.Graphics.Effect =
  Unchecked.defaultof<_>

let private cmdTag(cmd: Command3D) : string =
  match cmd with
  | Command3D.BeginEffect _ -> "begin"
  | Command3D.EndEffect -> "end"
  | Command3D.DrawInstanced _ -> "draw"
  | _ -> "other"

let private cmdSequence(buf: RenderBuffer3D) = [|
  for i in 0 .. buf.Count - 1 -> cmdTag(buf.Item i)
|]

// Context builders — named args disambiguate the two InstancedRenderContext ctors.
let private pairsFor(meshCount: int) : struct (PrimitiveMesh * Material3D)[] = [|
  for _ in 1..meshCount -> struct (dummyMesh(), dummyMat())
|]

let private ctxFromPairs meshCount =
  let getMeshesAndMaterial: int -> struct (PrimitiveMesh * Material3D)[] =
    fun _ -> pairsFor meshCount

  InstancedRenderContext<int, int>(
    getKey = id,
    getMeshesAndMaterial = getMeshesAndMaterial,
    getTransform = fun _ _ -> Matrix.Identity
  )

let private ctxFromTriples triples =
  let getMeshesMaterialAndShader
    : int
        -> struct (PrimitiveMesh *
        Material3D *
        Microsoft.Xna.Framework.Graphics.Effect voption)[] =
    fun _ -> triples

  InstancedRenderContext<int, int>(
    getKey = id,
    getMeshesMaterialAndShader = getMeshesMaterialAndShader,
    getTransform = fun _ _ -> Matrix.Identity
  )

let private singleCellGrid value =
  CellGrid3D.create
    1
    1
    1
    (System.Numerics.Vector3(1f, 1f, 1f))
    System.Numerics.Vector3.Zero
  |> Layout3D.run(fun s -> s |> Layout3D.set 0 0 0 value)

let private partFor
  (vertexOffset: int, startIndex: int, bone: Matrix)
  : ModelPart =
  {
    Mesh = dummyMesh()
    VertexOffset = vertexOffset
    StartIndex = startIndex
    Bone = bone
    Material = dummyMat()
  }

let private ctxFromParts(parts: ModelPart[]) =
  InstancedRenderContext<int, int>(
    getKey = id,
    getParts = (fun _ -> parts),
    getTransform = fun _ _ -> Matrix.CreateTranslation(1f, 0f, 0f)
  )

/// A grid whose cells state their own span: 0 is the identity, any other number
/// spans that many cells across two rows.
let private spanProjection(value: int) : InstanceSpan =
  if value = 0 then One else Span(value, 2)

let private spanGrid() =
  let grid =
    CellGrid2D.create
      8
      2
      (System.Numerics.Vector2(16f, 16f))
      System.Numerics.Vector2.Zero

  CellGrid2D.set 0 0 3 grid
  CellGrid2D.set 7 1 0 grid
  grid

let private scanSpans(grid: CellGrid2D<int>) : Occupancy =
  match Occupancy.scan spanProjection grid with
  | Ok occupancy -> occupancy
  | Error reason -> failtest reason

[<Tests>]
let tests =
  testList "Layout3D InstancedRenderer3D (MonoGame)" [
    testCase "renderInstanced emits one draw per sub-mesh, no effect scope"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 2
      let grid = singleCellGrid 0

      CellGridRenderer3D.renderInstanced ctx grid buf

      Expect.equal buf.Count 2 "two sub-meshes → two draws"
      Expect.equal (cmdSequence buf) [| "draw"; "draw" |] "no effect scope"

    testCase "legacy ctor + renderInstanced unchanged (regression)"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 1
      let grid = singleCellGrid 7

      CellGridRenderer3D.renderInstanced ctx grid buf

      Expect.equal buf.Count 1 "single draw"
      Expect.equal (cmdSequence buf) [| "draw" |] "no scope"

    testCase "per-sub-mesh ctor wraps ValueSome in its own scope"
    <| fun _ ->
      use buf = new RenderBuffer3D()

      let triples = [|
        struct (dummyMesh(), dummyMat(), ValueSome(dummyEffect()))
        struct (dummyMesh(), dummyMat(), ValueNone)
      |]

      let ctx = ctxFromTriples triples
      let grid = singleCellGrid 0

      CellGridRenderer3D.renderInstanced ctx grid buf

      Expect.equal buf.Count 4 "begin+draw+end, then draw"

      Expect.equal
        (cmdSequence buf)
        [| "begin"; "draw"; "end"; "draw" |]
        "per-sub-mesh scope"

    testCase "renderInstancedWithEffect wraps a ValueSome key"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 1
      let grid = singleCellGrid 0

      CellGridRenderer3D.renderInstancedWithEffect
        ctx
        grid
        (fun _ -> ValueSome(dummyEffect()))
        buf

      Expect.equal buf.Count 3 "begin + draw + end"
      Expect.equal (cmdSequence buf) [| "begin"; "draw"; "end" |] "key scope"

    testCase "renderInstancedWithEffect ValueNone key falls through to PBR"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 1
      let grid = singleCellGrid 0

      CellGridRenderer3D.renderInstancedWithEffect
        ctx
        grid
        (fun _ -> ValueNone)
        buf

      Expect.equal buf.Count 1 "draw only"
      Expect.equal (cmdSequence buf) [| "draw" |] "no scope"

    testCase "parts ctor emits one draw per part with offsets and bone fold"
    <| fun _ ->
      // Zero-copy ModelPart wraps: each part must draw with its own buffer
      // offsets, and a non-identity absolute bone must be folded in front of
      // the instance transform (a per-part snapshot, not the shared one).
      use buf = new RenderBuffer3D()

      let bone = Matrix.CreateTranslation(2f, 0f, 0f)

      let parts = [| partFor(0, 0, Matrix.Identity); partFor(7, 12, bone) |]
      let ctx = ctxFromParts parts
      let grid = singleCellGrid 0

      CellGridRenderer3D.renderInstanced ctx grid buf

      Expect.equal buf.Count 2 "two parts → two draws"

      match buf.Item 0 with
      | Command3D.DrawInstanced(_, t, _, _, _, vo, si) ->
        Expect.equal (vo, si) (0, 0) "identity-bone part keeps zero offsets"

        Expect.equal
          t[0]
          (Matrix.CreateTranslation(1f, 0f, 0f))
          "identity bone reuses the group snapshot unchanged"
      | other -> failtest $"expected DrawInstanced, got %A{other}"

      match buf.Item 1 with
      | Command3D.DrawInstanced(_, t, _, _, _, vo, si) ->
        Expect.equal (vo, si) (7, 12) "part offsets reach the command"

        Expect.equal
          t[0]
          (bone * Matrix.CreateTranslation(1f, 0f, 0f))
          "bone folded in front of the instance transform"
      | other -> failtest $"expected DrawInstanced, got %A{other}"

    testCase
      "parts ctor + renderInstancedWithEffect wraps every part in the key scope"
    <| fun _ ->
      // The parts branch of EmitInstancedWithEffect: one BeginEffect/EndEffect
      // scope per ValueSome key, with every part's draw (and its offsets)
      // inside it. A ValueNone key would draw bare.
      use buf = new RenderBuffer3D()

      let parts = [|
        partFor(0, 0, Matrix.Identity)
        partFor(7, 12, Matrix.Identity)
      |]

      let ctx = ctxFromParts parts
      let grid = singleCellGrid 0

      CellGridRenderer3D.renderInstancedWithEffect
        ctx
        grid
        (fun _ -> ValueSome(dummyEffect()))
        buf

      Expect.equal buf.Count 4 "begin + draw + draw + end"

      Expect.equal
        (cmdSequence buf)
        [| "begin"; "draw"; "draw"; "end" |]
        "one key scope over every part"

      match buf.Item 1, buf.Item 2 with
      | Command3D.DrawInstanced(_, _, _, _, _, vo1, _),
        Command3D.DrawInstanced(_, _, _, _, _, vo2, si2) ->
        Expect.equal vo1 0 "first part keeps its offsets inside the scope"

        Expect.equal
          (vo2, si2)
          (7, 12)
          "second part keeps its offsets inside the scope"
      | other -> failtest $"expected two DrawInstanced, got %A{other}"

    testCase "ResetFrameBuffers returns group and folded snapshots to the pool"
    <| fun _ ->
      // EmitPartInstanced rents a folded copy for non-identity bones and
      // tracks it in the snapshot pool; ResetFrameBuffers must return every
      // tracked array (group snapshot + folds) to ArrayPool.
      use buf = new RenderBuffer3D()

      let bone = Matrix.CreateTranslation(2f, 0f, 0f)
      let parts = [| partFor(0, 0, bone) |]
      let ctx = ctxFromParts parts
      let grid = singleCellGrid 0

      CellGridRenderer3D.renderInstanced ctx grid buf

      Expect.equal
        ctx.SnapshotPool.Count
        2
        "one group snapshot + one folded per-part snapshot tracked"

      ctx.ResetFrameBuffers()

      Expect.equal
        ctx.SnapshotPool.Count
        0
        "all snapshots returned to ArrayPool"

    testCase "mixed keys interleave scopes correctly"
    <| fun _ ->
      // key 0 → no shader, key 1 → shader. Exercises the rocks/water/ground
      // interleaving: no/scope/no would not leak state.
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 1

      // Two cells, two distinct keys → two groups (order not guaranteed by
      // Dictionary, so we assert the multiset of spans, not the sequence).
      let grid =
        CellGrid3D.create
          2
          1
          1
          (System.Numerics.Vector3(1f, 1f, 1f))
          System.Numerics.Vector3.Zero
        |> Layout3D.run(fun s ->
          s |> Layout3D.set 0 0 0 0 |> Layout3D.set 1 0 0 1)

      let shaderForKey k =
        match k with
        | 1 -> ValueSome(dummyEffect())
        | _ -> ValueNone

      CellGridRenderer3D.renderInstancedWithEffect ctx grid shaderForKey buf

      Expect.equal buf.Count 4 "2 groups: draw, and begin+draw+end"
      let kinds = cmdSequence buf |> Array.countBy id |> Map.ofArray

      Expect.equal kinds.["begin"] 1 "one begin"
      Expect.equal kinds.["end"] 1 "one end"
      Expect.equal kinds.["draw"] 2 "two draws"

    testCase "fluent Draw.renderCellGridInstanced resolves the witness"
    <| fun _ ->
      // SRTP resolution of a witness defined on InstancedRenderContext in
      // Layout3D/Renderer3D.fs. If this compiles AND runs, it resolves.
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 1
      let grid = singleCellGrid 0

      buf.renderCellGridInstanced(ctx, grid).drop()

      Expect.equal buf.Count 1 "fluent overload emitted one draw"
      Expect.equal (cmdSequence buf) [| "draw" |] "no scope"

    testCase "fluent Draw.renderCellGridInstanced per-key overload"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 1
      let grid = singleCellGrid 0

      buf
        .renderCellGridInstanced(ctx, grid, fun _ -> ValueSome(dummyEffect()))
        .drop()

      Expect.equal buf.Count 3 "begin + draw + end"

      Expect.equal
        (cmdSequence buf)
        [| "begin"; "draw"; "end" |]
        "key scope via fluent"

    testCase "CameraBlockCount tracks camera-block commands and resets on Clear"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      Expect.equal buf.CameraBlockCount 0 "Fresh buffer has no camera blocks"

      buf.Add(Command3D.BeginCamera(Unchecked.defaultof<Mibo.Elmish.Camera3D>))
      buf.Add Command3D.EndCamera
      buf.Add(Command3D.BeginCamera(Unchecked.defaultof<Mibo.Elmish.Camera3D>))

      Expect.equal buf.CameraBlockCount 2 "Counts only Begin camera commands"

      buf.Clear()
      Expect.equal buf.CameraBlockCount 0 "Clear should reset CameraBlockCount"

    testCase "an occupancy sizes an instance over its rectangle"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let seen = ResizeArray<CellRect>()

      let ctx =
        InstancedRenderContext<int, int>
          .Rect(
            getKey = id,
            getMeshesAndMaterial = (fun _ -> pairsFor 1),
            getTransform =
              (fun rect _ _ ->
                seen.Add rect
                Matrix.Identity)
          )

      let grid = spanGrid()
      let occupancy = scanSpans grid

      ctx.RenderInstanced(buf, grid, occupancy)

      Expect.equal buf.Count 2 "one draw per key: the span and the plain cell"

      Expect.equal
        (List.ofSeq seen)
        [ { X = 0; Y = 0; W = 3; H = 2 }; { X = 7; Y = 1; W = 1; H = 1 } ]
        "each anchor's rectangle reaches the transform"

    testCase "a windowed occupancy draw keeps an anchor whose cell is outside"
    <| fun _ ->
      let ctx = ctxFromPairs 1
      let grid = spanGrid()
      let occupancy = scanSpans grid

      let drawn(left, top, right, bottom) =
        use buf = new RenderBuffer3D()

        ctx.RenderWindowInstanced(
          buf,
          left,
          top,
          right,
          bottom,
          grid,
          occupancy
        )

        buf.Count

      // cell (2,1) is covered by the span anchored at (0,0)
      Expect.equal (drawn(32, 16, 47, 31)) 1 "the covered cell draws its anchor"

      // cell (5,0) is covered by nothing
      Expect.equal
        (drawn(80, 0, 95, 15))
        0
        "a window that touches no anchor draws nothing"

      // cell (7,1) is a plain instance of its own
      Expect.equal (drawn(112, 16, 127, 31)) 1 "a plain anchor draws itself"

    testCase "the occupancy form renders an effect scope per key"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 1
      let grid = spanGrid()
      let occupancy = scanSpans grid

      ctx.RenderWindowInstancedWithEffect(
        buf,
        0,
        0,
        400,
        400,
        grid,
        occupancy,
        (fun _ -> ValueSome(dummyEffect()))
      )

      Expect.equal
        (cmdSequence buf)
        [| "begin"; "draw"; "end"; "begin"; "draw"; "end" |]
        "each key's draw is wrapped"

    testCase "the whole-map occupancy form draws one instance per anchor"
    <| fun _ ->
      use buf = new RenderBuffer3D()
      let ctx = ctxFromPairs 2
      let grid = spanGrid()
      let occupancy = scanSpans grid

      ctx.RenderInstanced(buf, grid, occupancy)

      Expect.equal buf.Count 4 "two keys, two sub-meshes each"
  ]
