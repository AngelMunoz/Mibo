module Gamino.Input

open System
open System.Collections.Generic
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Input
open Gamino.Elmish

// --- High Performance Input System ---

type KeyListener = {
    Dispatch: Dispatch<obj>
    OnKeyDown: Keys -> obj
    OnKeyUp: Keys -> obj
}

// The InputService acts as the singleton manager for keyboard events.
// We implement IEngineService so it can be plugged into the main loop.
type InputService() =
    // Note: In a real pluggable system, we might want multiple instances,
    // but for the Subscription helper 'Keyboard.listen' to work without passing context,
    // we use a static backing field for the listeners.
    static let listeners = ResizeArray<KeyListener>()
    let mutable prevKeyboard = KeyboardState()

    interface IEngineService with
        member _.Update(gameTime) =
            let currKeyboard = Keyboard.GetState()
            
            if listeners.Count > 0 then
                let pKeys = prevKeyboard.GetPressedKeys()
                let cKeys = currKeyboard.GetPressedKeys()
                
                // Check Released
                for k in pKeys do
                    if not (currKeyboard.IsKeyDown(k)) then
                        for l in listeners do l.Dispatch (l.OnKeyUp k)

                // Check Pressed
                for k in cKeys do
                    if not (prevKeyboard.IsKeyDown(k)) then
                        for l in listeners do l.Dispatch (l.OnKeyDown k)

            prevKeyboard <- currKeyboard

    /// Register a listener. Returns a token to remove it.
    static member Register (dispatch: Dispatch<'Msg>) (onDown: Keys -> 'Msg) (onUp: Keys -> 'Msg) : IDisposable =
        let listener = {
            Dispatch = fun o -> dispatch (unbox o)
            OnKeyDown = fun k -> box (onDown k)
            OnKeyUp = fun k -> box (onUp k)
        }
        listeners.Add(listener)
        
        { new IDisposable with 
            member _.Dispose() = listeners.Remove(listener) |> ignore 
        }

module Keyboard =
    
    /// Creates a subscription to Keyboard events.
    /// Hooks into the InputService.
    let listen (onKeyDown: Keys -> 'Msg) (onKeyUp: Keys -> 'Msg) : Sub<'Msg> =
        let subId = ["Keyboard"; "Listen"]
        
        let subscribe (dispatch: Dispatch<'Msg>) =
            InputService.Register dispatch onKeyDown onKeyUp

        Sub.Active (subId, subscribe)