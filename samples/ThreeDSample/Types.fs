module ThreeDSample.Types

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Numerics
open Raylib_cs
open Mibo.Elmish
open Mibo.Elmish.Graphics3D
open Mibo.Animation
open Mibo.Input

[<Struct>]
type GameAction =
  | MoveLeft
  | MoveRight
  | MoveForward
  | MoveBackward
  | Jump
  | Respawn
  | RotateCameraLeft
  | RotateCameraRight
  | RotateCameraUp
  | RotateCameraDown

[<Struct>]
type BlockType =
  | Empty
  | Ground
  | GroundSlopeXPos
  | GroundSlopeXNeg
  | GroundSlopeZPos
  | GroundSlopeZNeg
  | Platform
  | PlatformRamp
  | SnowGround
  | SnowSlopeXPos
  | SnowSlopeXNeg
  | SnowSlopeZPos
  | SnowSlopeZNeg
  | Spikes
  | TreePine
  | TreeSnow
  | Rock
  | GrassTuft
  | Coin
  | Jewel
  | Heart
  | Star
  | Mushrooms
  | Crate
  | Barrel
  | Flag
  | MushroomLight

module BlockType =
  let modelPath =
    function
    | Ground -> Constants.KenneyModels.blockGrass
    | GroundSlopeXPos -> Constants.KenneyModels.blockGrassSlope
    | GroundSlopeXNeg -> Constants.KenneyModels.blockGrassSlope
    | GroundSlopeZPos -> Constants.KenneyModels.blockGrassSlope
    | GroundSlopeZNeg -> Constants.KenneyModels.blockGrassSlope
    | Platform -> Constants.KenneyModels.platform
    | PlatformRamp -> Constants.KenneyModels.platformRamp
    | SnowGround -> Constants.KenneyModels.blockSnow
    | SnowSlopeXPos -> Constants.KenneyModels.blockSnowSlope
    | SnowSlopeXNeg -> Constants.KenneyModels.blockSnowSlope
    | SnowSlopeZPos -> Constants.KenneyModels.blockSnowSlope
    | SnowSlopeZNeg -> Constants.KenneyModels.blockSnowSlope
    | Spikes -> Constants.KenneyModels.spikeBlock
    | TreePine -> Constants.KenneyModels.treePine
    | TreeSnow -> Constants.KenneyModels.treeSnow
    | Rock -> Constants.KenneyModels.rocks
    | GrassTuft -> Constants.KenneyModels.grass
    | Coin -> Constants.KenneyModels.coinGold
    | Jewel -> Constants.KenneyModels.jewel
    | Heart -> Constants.KenneyModels.heart
    | Star -> Constants.KenneyModels.star
    | Mushrooms -> Constants.KenneyModels.mushrooms
    | Crate -> Constants.KenneyModels.crate
    | Barrel -> Constants.KenneyModels.barrel
    | Flag -> Constants.KenneyModels.flag
    | MushroomLight -> Constants.KenneyModels.mushrooms
    | Empty -> ""

  let modelVerticalOffset =
    function
    | Platform
    | PlatformRamp -> Constants.cellSize * 0.5f
    | Coin
    | Jewel
    | Heart
    | Star
    | Flag -> Constants.cellSize * 0.5f
    | _ -> 0.0f

  let modelRotation =
    function
    | GroundSlopeXNeg -> 180.0f
    | GroundSlopeZPos -> 90.0f
    | GroundSlopeZNeg -> -90.0f
    | SnowSlopeXNeg -> 180.0f
    | SnowSlopeZPos -> 90.0f
    | SnowSlopeZNeg -> -90.0f
    | _ -> 0.0f

  let isSolid =
    function
    | Empty
    | Coin
    | Jewel
    | Heart
    | Star
    | GrassTuft
    | Mushrooms
    | MushroomLight
    | Flag -> false
    | _ -> true

  let isCollectible =
    function
    | Coin
    | Jewel
    | Heart
    | Star -> true
    | _ -> false

  let isDecoration =
    function
    | TreePine
    | TreeSnow
    | Rock
    | GrassTuft
    | Mushrooms
    | Flag
    | Barrel
    | Crate -> true
    | _ -> false

  let isLightSource =
    function
    | MushroomLight -> true
    | _ -> false

