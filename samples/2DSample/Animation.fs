module MiboSample.Animation

open Microsoft.Xna.Framework
open Mibo.Elmish
open Mibo.Animation
open Mibo.Input
open MiboSample.Domain

// ─────────────────────────────────────────────────────────────
// Loading: Create sprites at init time
// ─────────────────────────────────────────────────────────────

/// Loaded animation assets for the game
type AnimationAssets = {
  PlayerSprite: AnimatedSprite
  ChestSprite: AnimatedSprite
  CrateSprite: AnimatedSprite
  ItemSprite: AnimatedSprite
}

/// Load all animation assets from content
let load(ctx: GameContext) : AnimationAssets =
  // Character animations from Actions sheet (96x576 = 2x12 grid of 48x48)
  let actionsTex = Assets.texture "Characters/Basic Charakter Actions" ctx

  let idleAnim: Animation = {
    Frames = [| Rectangle(0, 0, 48, 48) |]
    FrameDuration = 1.0f
    Loop = false
  }

  let walkAnim: Animation = {
    Frames = [| for row in 0..3 -> Rectangle(0, row * 48, 48, 48) |]
    FrameDuration = 1.0f / 8.0f
    Loop = true
  }

  let playerSheet =
    SpriteSheet.fromFrames actionsTex (Vector2(24.0f, 24.0f)) [|
      "idle", idleAnim
      "walk", walkAnim
    |]

  let playerSprite =
    AnimatedSprite.create playerSheet "idle" |> AnimatedSprite.withScale 2.0f

  // Chest animation (240x96 = 5x2 grid of 48x48)
  let chestTex = Assets.texture "Objects/Chest" ctx
  let chestFrames = [| for i in 0..4 -> Rectangle(i * 48, 0, 48, 48) |]
  let chestSheet = SpriteSheet.single chestTex chestFrames 3.0f true

  let chestSprite =
    AnimatedSprite.create chestSheet "default" |> AnimatedSprite.withScale 2.0f

  // Crate sprite - dummy pick from tools sheet
  let toolsTex = Assets.texture "Objects/Basic_tools_and_meterials" ctx
  let crateSheet = SpriteSheet.static' toolsTex (Rectangle(16, 16, 16, 16)) // pick a tile

  let crateSprite =
    AnimatedSprite.create crateSheet "default" |> AnimatedSprite.withScale 2.0f

  // Item sprite - pick an egg
  let itemTex = Assets.texture "Objects/Egg_item" ctx
  let itemSheet = SpriteSheet.static' itemTex (Rectangle(0, 0, 16, 16))

  let itemSprite =
    AnimatedSprite.create itemSheet "default" |> AnimatedSprite.withScale 3.0f

  {
    PlayerSprite = playerSprite
    ChestSprite = chestSprite
    CrateSprite = crateSprite
    ItemSprite = itemSprite
  }

// ─────────────────────────────────────────────────────────────
// Update System: ReadonlySystem<ModelSnapshot, 'Msg>
// ─────────────────────────────────────────────────────────────

/// Check if player is moving based on held actions
let private isMoving(actions: ActionState<GameAction>) =
  actions.Held.Contains MoveLeft
  || actions.Held.Contains MoveRight
  || actions.Held.Contains MoveUp
  || actions.Held.Contains MoveDown

/// ReadonlySystem: updates all animated sprites and switches player animation
let update
  (dt: float32)
  (snapshot: ModelSnapshot)
  : struct (ModelSnapshot * Cmd<'Msg>) =
  // Switch player animation based on movement
  let playerSprite =
    if isMoving snapshot.Actions then
      snapshot.PlayerSprite |> AnimatedSprite.play "walk"
    else
      snapshot.PlayerSprite |> AnimatedSprite.play "idle"

  // Update all sprites
  let updatedPlayerSprite = AnimatedSprite.update dt playerSprite
  let updatedDecoration = AnimatedSprite.update dt snapshot.Decoration
  let updatedCrateSprite = AnimatedSprite.update dt snapshot.CrateSprite

  {
    snapshot with
        PlayerSprite = updatedPlayerSprite
        Decoration = updatedDecoration
        CrateSprite = updatedCrateSprite
  },
  Cmd.none
