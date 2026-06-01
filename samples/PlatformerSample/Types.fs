module PlatformerSample.Types

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Raylib_cs
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics2D.Lighting
open Mibo.Input
open Mibo.Animation

// -------------------------------------------------------------
// Domain Types
// -------------------------------------------------------------
[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | Jump
  | Respawn

[<Struct>]
type AnimationState =
  | Idle
  | Walk
  | Jump
  | Fall

[<Struct>]
type TileType =
  | Empty
  | Ground
  | Platform
  | Spikes
  | Coin
  | Flag

[<Struct>]
type Biome =
  | Grass
  | Stone
  | Snow
  | Sand

[<Struct>]
type TorchLight = {
  Position: Vector2
  Color: Color
  Radius: float32
}

[<Struct>]
type Chunk = {
  Grid: Mibo.Layout.CellGrid2D<TileType>
  Platforms: Rectangle[]
  Spikes: Rectangle[]
  Coins: Rectangle[]
  Flags: Rectangle[]
  Occluders: Occluder2D[]
  Torches: TorchLight[]
  Bounds: Rectangle
  Biome: Biome
}

[<Struct>]
type SpriteAssets = {
  PlayerSheet: SpriteSheet
  TileTexture: Texture2D
  TorchSheet: SpriteSheet
  ParticleTexture: Texture2D
  CoinNormalMap: Texture2D
  Font: Font
  JumpSound: Sound
}

// -------------------------------------------------------------
// Minimap Model
// -------------------------------------------------------------

type MinimapModel() =
  member val Blocks =
    Dictionary<struct (int * int), struct (float32 * TileType * Biome)>() with get, set

  member val Texture = Unchecked.defaultof<Texture2D> with get, set
  member val TexReady = false with get, set
  member val FrameCounter = 0 with get, set
  member val LastPlayerPos = Vector2.Zero with get, set


type Diagnostics() =
  member val Fps = 0 with get, set
  member val FrameTime = 0.0f with get, set

// -------------------------------------------------------------
// Mutable Model (Level 2.5 — reduces GC pressure)
// -------------------------------------------------------------

type Model() as self =
  member val PlayerPosition = Vector2(200.0f, 0.0f) with get, set
  member val PlayerVelocity = Vector2.Zero with get, set
  member val PlayerFacing = 1.0f with get, set
  member val IsGrounded = true with get, set
  member val Camera: Camera2D = Unchecked.defaultof<_> with get, set
  member val Actions: ActionState<GameAction> = ActionState.empty with get, set
  member val InputMap: InputMap<GameAction> = InputMap.empty with get, set
  member val Assets: SpriteAssets = Unchecked.defaultof<_> with get, set
  member val TotalTime = 0.0f with get, set
  member val AnimationState = Idle with get, set
  member val PlayerSprite: AnimatedSprite = Unchecked.defaultof<_> with get, set
  member val TorchSprite: AnimatedSprite = Unchecked.defaultof<_> with get, set
  member val PlayerChunk = struct (0, 0) with get, set

  member val Chunks =
    ConcurrentDictionary<struct (int * int), Chunk>() with get, set

  member val Seed = 0 with get, set
  member val DayNightTimeOfDay = 12.0f with get, set
  member val DayNightDuration = 60.0f with get, set
  member val Lighting: LightContext2D = Unchecked.defaultof<_> with get, set
  member val Particles: Particle2D[] = Array.zeroCreate 512 with get, set
  member val ParticleVelocities: Vector2[] = Array.zeroCreate 512 with get, set
  member val ParticleCount = 0 with get, set
  member val Score = 0 with get, set
  member val PendingChunks = HashSet<struct (int * int)>() with get, set

  member val Diagnostics = Diagnostics() with get, set
  member val Minimap = MinimapModel() with get, set

  interface IDisposable with
    member _.Dispose() =
      if self.Lighting <> Unchecked.defaultof<_> then
        (self.Lighting :> IDisposable).Dispose()

// -------------------------------------------------------------
// Struct Messages (Level 2.5 — zero-allocation dispatch)
// -------------------------------------------------------------

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputMapped of inputs: ActionState<GameAction>
  | ChunkCreated of key: struct (int * int) * chunk: Chunk
  | MinimapReady of image: Raylib_cs.Image
