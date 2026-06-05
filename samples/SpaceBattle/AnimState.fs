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
