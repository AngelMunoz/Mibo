namespace Mibo.Adaptive

open Mibo.Elmish

// ─────────────────────────────────────────────────────────────────────────────
// Raylib-specific AdaptiveProgram builder extensions.
//
// Mirrors RaylibProgram (the MVU lane's builder) for the adaptive lane: the
// only raylib-coupled builder, withBank, which binds a sound bank through a
// ServiceRegistration callback so the backend-neutral AdaptiveProgram type
// never references a backend type. The host runs the registration before the
// program's Init, which is the bank's required load slot.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Raylib-specific <see cref="T:Mibo.Adaptive.AdaptiveProgram`1"/> builder extensions.</summary>
module AdaptiveRaylibProgram =

  /// <summary>
  /// One entry of a sound bank: a game-vocabulary key bound to the file that
  /// plays under it. The whole bank loads before the program's <c>Init</c>
  /// runs.
  /// </summary>
  /// <remarks>
  /// Paths resolve against the program's asset base path
  /// (<see cref="M:Mibo.Adaptive.AdaptiveProgram.withAssetsBasePath"/>) — the
  /// same rule as the asset service. Formats: whatever the raylib build
  /// decodes (WAV, OGG, MP3, FLAC, QOA).
  /// </remarks>
  type BankEntry =
    /// <summary>A sound effect: plays with <c>Intents.play</c>, overlaps itself through an 8-slot pool.</summary>
    | Sound of key: string * path: string
    /// <summary>A music track for the single music channel.</summary>
    | Music of key: string * path: string

  /// <summary>
  /// Configures the game's sound bank: every entry loads before the program's
  /// <c>Init</c> runs, so <c>Intents.play</c>/<c>Intents.playMusic</c> can
  /// start sounds from the first step.
  /// </summary>
  /// <remarks>
  /// The bank is an ordinary F# value: a list literal for small games, or a
  /// generated value for large ones (a naming convention, a manifest, a
  /// directory scan). List order is load order; a key registered twice keeps
  /// the first entry. The adaptive counterpart of
  /// <see cref="M:Mibo.Elmish.RaylibProgram.withBank"/>: same entries, same
  /// load slot (service registrations run before <c>Init</c>).
  /// </remarks>
  /// <example>
  /// <code>
  /// let program =
  ///   AdaptiveProgram.mkProgram init update
  ///   |&gt; AdaptiveRaylibProgram.withBank
  ///     [ Sound("jump", "assets/jump.wav")
  ///       Music("overworld", "assets/overworld.ogg") ]
  ///
  /// AdaptiveRaylibGame(program).Run()
  /// </code>
  /// </example>
  let withBank
    (bank: BankEntry list)
    (program: AdaptiveProgram<'Frame>)
    : AdaptiveProgram<'Frame> =
    program
    |> AdaptiveProgram.withServiceRegistration(fun ctx ->
      match GameContext.tryGetService<AudioService> ctx with
      | ValueSome audio ->
        for entry in bank do
          match entry with
          | Sound(key, path) -> audio.RegisterSound(key, path)
          | Music(key, path) -> audio.RegisterMusic(key, path)
      | ValueNone -> ())