[<Struct>]
type Chunk = {
  Grid: Mibo.Layout3D.CellGrid3D<BlockType>
  Bounds: BoundingBox
  OriginX: int
  OriginZ: int
}

type MinimapModel(playerPos: Vector3) =
  member val Blocks =
    Dictionary<struct (int * int), struct (float32 * BlockType)>() with get, set

  member val Texture = Unchecked.defaultof<Texture2D> with get, set
  member val TexReady = false with get, set
  member val FrameCounter = 0 with get, set
  member val LastPlayerPos = playerPos with get, set

type DiagnosticsModel() =
  member val Font = Raylib.GetFontDefault() with get, set
  member val Fps = 0 with get, set
  member val ChunkCount = 0 with get, set
  member val Score = 0 with get, set
  member val TimeOfDay = 0.0f with get, set
  member val PlayerX = 0.0f with get, set
  member val PlayerY = 0.0f with get, set
  member val PlayerZ = 0.0f with get, set
  member val IsGrounded = false with get, set
  member val ParticleCount = 0 with get, set

type LightingModel() =
  member val SkyColor = Color.Black with get, set
  member val AmbientColor = Color.Black with get, set
  member val AmbientIntensity = 0.0f with get, set
  member val LightDirection = Vector3.Zero with get, set
  member val LightColor = Color.White with get, set
  member val LightIntensity = 0.0f with get, set

type ParticleModel() =
  member val Positions = Array.zeroCreate<Vector3> 512 with get, set
  member val Velocities = Array.zeroCreate<Vector3> 512 with get, set
  member val Sizes = Array.zeroCreate<Vector2> 512 with get, set
  member val Colors = Array.zeroCreate<Color> 512 with get, set
  member val Count = 0 with get, set
  member val Texture = Unchecked.defaultof<Texture2D> with get, set

type GameModel() =
  member val PlayerPosition = Constants.spawnPosition with get, set
  member val PlayerVelocity = Vector3.Zero with get, set
  member val IsGrounded = false with get, set
  member val CameraYaw = Constants.cameraDefaultYaw with get, set
  member val CameraPitch = Constants.cameraDefaultPitch with get, set

  member val CameraPosition =
    Constants.spawnPosition + Vector3(0.0f, 4.0f, 8.0f) with get, set

  member val CameraTarget = Constants.spawnPosition with get, set
  member val Actions: ActionState<GameAction> = ActionState.empty with get, set
  member val InputMap: InputMap<GameAction> = InputMap.empty with get, set
  member val PlayerModel = Unchecked.defaultof<Model> with get, set

  member val PlayerAnimClips =
    Unchecked.defaultof<Animation3DClips> with get, set

  member val PlayerAnim = Unchecked.defaultof<Animation3DState> with get, set
  member val ModelCache = Dictionary<string, Model>() with get, set

  member val Chunks =
    ConcurrentDictionary<struct (int * int), Chunk>() with get, set

  member val TotalTime = 0.0f with get, set
  member val DayNightTimeOfDay = 12.0f with get, set
  member val DayNightDuration = 60.0f with get, set
  member val Score = 0 with get, set
  member val Seed = 0 with get, set
  member val KeysToRemove = ResizeArray<struct (int * int)>() with get, set
  member val PlayerFacing = 0.0f with get, set
  member val Minimap = MinimapModel Constants.spawnPosition with get, set
  member val Diagnostics = DiagnosticsModel() with get, set
  member val Lighting = LightingModel() with get, set
  member val VisibleLights = ResizeArray<PointLight3D>() with get, set
  member val PendingChunks = HashSet<struct (int * int)>() with get, set
  member val Particles = ParticleModel() with get, set
  member val JumpSound = Unchecked.defaultof<Sound> with get, set

[<Struct>]
type Msg =
  | Tick of tick: GameTime
  | InputMapped of inputs: ActionState<GameAction>
  | ChunkCreated of key: struct (int * int) * chunk: Chunk
  | MinimapReady of image: Raylib_cs.Image
  | MushroomLightsReady of lights: PointLight3D[]
