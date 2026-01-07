namespace Mibo.Elmish

/// Generic system pipeline for composing frame updates with type-enforced snapshot boundaries.
///
/// The type difference between 'Model and 'Snapshot enforces mutable/readonly phases:
/// - `pipeMutable` works on 'Model (pre-snapshot)
/// - `pipe` works on 'Snapshot (post-snapshot)
/// - `snapshot` transitions between them
module System =

  /// Start pipeline with mutable model
  let inline start (model: 'Model) : 'Model * Cmd<'Msg> list = (model, [])

  /// Pipe a mutable system (pre-snapshot phase).
  /// Use for systems that need to mutate model state (physics, particles).
  let inline pipeMutable
    (system: 'Model -> struct ('Model * Cmd<'Msg> list))
    (model: 'Model, cmds: Cmd<'Msg> list)
    : 'Model * Cmd<'Msg> list =
    let struct (newModel, newCmds) = system model
    (newModel, cmds @ newCmds)

  /// SNAPSHOT: Transition from mutable Model to readonly Snapshot.
  /// After this point, only readonly systems can be piped.
  let inline snapshot
    (toSnapshot: 'Model -> 'Snapshot)
    (model: 'Model, cmds: Cmd<'Msg> list)
    : 'Snapshot * Cmd<'Msg> list =
    (toSnapshot model, cmds)

  /// Pipe a readonly system (post-snapshot phase).
  /// Use for systems that read state but don't mutate it (rendering prep, AI decisions).
  let inline pipe
    (system: 'Snapshot -> struct ('Snapshot * Cmd<'Msg> list))
    (snap: 'Snapshot, cmds: Cmd<'Msg> list)
    : 'Snapshot * Cmd<'Msg> list =
    let struct (newSnap, newCmds) = system snap
    (newSnap, cmds @ newCmds)

  /// Finish pipeline: convert snapshot back to model, batch all commands.
  let inline finish
    (fromSnapshot: 'Snapshot -> 'Model)
    (snap: 'Snapshot, cmds: Cmd<'Msg> list)
    : struct ('Model * Cmd<'Msg>) =
    struct (fromSnapshot snap, Cmd.batch cmds)
