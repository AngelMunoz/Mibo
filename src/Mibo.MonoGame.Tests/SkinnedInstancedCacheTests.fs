module Mibo.MonoGame.Tests.SkinnedInstancedCache

open System.Reflection
open System.Runtime.CompilerServices
open Expecto
open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish.Graphics3D.Pipelines

// Sentinel effects for reference-identity checks: uninitialized instances, never
// dereferenced — the validator only compares references. (A real Effect needs a
// GraphicsDevice, which the headless harness does not have.)
let private sentinelEffect() : Effect =
  RuntimeHelpers.GetUninitializedObject(typeof<Effect>) :?> Effect

// ModelMeshPart.Effect's setter walks parent.MeshParts (null on a detached part),
// so tests assign the backing field directly. The part itself is uninitialized for
// the same reason — only its Effect reference matters here.
let private effectField =
  typeof<ModelMeshPart>
    .GetField("_effect", BindingFlags.NonPublic ||| BindingFlags.Instance)

let private partWith(fx: Effect) : ModelMeshPart =
  let part =
    RuntimeHelpers.GetUninitializedObject(typeof<ModelMeshPart>)
    :?> ModelMeshPart

  effectField.SetValue(part, fx)
  part

let private swapEffect (part: ModelMeshPart) (fx: Effect) =
  effectField.SetValue(part, fx)

let private metaFor
  (part: ModelMeshPart)
  (effect: Effect)
  : SkinnedInstancedPartMeta =
  {
    Part = part
    Index = 0
    ParentBoneIndex = 0
    IsSkinned = true
    UseGrouped = false
    Technique = null
    SourceEffect = effect
  }

let private entryWith
  (metas: SkinnedInstancedPartMeta[])
  : SkinnedInstancedModelEntry =
  {
    Plain = metas
    Colored = [||]
    MergedMap = null
    InfoIndex = null
    ForEffect = ValueNone
    ForGroupedEffect = ValueNone
  }

[<Tests>]
let skinnedInstancedCacheTests =
  testList "SkinnedInstancedModelEntry validation (MonoGame)" [
    test "entry matches when every part keeps its source effect" {
      let fx = sentinelEffect()
      let part = partWith fx

      Expect.isTrue
        (PbrShading.skinnedInstancedEntryMatches
          ValueNone
          ValueNone
          (entryWith [| metaFor part fx |]))
        "same effect instance on the part"
    }

    test "swapping a part's effect invalidates the entry" {
      let fx = sentinelEffect()
      let other = sentinelEffect()
      let part = partWith fx
      let entry = entryWith [| metaFor part fx |]

      swapEffect part other

      Expect.isFalse
        (PbrShading.skinnedInstancedEntryMatches ValueNone ValueNone entry)
        "the cached IsSkinned/technique were derived from the old effect"
    }

    test "pipeline effect instance mismatch invalidates the entry" {
      let fx = sentinelEffect()
      let part = partWith fx
      let entry = entryWith [| metaFor part fx |]

      Expect.isFalse
        (PbrShading.skinnedInstancedEntryMatches (ValueSome fx) ValueNone entry)
        "entry was built without a main effect, pipeline now has one"
    }
  ]
