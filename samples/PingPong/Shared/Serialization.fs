module PingPong.Shared.Serialization

open System.Text.Json
open System.Text.Json.Nodes
open JDeck
open PingPong.Shared.Types

// ── Encoders ───────────────────────────────────────────────────────────────

let encodeBall(ball: Ball) : JsonNode =
  Json.object [
    "x", Encode.single ball.Position.X
    "y", Encode.single ball.Position.Y
    "vx", Encode.single ball.Velocity.X
    "vy", Encode.single ball.Velocity.Y
  ]

let encodePaddleSide(side: PaddleSide) : JsonNode =
  match side with
  | Left -> Encode.string "Left"
  | Right -> Encode.string "Right"

let encodePaddle(paddle: Paddle) : JsonNode =
  Json.object [
    "side", encodePaddleSide paddle.Side
    "y", Encode.single paddle.Y
  ]

let encodeScores(scores: Scores) : JsonNode =
  Json.object [
    "left", Encode.int scores.Left
    "right", Encode.int scores.Right
  ]

let encodeGameState(state: GameState) : JsonNode =
  Json.object [
    "ball", encodeBall state.Ball
    "leftPaddle", encodePaddle state.LeftPaddle
    "rightPaddle", encodePaddle state.RightPaddle
    "scores", encodeScores state.Scores
    "width", Encode.single state.Width
    "height", Encode.single state.Height
  ]

let encodeClientMsg(msg: ClientMsg) : JsonNode =
  match msg with
  | MovePaddle(side, y) ->
    Json.object [
      "type", Encode.string "MovePaddle"
      "side", encodePaddleSide side
      "y", Encode.single y
    ]

// ── Decoders ───────────────────────────────────────────────────────────────

let decodeBall: Decoder<Ball> =
  fun el -> decode {
    let! x = el |> Required.Property.get("x", Required.single)
    and! y = el |> Required.Property.get("y", Required.single)
    and! vx = el |> Required.Property.get("vx", Required.single)
    and! vy = el |> Required.Property.get("vy", Required.single)

    return {
      Position = System.Numerics.Vector2(x, y)
      Velocity = System.Numerics.Vector2(vx, vy)
    }
  }

let decodePaddleSide: Decoder<PaddleSide> =
  fun el ->
    match el.GetString() with
    | "Left" -> Ok Left
    | "Right" -> Ok Right
    | other -> Error(DecodeError.ofError(el, $"Unknown paddle side: {other}"))

let decodePaddle: Decoder<Paddle> =
  fun el -> decode {
    let! side = el |> Required.Property.get("side", decodePaddleSide)
    and! y = el |> Required.Property.get("y", Required.single)
    return { Side = side; Y = y }
  }

let decodeScores: Decoder<Scores> =
  fun el -> decode {
    let! left = el |> Required.Property.get("left", Required.int)
    and! right = el |> Required.Property.get("right", Required.int)
    return { Left = left; Right = right }
  }

let decodeGameState: Decoder<GameState> =
  fun el -> decode {
    let! ball = el |> Required.Property.get("ball", decodeBall)
    and! leftPaddle = el |> Required.Property.get("leftPaddle", decodePaddle)
    and! rightPaddle = el |> Required.Property.get("rightPaddle", decodePaddle)
    and! scores = el |> Required.Property.get("scores", decodeScores)
    and! width = el |> Required.Property.get("width", Required.single)
    and! height = el |> Required.Property.get("height", Required.single)

    return {
      Ball = ball
      LeftPaddle = leftPaddle
      RightPaddle = rightPaddle
      Scores = scores
      Width = width
      Height = height
    }
  }

let decodeClientMsg: Decoder<ClientMsg> =
  fun el -> decode {
    let! msgType = el |> Required.Property.get("type", Required.string)

    match msgType with
    | "MovePaddle" ->
      let! side = el |> Required.Property.get("side", decodePaddleSide)
      and! y = el |> Required.Property.get("y", Required.single)
      return MovePaddle(side, y)
    | other ->
      return! Error(DecodeError.ofError(el, $"Unknown message type: {other}"))
  }

// ── Options ────────────────────────────────────────────────────────────────

let jsonOptions =
  JsonSerializerOptions()
  |> Codec.useCodec(encodeGameState, decodeGameState)
  |> Codec.useCodec(encodeClientMsg, decodeClientMsg)

// ── Helpers ────────────────────────────────────────────────────────────────

let serializeGameState(state: GameState) : byte[] =
  JsonSerializer.SerializeToUtf8Bytes(state, jsonOptions)

let deserializeGameState(bytes: byte[]) : GameState =
  JsonSerializer.Deserialize<GameState>(bytes, jsonOptions)

let serializeClientMsg(msg: ClientMsg) : byte[] =
  JsonSerializer.SerializeToUtf8Bytes(msg, jsonOptions)

let deserializeClientMsg(bytes: byte[]) : ClientMsg =
  JsonSerializer.Deserialize<ClientMsg>(bytes, jsonOptions)
