module MiboSample.Crates

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open FSharp.UMX

open Mibo.Elmish
open Mibo.Elmish.Graphics2D
open Mibo.Animation
open MiboSample.Domain

let targetCount = 100
let crateSize = Vector2(18f, 18f)

[<Struct>]
type RetryMode =
  | Deferred
  | Immediate

let private rectFromCenter (center: Vector2) (size: Vector2) : Rectangle =
  let x = int(center.X - size.X * 0.5f)
  let y = int(center.Y - size.Y * 0.5f)
  Rectangle(x, y, int size.X, int size.Y)

let intersects
  (aPos: Vector2)
  (aSize: Vector2)
  (bPos: Vector2)
  (bSize: Vector2)
  : bool =
  (rectFromCenter aPos aSize).Intersects(rectFromCenter bPos bSize)

let ensureTarget<'Msg>
  (mode: RetryMode)
  (mkSpawn: unit -> 'Msg)
  (snapshot: ModelSnapshot)
  : struct (ModelSnapshot * Cmd<'Msg>) =
  if snapshot.Crates.Count >= targetCount then
    struct (snapshot, Cmd.none)
  else
    let cmd =
      match mode with
      | Deferred -> Cmd.ofMsg(mkSpawn()) |> Cmd.deferNextFrame
      | Immediate -> Cmd.ofMsg(mkSpawn())

    struct (snapshot, cmd)

let spawnOne
  (mode: RetryMode)
  (mkSpawn: unit -> 'Msg)
  (width: int)
  (height: int)
  (model: Model)
  : struct (Model * Cmd<'Msg>) =
  if model.Crates.Count >= targetCount then
    struct (model, Cmd.none)
  else

  let rng = Random.Shared

  let playerPos = model.Positions[model.PlayerId]

  let playerSize =
    model.Sizes
    |> Map.tryFind model.PlayerId
    |> Option.defaultValue(Vector2(32f, 32f))

  let marginX = int(crateSize.X * 0.5f) + 2
  let marginY = int(crateSize.Y * 0.5f) + 2

  let jitterX = rng.Next(-600, 601)
  let jitterY = rng.Next(-600, 601)

  let rawX = int playerPos.X + jitterX
  let rawY = int playerPos.Y + jitterY

  let x = max marginX (min (width - marginX) rawX)
  let y = max marginY (min (height - marginY) rawY)

  let pos = Vector2(float32 x, float32 y)

  if intersects playerPos playerSize pos crateSize then
    if model.Crates.Count >= targetCount then
      struct (model, Cmd.none)
    else
      let cmd =
        match mode with
        | Deferred -> Cmd.ofMsg(mkSpawn()) |> Cmd.deferNextFrame
        | Immediate -> Cmd.ofMsg(mkSpawn())

      struct (model, cmd)
  else
    let id = Guid.NewGuid() |> UMX.tag<EntityId>
    model.Crates.Add id
    model.Positions[id] <- pos

    if mode = Immediate && model.Crates.Count < targetCount then
      struct (model, Cmd.ofMsg(mkSpawn()))
    else
      struct (model, Cmd.none)

let removeCrate (crateId: Guid<EntityId>) (model: Model) : Model =
  model.Positions.Remove(crateId) |> ignore

  let mutable i = model.Crates.Count - 1

  while i >= 0 do
    if model.Crates[i] = crateId then
      model.Crates.RemoveAt(i)

    i <- i - 1

  {
    model with
        CrateHits = model.CrateHits + 1
  }

let detectFirstOverlap<'Msg>
  (mkHit: Guid<EntityId> -> 'Msg)
  (snapshot: ModelSnapshot)
  : struct (ModelSnapshot * Cmd<'Msg>) =
  let playerPos = snapshot.Positions[snapshot.PlayerId]

  let playerSize =
    snapshot.Sizes
    |> Map.tryFind snapshot.PlayerId
    |> Option.defaultValue(Vector2(32f, 32f))

  let mutable cmd = Cmd.none
  let mutable found = false
  let mutable i = 0

  while not found && i < snapshot.Crates.Count do
    let crateId = snapshot.Crates[i]

    match snapshot.Positions.TryGetValue(crateId) with
    | true, cratePos when intersects playerPos playerSize cratePos crateSize ->
      cmd <- Cmd.ofMsg(mkHit crateId)
      found <- true
    | _ -> ()

    i <- i + 1

  if found then
    struct (snapshot, cmd)
  else
    struct (snapshot, Cmd.none)

let view
  (ctx: GameContext)
  (snapshot: ModelSnapshot)
  (buffer: RenderBuffer<RenderCmd2D>)
  =
  for i = 0 to snapshot.Crates.Count - 1 do
    let crateId = snapshot.Crates[i]

    match snapshot.Positions.TryGetValue(crateId) with
    | true, pos ->
      snapshot.CrateSprite |> AnimatedSprite.draw pos 2<RenderLayer> buffer
    | _ -> ()
