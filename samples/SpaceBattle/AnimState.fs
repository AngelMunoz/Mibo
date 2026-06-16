namespace SpaceBattle

open System.Numerics
open SpaceBattle.Units

module AnimState =

  type MoveTween = {
    From: struct (int * int)
    To: struct (int * int)
    Waypoints: Vector2[]
    SegmentDists: float32[]
    Directions: Direction[]
    SegmentIndex: int
    Progress: float32
    Duration: float32
  }

  type AttackTween = {
    From: Vector2
    To: Vector2
    Direction: Direction
    AttackerCell: struct (int * int)
    TargetCell: struct (int * int)
    Progress: float32
    Duration: float32
  }

  type Banner = {
    Message: string
    Timer: float32
    Duration: float32
  }

  type TurnTransition = {
    NewFaction: Faction
    Timer: float32
    Duration: float32
  }

  type AnimationState =
    | Idle
    | Moving of MoveTween
    | Attacking of AttackTween
    | ShowingBanner of Banner
    | Transitioning of TurnTransition

  [<RequireQualifiedAccess>]
  type AnimationEvent =
    | MoveComplete
    | AttackComplete
    | SegmentChanged of direction: Direction
    | BannerComplete
    | TransitionComplete of newFaction: Faction

  [<RequireQualifiedAccess>]
  type AnimationMsg =
    | StartMove of
      from: struct (int * int) *
      dest: struct (int * int) *
      waypoints: Vector2[] *
      segmentDists: float32[] *
      directions: Direction[] *
      duration: float32
    | StartAttack of
      from: Vector2 *
      ``to``: Vector2 *
      direction: Direction *
      attackerCell: struct (int * int) *
      targetCell: struct (int * int) *
      duration: float32
    | StartTransition of newFaction: Faction * duration: float32
    | ShowBanner of message: string * duration: float32
    | Tick of dt: float32

  let moveDuration (unitMoveRange: int) (totalHexSteps: int) : float32 =
    float32 totalHexSteps * 0.5f / float32 unitMoveRange

  let segmentIndex (segmentDists: float32[]) (t: float32) : int =
    let mutable i = 0

    while i < segmentDists.Length - 2 && segmentDists[i + 1] < t do
      i <- i + 1

    i

  let interpolatePosition
    (waypoints: Vector2[])
    (segmentDists: float32[])
    (t: float32)
    : Vector2 =
    if waypoints.Length = 1 then
      waypoints[0]
    else
      let mutable i = 0

      while i < segmentDists.Length - 2 && segmentDists[i + 1] < t do
        i <- i + 1

      let lo = segmentDists[i]
      let hi = segmentDists[i + 1]
      let localT = if hi - lo < 1e-6f then 0f else (t - lo) / (hi - lo)
      Vector2.Lerp(waypoints[i], waypoints[i + 1], localT)

  let startMove
    (from: struct (int * int))
    (dest: struct (int * int))
    (waypoints: Vector2[])
    (segmentDists: float32[])
    (directions: Direction[])
    (duration: float32)
    (state: AnimationState)
    : AnimationState =
    match state with
    | Idle ->
      Moving {
        From = from
        To = dest
        Waypoints = waypoints
        SegmentDists = segmentDists
        Directions = directions
        SegmentIndex = 0
        Progress = 0.0f
        Duration = duration
      }
    | _ -> state

  let startAttack
    (from: Vector2)
    (target: Vector2)
    (direction: Direction)
    (attackerCell: struct (int * int))
    (targetCell: struct (int * int))
    (duration: float32)
    (state: AnimationState)
    : AnimationState =
    match state with
    | Idle ->
      Attacking {
        From = from
        To = target
        Direction = direction
        AttackerCell = attackerCell
        TargetCell = targetCell
        Progress = 0.0f
        Duration = duration
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

  let startTransition
    (newFaction: Faction)
    (duration: float32)
    (state: AnimationState)
    : AnimationState =
    match state with
    | Idle ->
      Transitioning {
        NewFaction = newFaction
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
        let newIdx = segmentIndex tween.SegmentDists p

        let event =
          if newIdx <> tween.SegmentIndex then
            ValueSome(AnimationEvent.SegmentChanged tween.Directions[newIdx])
          else
            ValueNone

        Moving {
          tween with
              Progress = p
              SegmentIndex = newIdx
        },
        event
    | Attacking tween ->
      let p = tween.Progress + dt / tween.Duration

      if p >= 1.0f then
        Idle, ValueSome AnimationEvent.AttackComplete
      else
        Attacking { tween with Progress = p }, ValueNone
    | ShowingBanner banner ->
      let t = banner.Timer - dt

      if t <= 0.0f then
        Idle, ValueSome AnimationEvent.BannerComplete
      else
        ShowingBanner { banner with Timer = t }, ValueNone
    | Transitioning transition ->
      let t = transition.Timer - dt

      if t <= 0.0f then
        Idle, ValueSome(AnimationEvent.TransitionComplete transition.NewFaction)
      else
        Transitioning { transition with Timer = t }, ValueNone

  module Debug =

    open Mibo.Elmish.Next.Graphics2D
    open Raylib_cs

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

        let struct (y, buffer) =
          DebugUtils.kv
            font
            style
            x
            y
            "Waypoints"
            $"{tween.Waypoints.Length}"
            buffer

        DebugUtils.kv font style x y "Progress" $"{tween.Progress:F2}" buffer
      | Attacking tween ->
        let struct (y, buffer) =
          DebugUtils.kv font style x y "State" "Attacking" buffer

        let struct (y, buffer) =
          DebugUtils.kv
            font
            style
            x
            y
            "From"
            $"{tween.From.X:F0},{tween.From.Y:F0}"
            buffer

        let struct (y, buffer) =
          DebugUtils.kv
            font
            style
            x
            y
            "To"
            $"{tween.To.X:F0},{tween.To.Y:F0}"
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
      | Transitioning transition ->
        let struct (y, buffer) =
          DebugUtils.kv font style x y "State" "Transitioning" buffer

        let struct (y, buffer) =
          DebugUtils.kv
            font
            style
            x
            y
            "Faction"
            (string transition.NewFaction)
            buffer

        DebugUtils.kv
          font
          style
          x
          y
          "Timer"
          $"{transition.Timer:F2}/{transition.Duration:F2}"
          buffer
