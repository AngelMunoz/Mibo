namespace Mibo.Elmish.Next

open System.Collections.Generic
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Next.Graphics2D

// ─────────────────────────────────────────────────────────────────
// Generic resource registry — single implementation for all backends.
// Forward map: native handle → sequential index (reference identity for
// classes, value equality for structs). Reverse map: index → native.
// ─────────────────────────────────────────────────────────────────

type ResourceRegistry<'T when 'T: equality>() =
  let fwd = Dictionary<'T, int>()
  let rev = ResizeArray<'T>()

  member _.Register(t: 'T) =
    match fwd.TryGetValue t with
    | true, h -> h
    | _ ->
      let h = rev.Count
      rev.Add t
      fwd[t] <- h
      h

  member _.Resolve(h: int) = rev[h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()

// ─────────────────────────────────────────────────────────────────
// UoM-typed facades — thin wrappers over ResourceRegistry<int>
// that tag the handle with the correct measure.
// ─────────────────────────────────────────────────────────────────

type MgTextureRegistry() =
  let inner = ResourceRegistry<Texture2D>()
  member _.Register(t) = inner.Register t * 1<Texture>
  member _.Resolve(h: int<Texture>) = inner.Resolve(int h)
  member _.Clear() = inner.Clear()

type MgFontRegistry() =
  let inner = ResourceRegistry<SpriteFont>()
  member _.Register(f) = inner.Register f * 1<Font>
  member _.Resolve(h: int<Font>) = inner.Resolve(int h)
  member _.Clear() = inner.Clear()

type MgEffectRegistry() =
  let inner = ResourceRegistry<Effect>()
  member _.Register(e) = inner.Register e * 1<Shader>
  member _.Resolve(h: int<Shader>) = inner.Resolve(int h)
  member _.Clear() = inner.Clear()

type MgRenderTargetRegistry() =
  let inner = ResourceRegistry<RenderTarget2D>()
  member _.Register(rt) = inner.Register rt * 1<RenderTarget>
  member _.Resolve(h: int<RenderTarget>) = inner.Resolve(int h)
  member _.Clear() = inner.Clear()
