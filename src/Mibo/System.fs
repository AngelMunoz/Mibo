namespace Mibo.Elmish

/// <summary>
/// Generic system pipeline for composing frame updates with type-enforced snapshot boundaries.
/// </summary>
/// <remarks>
/// This module provides a pipeline pattern where mutable systems run first (physics,
/// particles), then a snapshot is taken, and readonly systems run on the snapshot.
/// The type system enforces this ordering at compile time.
/// <para>
/// The pipeline accumulates a single <see cref="T:Mibo.Elmish.Cmd`1" /> (not a list) to keep it fast:
/// no list appends, no reversing, and no quadratic behavior as you add phases.
/// </para>
/// <para>Pattern Overview:</para>
/// <ol>
/// <li><see cref="M:Mibo.Elmish.System.start"/> - Begin with mutable model</li>
/// <li><see cref="M:Mibo.Elmish.System.pipeMutable"/> - Run systems that mutate (physics, AI movement)</li>
/// <li><see cref="M:Mibo.Elmish.System.snapshot"/> - Take readonly snapshot (type changes from Model to Snapshot)</li>
/// <li><see cref="M:Mibo.Elmish.System.pipe"/> - Run readonly systems (rendering prep, queries)</li>
/// <li><see cref="M:Mibo.Elmish.System.finish"/> - Convert back to model and return the accumulated command</li>
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
  let inline start(model: 'Model) : struct ('Model * Cmd<'Msg>) =
    model, Cmd.none

  let inline private combine (a: Cmd<'Msg>) (b: Cmd<'Msg>) : Cmd<'Msg> =
    match a, b with
    | Cmd.Empty, x -> x
    | x, Cmd.Empty -> x
    | _ -> Cmd.batch2(a, b)

  /// <summary>Pipe a mutable system (pre-snapshot phase).</summary>
  /// <remarks>Use for systems that need to mutate model state (physics, particles). The system receives the model and returns updated model with commands.</remarks>
  let inline pipeMutable
    ([<InlineIfLambda>] system: 'Model -> struct ('Model * Cmd<'Msg>))
    struct (model: 'Model, cmds: Cmd<'Msg>)
    : struct ('Model * Cmd<'Msg>) =
    let struct (newModel, newCmds) = system model
    newModel, combine cmds newCmds

  /// <summary>Transition from mutable Model to readonly Snapshot.</summary>
  /// <remarks>This is the "barrier" in the pipeline. After calling snapshot, only readonly systems (using <see cref="M:Mibo.Elmish.System.pipe"/>) can be added.</remarks>
  /// <example>
  /// <code>
  /// |&gt; System.snapshot Model.toSnapshot
  /// </code>
  /// </example>
  let inline snapshot
    ([<InlineIfLambda>] toSnapshot: 'Model -> 'Snapshot)
    struct (model: 'Model, cmds: Cmd<'Msg>)
    : struct ('Snapshot * Cmd<'Msg>) =
    toSnapshot model, cmds

  /// <summary>Pipe a readonly system (post-snapshot phase).</summary>
  /// <remarks>Use for systems that read state but don't mutate it (rendering prep, AI decisions that only emit commands, query systems).</remarks>
  let inline pipe
    ([<InlineIfLambda>] system: 'Snapshot -> struct ('Snapshot * Cmd<'Msg>))
    struct (snap: 'Snapshot, cmds: Cmd<'Msg>)
    : struct ('Snapshot * Cmd<'Msg>) =
    let struct (newSnap, newCmds) = system snap
    newSnap, combine cmds newCmds

  /// <summary>Dispatch commands based on snapshot state.</summary>
  /// <remarks>
  /// Use for systems that produce messages without modifying state.
  /// The snapshot passes through unchanged.
  /// </remarks>
  /// <example>
  /// <code>
  /// |&gt; System.dispatch (fun snap -&gt;
  ///     if snap.Health &lt;= 0f then Cmd.ofMsg PlayerDied else Cmd.none)
  /// </code>
  /// </example>
  let inline dispatch
    ([<InlineIfLambda>] dispatcher: 'Snapshot -> Cmd<'Msg>)
    struct (snap: 'Snapshot, cmds: Cmd<'Msg>)
    : struct ('Snapshot * Cmd<'Msg>) =
    snap, combine cmds (dispatcher snap)

  /// <summary>Dispatch commands with input selection from snapshot.</summary>
  /// <remarks>
  /// <para>Use for encapsulated/autonomous subsystems that:</para>
  /// <list type="bullet">
  /// <item>Manage their own internal state (via closure)</item>
  /// <item>Receive optional input derived from the snapshot</item>
  /// <item>Dispatch commands to communicate with the parent</item>
  /// </list>
  /// <para>
  /// The selector extracts relevant data from the snapshot, allowing the dispatcher
  /// to remain decoupled from the parent's model structure.
  /// </para>
  /// </remarks>
  /// <example>
  /// <code>
  /// // Autonomous subsystem with internal state (closure)
  /// let healthTracker : HealthInput voption -&gt; ModelSnapshot -&gt; Cmd&lt;Msg&gt; =
  ///     let mutable hp = 100f
  ///     fun input snap -&gt;
  ///         input |&gt; ValueOption.iter (function
  ///             | TakeDamage amt -&gt; hp &lt;- hp - amt
  ///             | Heal amt -&gt; hp &lt;- min 100f (hp + amt))
  ///         if hp &lt;= 0f then Cmd.ofMsg PlayerDied else Cmd.none
  ///
  /// // Selector bridges parent snapshot to subsystem input
  /// let selectHealthInput snap =
  ///     if snap.PlayerWasHit then ValueSome (TakeDamage snap.DamageAmount)
  ///     else ValueNone
  ///
  /// // Usage in pipeline
  /// |&gt; System.dispatchWith selectHealthInput healthTracker
  /// </code>
  /// </example>
  let inline dispatchWith
    ([<InlineIfLambda>] selectInput: 'Snapshot -> 'Input voption)
    ([<InlineIfLambda>] dispatcher: 'Input voption -> 'Snapshot -> Cmd<'Msg>)
    struct (snap: 'Snapshot, cmds: Cmd<'Msg>)
    : struct ('Snapshot * Cmd<'Msg>) =
    let input = selectInput snap
    snap, combine cmds (dispatcher input snap)

  /// <summary>Finish pipeline: convert snapshot back to model and return the accumulated command.</summary>
  /// <remarks>Returns a tuple ready for the Elmish update function.</remarks>
  let inline finish
    ([<InlineIfLambda>] fromSnapshot: 'Snapshot -> 'Model)
    struct (snap: 'Snapshot, cmds: Cmd<'Msg>)
    : struct ('Model * Cmd<'Msg>) =
    fromSnapshot snap, cmds
