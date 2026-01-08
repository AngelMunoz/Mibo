namespace Mibo.Elmish

/// <summary>
/// Generic system pipeline for composing frame updates with type-enforced snapshot boundaries.
/// </summary>
/// <remarks>
/// This module provides a pipeline pattern where mutable systems run first (physics,
/// particles), then a snapshot is taken, and readonly systems run on the snapshot.
/// The type system enforces this ordering at compile time.
/// <para>Pattern Overview:</para>
/// <ol>
/// <li><see cref="M:Mibo.Elmish.System.start"/> - Begin with mutable model</li>
/// <li><see cref="M:Mibo.Elmish.System.pipeMutable"/> - Run systems that mutate (physics, AI movement)</li>
/// <li><see cref="M:Mibo.Elmish.System.snapshot"/> - Take readonly snapshot (type changes from Model to Snapshot)</li>
/// <li><see cref="M:Mibo.Elmish.System.pipe"/> - Run readonly systems (rendering prep, queries)</li>
/// <li><see cref="M:Mibo.Elmish.System.finish"/> - Convert back to model and batch commands</li>
/// </ol>
/// </remarks>
/// <example>
/// <code>
/// let updateSystems model =
///     model
///     |&gt; System.start
///     |&gt; System.pipeMutable physicsSystem
///     |&gt; System.pipeMutable particleSystem
///     |&gt; System.snapshot Model.toSnapshot
///     |&gt; System.pipe aiDecisionSystem
///     |&gt; System.finish Model.fromSnapshot
/// </code>
/// </example>
module System =

  /// Start pipeline with mutable model.
  let inline start(model: 'Model) : 'Model * Cmd<'Msg> list = (model, [])

  /// <summary>Pipe a mutable system (pre-snapshot phase).</summary>
  /// <remarks>Use for systems that need to mutate model state (physics, particles). The system receives the model and returns updated model with commands.</remarks>
  let inline pipeMutable
    (system: 'Model -> struct ('Model * Cmd<'Msg> list))
    (model: 'Model, cmds: Cmd<'Msg> list)
    : 'Model * Cmd<'Msg> list =
    let struct (newModel, newCmds) = system model
    (newModel, cmds @ newCmds)

  /// <summary>Transition from mutable Model to readonly Snapshot.</summary>
  /// <remarks>This is the "barrier" in the pipeline. After calling snapshot, only readonly systems (using <see cref="M:Mibo.Elmish.System.pipe"/>) can be added.</remarks>
  /// <example>
  /// <code>
  /// |&gt; System.snapshot Model.toSnapshot
  /// </code>
  /// </example>
  let inline snapshot
    (toSnapshot: 'Model -> 'Snapshot)
    (model: 'Model, cmds: Cmd<'Msg> list)
    : 'Snapshot * Cmd<'Msg> list =
    (toSnapshot model, cmds)

  /// <summary>Pipe a readonly system (post-snapshot phase).</summary>
  /// <remarks>Use for systems that read state but don't mutate it (rendering prep, AI decisions that only emit commands, query systems).</remarks>
  let inline pipe
    (system: 'Snapshot -> struct ('Snapshot * Cmd<'Msg> list))
    (snap: 'Snapshot, cmds: Cmd<'Msg> list)
    : 'Snapshot * Cmd<'Msg> list =
    let struct (newSnap, newCmds) = system snap
    (newSnap, cmds @ newCmds)

  /// <summary>Finish pipeline: convert snapshot back to model, batch all commands.</summary>
  /// <remarks>Returns a tuple ready for the Elmish update function.</remarks>
  let inline finish
    (fromSnapshot: 'Snapshot -> 'Model)
    (snap: 'Snapshot, cmds: Cmd<'Msg> list)
    : struct ('Model * Cmd<'Msg>) =
    struct (fromSnapshot snap, Cmd.batch cmds)
