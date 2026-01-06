module MiboSample.DemoComponents

open System
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics

/// A small, self-contained MonoGame `DrawableGameComponent` used to demonstrate
/// drop-in interoperability through `Program.withComponent`.
///
/// It draws a bouncing colored rectangle as an overlay (it will draw after the Elmish renderers
/// because `ElmishGame.Draw` calls `base.Draw` at the end).
type BouncingBoxOverlay(game: Game) =
    inherit DrawableGameComponent(game)

    let mutable spriteBatch: SpriteBatch = null
    let mutable pixel: Texture2D = null

    let mutable pos = Vector2(50.0f, 50.0f)
    let mutable vel = Vector2(180.0f, 120.0f)
    let size = Vector2(40.0f, 40.0f)

    do
        // Make it obvious this is "on top" of the regular renderer.
        base.DrawOrder <- 10_000

    override _.LoadContent() =
        // These must be created on the main game thread.
        spriteBatch <- new SpriteBatch(game.GraphicsDevice)
        pixel <- new Texture2D(game.GraphicsDevice, 1, 1)
        pixel.SetData([| Color.White |])
        base.LoadContent()

    override _.UnloadContent() =
        if not (isNull pixel) then
            pixel.Dispose()
            pixel <- null

        if not (isNull spriteBatch) then
            spriteBatch.Dispose()
            spriteBatch <- null

        base.UnloadContent()

    override _.Update(gameTime: GameTime) =
        let dt = float32 gameTime.ElapsedGameTime.TotalSeconds

        // Cap dt to avoid tunneling when the app regains focus.
        let dt = min dt 0.05f

        pos <- pos + vel * dt

        let vp = game.GraphicsDevice.Viewport
        let maxX = float32 vp.Width - size.X
        let maxY = float32 vp.Height - size.Y

        if pos.X < 0.0f then
            pos <- Vector2(0.0f, pos.Y)
            vel <- Vector2(abs vel.X, vel.Y)
        elif pos.X > maxX then
            pos <- Vector2(maxX, pos.Y)
            vel <- Vector2(-abs vel.X, vel.Y)

        if pos.Y < 0.0f then
            pos <- Vector2(pos.X, 0.0f)
            vel <- Vector2(vel.X, abs vel.Y)
        elif pos.Y > maxY then
            pos <- Vector2(pos.X, maxY)
            vel <- Vector2(vel.X, -abs vel.Y)

        base.Update(gameTime)

    override _.Draw(gameTime: GameTime) =
        if isNull spriteBatch || isNull pixel then
            ()
        else
            let rect = Rectangle(int pos.X, int pos.Y, int size.X, int size.Y)

            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied)
            spriteBatch.Draw(pixel, rect, Color.LimeGreen)
            spriteBatch.End()

        base.Draw(gameTime)

module BouncingBoxOverlay =

    /// Factory for `Program.withComponent`.
    let create (game: Game) =
        new BouncingBoxOverlay(game) :> IGameComponent


/// A `DrawableGameComponent` that demonstrates *two-way* interop with Elmish:
/// - Component -> Elmish: raises an event when it bounces off the screen bounds.
/// - Elmish -> Component: Elmish can change tint/speed/visibility via the captured handle.
type InteractiveBoxOverlay(game: Game) =
    inherit DrawableGameComponent(game)

    let mutable spriteBatch: SpriteBatch = null
    let mutable pixel: Texture2D = null

    let mutable pos = Vector2(120.0f, 80.0f)
    let mutable vel = Vector2(220.0f, 150.0f)
    let size = Vector2(28.0f, 28.0f)

    let mutable tint = Color.DeepSkyBlue
    let mutable speedScale = 1.0f

    let bounceCount = ref 0
    let bounced = Event<int>()

    do
        base.DrawOrder <- 9_000
        base.Enabled <- true
        base.Visible <- true

    /// Fired each time the box bounces. Payload is the running bounce count.
    member _.Bounced = bounced.Publish

    member _.Tint
        with get () = tint
        and set value = tint <- value

    member _.SpeedScale
        with get () = speedScale
        and set value = speedScale <- max 0.0f value

    member _.SetVisible(value: bool) = base.Visible <- value

    member _.SetEnabled(value: bool) = base.Enabled <- value

    override _.LoadContent() =
        spriteBatch <- new SpriteBatch(game.GraphicsDevice)
        pixel <- new Texture2D(game.GraphicsDevice, 1, 1)
        pixel.SetData([| Color.White |])
        base.LoadContent()

    override _.UnloadContent() =
        if not (isNull pixel) then
            pixel.Dispose()
            pixel <- null

        if not (isNull spriteBatch) then
            spriteBatch.Dispose()
            spriteBatch <- null

        base.UnloadContent()

    override _.Update(gameTime: GameTime) =
        let dt = float32 gameTime.ElapsedGameTime.TotalSeconds
        let dt = min dt 0.05f

        // Apply scaling from Elmish.
        let stepVel = vel * speedScale
        pos <- pos + stepVel * dt

        let vp = game.GraphicsDevice.Viewport
        let maxX = float32 vp.Width - size.X
        let maxY = float32 vp.Height - size.Y

        let mutable didBounce = false

        if pos.X < 0.0f then
            pos <- Vector2(0.0f, pos.Y)
            vel <- Vector2(abs vel.X, vel.Y)
            didBounce <- true
        elif pos.X > maxX then
            pos <- Vector2(maxX, pos.Y)
            vel <- Vector2(-abs vel.X, vel.Y)
            didBounce <- true

        if pos.Y < 0.0f then
            pos <- Vector2(pos.X, 0.0f)
            vel <- Vector2(vel.X, abs vel.Y)
            didBounce <- true
        elif pos.Y > maxY then
            pos <- Vector2(pos.X, maxY)
            vel <- Vector2(vel.X, -abs vel.Y)
            didBounce <- true

        if didBounce then
            bounceCount.Value <- bounceCount.Value + 1
            bounced.Trigger(bounceCount.Value)

        base.Update(gameTime)

    override _.Draw(gameTime: GameTime) =
        if isNull spriteBatch || isNull pixel then
            ()
        else
            let rect = Rectangle(int pos.X, int pos.Y, int size.X, int size.Y)
            spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied)
            spriteBatch.Draw(pixel, rect, tint)
            spriteBatch.End()

        base.Draw(gameTime)

module InteractiveBoxOverlayBridge =

    open Mibo.Elmish

    /// Factory for `Program.withComponentRef`.
    let create (game: Game) = new InteractiveBoxOverlay(game)

    /// Subscribe to the component's bounce event.
    ///
    /// If the component isn't available yet, this subscribes to nothing.
    let subscribeBounced (componentRef: ComponentRef<InteractiveBoxOverlay>) (ofBounce: int -> 'Msg) : Sub<'Msg> =
        let subId = [ "DemoComponents"; "InteractiveBoxOverlay"; "Bounced" ]

        let subscribe (dispatch: Dispatch<'Msg>) =
            match componentRef.TryGet() with
            | ValueSome c -> c.Bounced.Subscribe(fun n -> dispatch (ofBounce n))
            | ValueNone ->
                { new IDisposable with
                    member _.Dispose() = () }

        Sub.Active(subId, subscribe)
