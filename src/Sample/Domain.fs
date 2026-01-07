module MiboSample.Domain

open FSharp.UMX

/// Entity identifier measure type
[<Measure>]
type EntityId

/// Input state for controllable entities
[<Struct>]
type InputState = {
  MovingLeft: bool
  MovingRight: bool
  MovingUp: bool
  MovingDown: bool
  IsFiring: bool
}

module InputState =
  let empty = {
    MovingLeft = false
    MovingRight = false
    MovingUp = false
    MovingDown = false
    IsFiring = false
  }
