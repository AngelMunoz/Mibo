namespace Mibo.Elmish.Graphics3D.Pipelines

open Microsoft.Xna.Framework.Graphics
open Mibo.Elmish

/// <summary>A single post-processing pass applied to the rendered 3D scene.</summary>
/// <remarks>
/// Ported from <c>Mibo.Raylib/Graphics3D/Pipelines/PostProcess3D.fs</c> with the
/// <c>Shader</c> field swapped to native <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>
/// (the compiled <c>.mgfx</c>) per the §4.1 material model: <c>Effect</c> is the
/// native-first shader carrier in MonoGame.
/// </remarks>
[<Struct>]
type PostProcessPass3D = {

  /// <summary>
  /// Effect used for this pass. Receives the scene texture as the active texture (slot 0).
  /// </summary>
  Effect: Effect

  /// <summary>
  /// Optional callback to set effect parameters before rendering the fullscreen quad.
  /// Called once per frame when this pass executes. The <see cref="T:Microsoft.Xna.Framework.Graphics.Effect"/>
  /// is already applied via <c>CurrentTechnique.Passes[0].Apply()</c> when this callback runs.
  /// </summary>
  OnSetup: (Effect -> GameContext -> unit) voption
}

/// <summary>Configuration for post-processing in a 3D pipeline.</summary>
[<Struct>]
type PostProcessConfig3D = {
  /// <summary>Post-processing passes applied in order after scene rendering.</summary>
  Passes: PostProcessPass3D[] voption
}

/// <summary>Convenience values for <see cref="T:Mibo.Elmish.Graphics3D.Pipelines.PostProcessConfig3D"/>.</summary>
module PostProcessConfig3D =

  /// <summary>No post-processing.</summary>
  let none: PostProcessConfig3D = { Passes = ValueNone }
