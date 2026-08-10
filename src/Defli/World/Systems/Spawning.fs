module Defli.World.Systems.Spawning

open System
open System.Collections.Generic
open Defli.World

// ─────────────────────────────────────────────────────────────
// Spawning sub-system (Kimo's Systems/Spawning.fs analog) — owns
// the spawn queue, the weighted table picks, and its own RNG
// stream (seeded by the caller — never shared with other systems).
// Waves composes WHAT a wave contains; Spawning executes it.
//
// Deliberately no capacity/respawn invariant: Kimo's zone capacity
// + EntityDied→respawn serves ambient spawns; TD waves are finite
// batches. Death/arrival handling lives in Enemies (events out).
// ─────────────────────────────────────────────────────────────

[<Struct>]
type SpawnMsg = FillWave of wave: WaveDef

[<Struct>]
type SpawnEvent =
  | SpawnEnemy of def: EnemyDef
  | SpawnFailed of reason: string

type SpawningModel(rng: Random) =
  /// Pending spawns: (enemy def, remaining delay in seconds).
  member val Queue = ResizeArray<struct (EnemyDef * float32)>() with get, set
  member val Rng: Random = rng

module Spawning =

  let init(seed: int) = SpawningModel(Random seed)

  /// One weighted pick from the table (Kimo's algorithm; zero-weight
  /// entries never win).
  let private pickKey
    (rng: Random)
    (table: struct (EnemyDef * int)[])
    : EnemyDef voption =
    let mutable total = 0

    for struct (_, weight) in table do
      total <- total + max 0 weight

    if total <= 0 then
      ValueNone
    else
      let mutable roll = rng.Next total
      let mutable result = ValueNone
      let mutable i = 0

      while result.IsNone && i < table.Length do
        let struct (def, weight) = table[i]

        if roll < max 0 weight then
          result <- ValueSome def

        roll <- roll - max 0 weight
        i <- i + 1

      result

  /// Cold path: rebuild the queue from a wave — explicit ExtraSpawns
  /// at their fixed delays (the boss leads), then one weighted pick
  /// per spawn, spaced by the wave's interval. Each entry carries its
  /// own remaining delay, so queue order never affects timing.
  let update (msg: SpawnMsg) (model: SpawningModel) : SpawnEvent[] =
    match msg with
    | FillWave wave ->
      model.Queue.Clear()
      let queue = model.Queue

      for struct (def, delay) in wave.ExtraSpawns do
        queue.Add struct (def, delay)

      let mutable delay = wave.InitialDelay
      let mutable failed = false

      for _ in 1 .. wave.Count do
        match pickKey model.Rng wave.Table with
        | ValueSome def ->
          queue.Add struct (def, delay)
          delay <- delay + wave.Interval
        | ValueNone -> failed <- true


      (if failed then
         [| SpawnFailed "empty wave table" |]
       else
         Array.empty)

  /// Hot path: drain the queue — decrement delays, emit due spawns,
  /// swap-remove (order is not significant, Kimo's pattern).
  let tick (dt: float32) (model: SpawningModel) : SpawnEvent seq =
    let queue = model.Queue
    let mutable events: ResizeArray<SpawnEvent> = null
    let mutable i = queue.Count - 1

    while i >= 0 do
      let struct (def, remaining) = queue[i]
      let remaining = remaining - dt

      if remaining <= 0f then
        if isNull events then
          events <- ResizeArray()

        events.Add(SpawnEnemy def)
        queue[i] <- queue[queue.Count - 1]
        queue.RemoveAt(queue.Count - 1)
      else
        queue[i] <- struct (def, remaining)

      i <- i - 1

    (if isNull events then Array.empty else events)
