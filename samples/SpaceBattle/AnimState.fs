namespace SpaceBattle

open System.Numerics

module AnimState =

  type MoveTween = {
    From: struct (int * int)
    To: struct (int * int)
    FromPos: Vector2
    ToPos: Vector2
    Progress: float32
    Duration: float32
  }

  type Banner = {
    Message: string
    Timer: float32
    Duration: float32
  }

  type AnimationState =
    | Idle
    | Moving of MoveTween
    | ShowingBanner of Banner

  [<RequireQualifiedAccess>]
  type AnimationEvent =
    | MoveComplete
    | BannerComplete

  [<RequireQualifiedAccess>]
  type AnimationMsg =
    | StartMove of
      from: struct (int * int) *
      dest: struct (int * int) *
      fromPos: Vector2 *
      toPos: Vector2
    | ShowBanner of message: string * duration: float32
    | Tick of dt: float32

  let startMove
    (from: struct (int * int))
    (dest: struct (int * int))
    (fromPos: Vector2)
    (toPos: Vector2)
    (state: AnimationState)
    : AnimationState =
    match state with
    | Idle ->
      Moving {
        From = from
        To = dest
        FromPos = fromPos
        ToPos = toPos
        Progress = 0.0f
        Duration = 0.3f
      }
    | _ -> state

  let showBanner
    (message: string)
    (duration: float32)
    (state: AnimationState)
    : AnimationState =
    match state with
    | Idle ->
      ShowingBanner {
        Message = message
        Timer = duration
        Duration = duration
      }
    | _ -> state

  let inline update
    (dt: float32)
    (state: AnimationState)
    : struct (AnimationState * AnimationEvent voption) =
    match state with
    | Idle -> state, ValueNone
    | Moving tween ->
      let p = tween.Progress + dt / tween.Duration

      if p >= 1.0f then
        Idle, ValueSome AnimationEvent.MoveComplete
      else
        Moving { tween with Progress = p }, ValueNone
    | ShowingBanner banner ->
      let t = banner.Timer - dt

      if t <= 0.0f then
        Idle, ValueSome AnimationEvent.BannerComplete
      else
        ShowingBanner { banner with Timer = t }, ValueNone

  module Debug =

    open Raylib_cs
    open Mibo.Elmish.Graphics2D

    let inline view
      (font: Font)
      (style: DebugUtils.DebugStyle)
      (state: AnimationState)
      (x: int)
      (y: int)
      (buffer: RenderBuffer2D)
      : struct (int * RenderBuffer2D) =
      let struct (y, buffer) =
        DebugUtils.section font style x y "Animation" buffer

      match state with
      | Idle -> DebugUtils.kv font style x y "State" "Idle" buffer
      | Moving tween ->
        let struct (y, buffer) =
          DebugUtils.kv font style x y "State" "Moving" buffer

        let struct (y, buffer) =
          DebugUtils.kv
            font
            style
            x
            y
            "From"
            (DebugUtils.formatCell tween.From)
            buffer

        let struct (y, buffer) =
          DebugUtils.kv
            font
            style
            x
            y
            "To"
            (DebugUtils.formatCell tween.To)
            buffer

        DebugUtils.kv font style x y "Progress" $"{tween.Progress:F2}" buffer
      | ShowingBanner banner ->
        let struct (y, buffer) =
          DebugUtils.kv font style x y "State" "Banner" buffer

        let struct (y, buffer) =
          DebugUtils.kv font style x y "Message" banner.Message buffer

        DebugUtils.kv
          font
          style
          x
          y
          "Timer"
          $"{banner.Timer:F2}/{banner.Duration:F2}"
          buffer
