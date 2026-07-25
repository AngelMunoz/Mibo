module Mibo.MonoGame.Tests.Layout3D

open Expecto
open System
open Microsoft.Xna.Framework
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
  ]
