namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework
open Mibo.Elmish.Graphics3D

/// <summary>
/// An immutable snapshot of a light set: the ambient slot plus the directional, point, and
/// spot light arrays. Light-state semantics are per light <i>type</i> — each kind is an
/// independent array, so a set may hold any number of directional lights.
/// </summary>
[<Struct>]
type BlockLightSet = {
  /// <summary>The ambient light (single slot; a later ambient command overwrites the earlier one).</summary>
  Ambient: AmbientLight3D voption

  /// <summary>The directional lights, in command order.</summary>
  DirLights: DirectionalLight3D[]

  /// <summary>The point lights, in command order.</summary>
  PointLights: PointLight3D[]

  /// <summary>The spot lights, in command order.</summary>
  SpotLights: SpotLight3D[]
}

/// <summary>The plan for one camera block (<c>BeginCamera</c>/<c>BeginCameraConfig</c> … <c>EndCamera</c>).</summary>
[<Struct>]
type CameraBlockPlan = {
  /// <summary>Whether the block contains at least one light command.</summary>
  HasLightCommands: bool

  /// <summary>
  /// The block's final light set. A block with light commands starts from the frame defaults
  /// and applies its own commands in-order (ambient overwrites; directional/point/spot append).
  /// A block without light commands inherits the running light set at its start: the previous
  /// block's final set plus any light commands issued between the two blocks.
  /// </summary>
  Lights: BlockLightSet

  /// <summary>The block's shadow origin override, if <c>SetShadowOrigin</c> was issued inside it (last one wins).</summary>
  ShadowOrigin: Vector3 voption

  /// <summary>The running <c>EnableShadows</c>/<c>DisableShadows</c> toggle state at the block's start.</summary>
  InitialCastEnabled: bool

  /// <summary>The buffer index of the block's first command (one past its Begin command).</summary>
  StartIndex: int

  /// <summary>The buffer index one past the block's last command (its <c>EndCamera</c>, the next
  /// Begin, or the end of the buffer) — the block's commands are the half-open range
  /// <c>[StartIndex, EndIndex)</c>.</summary>
  EndIndex: int
}

/// <summary>
/// The result of walking a <see cref="T:Mibo.Elmish.Graphics3D.RenderBuffer3D"/> once: how many
/// camera blocks the frame contains, the per-block light/shadow state, and the frame-default
/// light set that blocks with their own light commands reset to.
/// </summary>
/// <remarks>
/// Light commands outside any camera block accumulate into the frame defaults — including
/// commands between blocks (after an <c>EndCamera</c>, before the next <c>BeginCamera</c>) and
/// after the last <c>EndCamera</c> (those affect no block). Between-block commands also join the
/// running light set that blocks without their own light commands inherit.
/// <c>SetShadowOrigin</c> is scoped to the block it appears in and never leaks across blocks.
/// </remarks>
[<Struct>]
type BlockPlan = {
  /// <summary>The number of camera blocks in the buffer.</summary>
  BlockCount: int

  /// <summary>The per-block plans, indexed by block (in buffer order).</summary>
  Blocks: CameraBlockPlan[]

  /// <summary>The frame-default light set: every light command issued outside a camera block.</summary>
  FrameDefaults: BlockLightSet
}

// ─────────────────────────────────────────────────────────────────────────────
// Walk accumulators — the mutable state BlockPlan.build folds over. Reference
// records (one per open block per frame) so helpers mutate them in place; the
// immutable plan records above are the only data that leaves the module.
// ─────────────────────────────────────────────────────────────────────────────

type private LightAccum = {
  mutable Ambient: AmbientLight3D voption
  DirLights: ResizeArray<DirectionalLight3D>
  PointLights: ResizeArray<PointLight3D>
  SpotLights: ResizeArray<SpotLight3D>
}

type private BlockAccum = {
  mutable HasLightCommands: bool
  Lights: LightAccum
  mutable ShadowOrigin: Vector3 voption
  InitialCastEnabled: bool
  StartIndex: int
}

type private WalkState = {
  Defaults: LightAccum
  Running: LightAccum
  Blocks: ResizeArray<CameraBlockPlan>
  mutable Current: BlockAccum voption
  mutable CastEnabled: bool
}

