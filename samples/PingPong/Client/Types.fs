module PingPong.Client.Types

open System
open PingPong.Shared.Types

// ── Connection State (client-only) ─────────────────────────────────────────

[<Struct>]
type ConnectionState =
  | Disconnected
  | Connecting
  | Connected
  | Reconnecting

// ── Client Transport (client-only) ─────────────────────────────────────────
// Point-to-point pipe to a single server. No peer multiplexing.

type IClientTransport =
  abstract State: ConnectionState
  abstract StateChanged: IObservable<ConnectionState>
  abstract MessageReceived: IObservable<byte[]>
  abstract Send: data: byte[] -> unit
  abstract Connect: address: string -> unit
  abstract Disconnect: unit -> unit

// ── Model ──────────────────────────────────────────────────────────────────

type Model = {
  LocalState: GameState
  ServerState: GameState voption
  AssignedPaddle: PaddleSide voption
  Connected: bool
  PeerId: int<peerId>
}

// ── Messages ───────────────────────────────────────────────────────────────

type Msg =
  | ConnectionChanged of
    state: ConnectionState *
    peerId: int<peerId> *
    side: PaddleSide voption
  | ServerState of GameState
  | LocalInput of float32
