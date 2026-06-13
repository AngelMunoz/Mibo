module PingPong.Client.Types

open PingPong.Shared.Types
open Mibo.Elmish


// ── Model ──────────────────────────────────────────────────────────────────

type Model = {
  GameState: GameState
  Connected: bool
  PeerId: int<peerId>
}

// ── Messages ───────────────────────────────────────────────────────────────

type Msg =
  | ServerState of byte[]
  | ConnectionChanged of ConnectionState
  | LocalInput of float32
  | Tick of GameTime