module private BlockPlanWalk =

  let inline lightAccum() : LightAccum = {
    Ambient = ValueNone
    DirLights = ResizeArray<DirectionalLight3D>()
    PointLights = ResizeArray<PointLight3D>()
    SpotLights = ResizeArray<SpotLight3D>()
  }

  let inline applyLight (lights: LightAccum) (cmd: Command3D) =
    match cmd with
    | Command3D.SetAmbientLight a -> lights.Ambient <- ValueSome a
    | Command3D.AddDirectionalLight d -> lights.DirLights.Add d
    | Command3D.AddPointLight p -> lights.PointLights.Add p
    | Command3D.AddSpotLight s -> lights.SpotLights.Add s
    | _ -> ()

  let inline snapshot(lights: LightAccum) : BlockLightSet = {
    Ambient = lights.Ambient
    DirLights = lights.DirLights.ToArray()
    PointLights = lights.PointLights.ToArray()
    SpotLights = lights.SpotLights.ToArray()
  }

  let inline replaceContents (set: BlockLightSet) (accum: LightAccum) =
    accum.Ambient <- set.Ambient
    accum.DirLights.Clear()
    accum.DirLights.AddRange(set.DirLights)
    accum.PointLights.Clear()
    accum.PointLights.AddRange(set.PointLights)
    accum.SpotLights.Clear()
    accum.SpotLights.AddRange(set.SpotLights)

  let inline merged (defaults: LightAccum) (own: LightAccum) : BlockLightSet = {
    Ambient =
      match own.Ambient with
      | ValueSome _ -> own.Ambient
      | ValueNone -> defaults.Ambient
    DirLights =
      Array.append (defaults.DirLights.ToArray()) (own.DirLights.ToArray())
    PointLights =
      Array.append (defaults.PointLights.ToArray()) (own.PointLights.ToArray())
    SpotLights =
      Array.append (defaults.SpotLights.ToArray()) (own.SpotLights.ToArray())
  }

  let inline finalizeBlock
    (defaults: LightAccum)
    (running: LightAccum)
    (endIndex: int)
    (block: BlockAccum)
    : CameraBlockPlan =
    let lights =
      if block.HasLightCommands then
        let final = merged defaults block.Lights
        replaceContents final running
        final
      else
        snapshot running

    {
      HasLightCommands = block.HasLightCommands
      Lights = lights
      ShadowOrigin = block.ShadowOrigin
      InitialCastEnabled = block.InitialCastEnabled
      StartIndex = block.StartIndex
      EndIndex = endIndex
    }

  let closeCurrent (state: WalkState) (endIndex: int) =
    match state.Current with
    | ValueSome block ->
      let plan = finalizeBlock state.Defaults state.Running endIndex block
      state.Blocks.Add plan
      state.Current <- ValueNone
    | ValueNone -> ()

  /// A BeginCamera while a block is open closes that block first (nested camera
  /// commands are tolerated, matching the forward pass).
  let beginBlock (state: WalkState) (startIndex: int) =
    closeCurrent state (startIndex - 1)

    state.Current <-
      ValueSome(
        {
          HasLightCommands = false
          Lights = lightAccum()
          ShadowOrigin = ValueNone
          InitialCastEnabled = state.CastEnabled
          StartIndex = startIndex
        }
      )

  let lightCommand (state: WalkState) (cmd: Command3D) =
    match state.Current with
    | ValueSome block ->
      block.HasLightCommands <- true
      applyLight block.Lights cmd
    | ValueNone ->
      applyLight state.Defaults cmd
      applyLight state.Running cmd

  let shadowOrigin (state: WalkState) (origin: Vector3) =
    match state.Current with
    | ValueSome block -> block.ShadowOrigin <- ValueSome origin
    | ValueNone -> ()

  let finish (state: WalkState) (endIndex: int) : BlockPlan =
    closeCurrent state endIndex

    {
      BlockCount = state.Blocks.Count
      Blocks = state.Blocks.ToArray()
      FrameDefaults = snapshot state.Defaults
    }

/// <summary>Builds a <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.BlockPlan"/> from a render buffer.</summary>
module BlockPlan =

  /// <summary>
  /// Walks the buffer once and produces the per-camera-block light/shadow plan for the frame:
  /// the block count, each block's final light set / shadow origin / initial shadow-caster
  /// toggle / buffer slice, and the frame-default light set.
  /// </summary>
  let build(buffer: RenderBuffer3D) : BlockPlan =
    let state: WalkState = {
      Defaults = BlockPlanWalk.lightAccum()
      Running = BlockPlanWalk.lightAccum()
      Blocks = ResizeArray<CameraBlockPlan>()
      Current = ValueNone
      CastEnabled = true
    }

    for i = 0 to buffer.Count - 1 do
      match buffer[i] with
      | Command3D.BeginCamera _
      | Command3D.BeginCameraConfig _ -> BlockPlanWalk.beginBlock state (i + 1)
      | Command3D.EndCamera -> BlockPlanWalk.closeCurrent state i
      | Command3D.SetAmbientLight _
      | Command3D.AddDirectionalLight _
      | Command3D.AddPointLight _
      | Command3D.AddSpotLight _ as cmd -> BlockPlanWalk.lightCommand state cmd
      | Command3D.SetShadowOrigin origin ->
        BlockPlanWalk.shadowOrigin state origin
      | Command3D.EnableShadows -> state.CastEnabled <- true
      | Command3D.DisableShadows -> state.CastEnabled <- false
      | _ -> ()

    BlockPlanWalk.finish state buffer.Count
