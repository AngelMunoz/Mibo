namespace Mibo.Elmish

open Mibo.Input

// ─────────────────────────────────────────────────────────────────────────────
// Raylib-specific Program builder extensions.
//
// The backend-neutral Program builder (mkProgram, withConfig, withRenderer,
// withTick, withFixedStep, withDispatchMode, withSubscription, withAssets,
// withAssetsBasePath, withInput, withServiceRegistration) lives in Mibo.Core.
//
// This module holds the only backend-coupled builder: withInputMapper, which
// instantiates the raylib IInputMapper implementation. It registers the service
// via a ServiceRegistration callback that the runtime host runs before Init,
// so the Core Program type never references a backend factory.
//
// It lives in its own module (not as `Program.withInputMapper`) because the
// factory is raylib-specific: each backend exposes its own withInputMapper that
// supplies its native IInputMapper implementation.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Raylib-specific <see cref="T:Mibo.Elmish.Program"/> builder extensions.</summary>
module RaylibProgram =

  /// <summary>
  /// One entry of a sound bank: a game-vocabulary key bound to the file that
  /// plays under it. The whole bank loads before user <c>init</c> runs.
  /// </summary>
  /// <remarks>
  /// Paths resolve against the program's asset base path
  /// (<see cref="M:Mibo.Elmish.Program.withAssetsBasePath"/>) — the same rule
  /// as the asset service. Formats: whatever the raylib build decodes (WAV,
  /// OGG, MP3, FLAC, QOA).
  /// </remarks>
  type BankEntry =
    /// <summary>A sound effect: plays with <c>Audio.play</c>, overlaps itself through an 8-slot pool.</summary>
    | Sound of key: string * path: string
    /// <summary>A music track for the single music channel.</summary>
    | Music of key: string * path: string

  /// <summary>
  /// Configures the game's sound bank: every entry loads before user
  /// <c>init</c> runs, so <c>Audio.play</c>/<c>Audio.playMusic</c> can start
  /// sounds from the very first message.
  /// </summary>
  /// <remarks>
  /// <para>
  /// The bank is an ordinary F# value: a list literal for small games, or a
  /// generated value for large ones (a naming convention, a manifest, a
  /// directory scan). List order is load order. One call configures the whole
  /// bank; there are no per-asset builder calls.
  /// </para>
  /// <para>
  /// Each entry becomes a registration callback that runs where every other
  /// service registration runs — after the host registers its services,
  /// before <c>Init</c>. Keys are game vocabulary ("jump", "overworld"); a
  /// key registered twice keeps the first entry.
  /// </para>
  /// </remarks>
  /// <example>
  /// <code>
  /// let program =
  ///   mkProgram init update
  ///   |&gt; RaylibProgram.withBank
  ///     [ Sound("jump", "assets/jump.wav")
  ///       Music("overworld", "assets/overworld.ogg") ]
  /// </code>
  /// </example>
  let withBank
    (bank: BankEntry list)
    (program: Program<'Model, 'Msg>)
    : Program<'Model, 'Msg> =
    program
    |> Program.withServiceRegistration(fun ctx ->
      match GameContext.tryGetService<AudioService> ctx with
      | ValueSome audio ->
        for entry in bank do
          match entry with
          | Sound(key, path) -> audio.RegisterSound(key, path)
          | Music(key, path) -> audio.RegisterMusic(key, path)
      | ValueNone -> ())

  /// <summary>
  /// Configures the game to register an <see cref="T:Mibo.Input.IInputMapper`1"/> service
  /// backed by raylib's polling API.
  /// </summary>
  /// <remarks>
  /// <para>This registers <see cref="T:Mibo.Input.IInput"/> automatically (equivalent to <see cref="M:Mibo.Elmish.Program.withInput"/>).</para>
  /// <para>The mapper is registered as a service via a <see cref="F:Mibo.Elmish.Program.ServiceRegistrations"/>
  /// callback that the runtime host runs before <c>Init</c>, so the Core Program
  /// type does not reference the raylib factory directly.</para>
  /// <para>The service is registered, not driven: nothing polls
  /// <c>Update()</c> automatically — call it yourself each frame if you read
  /// <c>CurrentState</c>, or prefer the subscription path below, which the
  /// input deltas drive on their own.</para>
  /// <para>If you want to stay fully "Elmish" (no service access), consider using
  /// <see cref="M:Mibo.Input.InputMapper.subscribe"/> instead and handle a single message
  /// (adaptive programs: <see cref="M:Mibo.Input.InputMapper.subscribeAdaptive"/>).</para>
  /// </remarks>
  /// <example>
  /// <code>
  /// program |&gt; RaylibProgram.withInputMapper inputMap
  /// </code>
  /// </example>
  let withInputMapper<'Model, 'Msg, 'Action when 'Action: comparison>
    (initialMap: InputMap<'Action>)
    (program: Program<'Model, 'Msg>)
    : Program<'Model, 'Msg> =
    let program = program |> Program.withInput

    // Register the raylib-backed IInputMapper service before Init runs.
    // The host invokes ServiceRegistrations after IInput is available.
    let withRegistration =
      program
      |> Program.withServiceRegistration(fun ctx ->
        let mapper = InputMapper.createService initialMap
        GameContext.register<IInputMapper<'Action>> mapper ctx)

    {
      withRegistration with
          HasInputMapper = true
    }
