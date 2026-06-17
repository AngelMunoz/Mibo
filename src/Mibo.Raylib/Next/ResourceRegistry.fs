namespace Mibo.Elmish.Next

open System.Collections.Generic
open Raylib_cs
open Mibo.Elmish.Next.Graphics2D

// ─────────────────────────────────────────────────────────────────
// Resource registries (raylib backend)
//
// Forward map:  identity-token (uint) → int<Resource>  (O(1) hash)
// Reverse map:  int<Resource> → native handle           (O(1) array)
//
// The identity token is the GPU handle raylib assigns (Texture2D.Id,
// Shader.Id, etc.) — NOT the struct's reflection-based equality.
// ─────────────────────────────────────────────────────────────────

type RaylibTextureRegistry() =
  let fwd = Dictionary<uint, int<Texture>>()
  let rev = ResizeArray<Raylib_cs.Texture2D>()

  member _.Register(t: Raylib_cs.Texture2D) =
    match fwd.TryGetValue t.Id with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<Texture>
      rev.Add t
      fwd[t.Id] <- h
      h

  member _.Resolve(h: int<Texture>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()

type RaylibFontRegistry() =
  let fwd = Dictionary<uint, int<Font>>()
  let rev = ResizeArray<Raylib_cs.Font>()

  member _.Register(f: Raylib_cs.Font) =
    let key = f.Texture.Id

    match fwd.TryGetValue key with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<Font>
      rev.Add f
      fwd[key] <- h
      h

  member _.Resolve(h: int<Font>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()

type RaylibShaderRegistry() =
  let fwd = Dictionary<uint, int<Shader>>()
  let rev = ResizeArray<Raylib_cs.Shader>()

  member _.Register(s: Raylib_cs.Shader) =
    match fwd.TryGetValue(uint s.Id) with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<Shader>
      rev.Add s
      fwd[uint s.Id] <- h
      h

  member _.Resolve(h: int<Shader>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()

type RaylibRenderTargetRegistry() =
  let fwd = Dictionary<uint, int<RenderTarget>>()
  let rev = ResizeArray<Raylib_cs.RenderTexture2D>()

  member _.Register(rt: Raylib_cs.RenderTexture2D) =
    match fwd.TryGetValue rt.Id with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<RenderTarget>
      rev.Add rt
      fwd[rt.Id] <- h
      h

  member _.Resolve(h: int<RenderTarget>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()

type RaylibMeshRegistry() =
  let fwd = Dictionary<int, int<Mesh>>()
  let rev = ResizeArray<Raylib_cs.Mesh>()

  // TODO: GenericHash is expensive (reflection on ~20 fields incl. native pointers)
  // and collision-prone. Replace with Mesh.VaoId or a registration wrapper when
  // the 3D pipeline is ported. See plan §2 (handle reliability).
  member _.Register(m: Raylib_cs.Mesh) =
    let key = LanguagePrimitives.GenericHash m

    match fwd.TryGetValue key with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<Mesh>
      rev.Add m
      fwd[key] <- h
      h

  member _.Resolve(h: int<Mesh>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()

type RaylibModelRegistry() =
  let fwd = Dictionary<int, int<ModelAsset>>()
  let rev = ResizeArray<Raylib_cs.Model>()

  // TODO: GenericHash is expensive (reflection on many fields) and collision-prone.
  // Replace with a proper identity token when the 3D pipeline is ported.
  member _.Register(m: Raylib_cs.Model) =
    let key = LanguagePrimitives.GenericHash m

    match fwd.TryGetValue key with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<ModelAsset>
      rev.Add m
      fwd[key] <- h
      h

  member _.Resolve(h: int<ModelAsset>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()

type LightContextRegistry() =
  let fwd = Dictionary<obj, int<LightContext>>()
  let rev = ResizeArray<Mibo.Elmish.Next.Graphics2D.Lighting.LightContext2D>()

  member _.Register(ctx: Mibo.Elmish.Next.Graphics2D.Lighting.LightContext2D) =
    let key = box ctx

    match fwd.TryGetValue key with
    | true, h -> h
    | _ ->
      let h = rev.Count * 1<LightContext>
      rev.Add ctx
      fwd[key] <- h
      h

  member _.Resolve(h: int<LightContext>) = rev[int h]

  member _.Clear() =
    fwd.Clear()
    rev.Clear()
