// ─────────────────────────────────────────────────────────────
// Speedscope (evented) trace analyzer — read-only analysis.
// Reports real inclusive/exclusive times per frame, plus the
// callers of the allocation/string leaves.
// Usage: dotnet fsi tools/analyze-trace.fsx <trace.speedscope.json>
// ─────────────────────────────────────────────────────────────
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let private shorten(n: string) =
  if n.Length <= 110 then n else n.Substring(0, 107) + "..."

/// The owning module/library of a frame name (before the first !).
let private moduleOf(n: string) =
  match n.IndexOf '!' with
  | -1 ->
    match n.IndexOf '(' with
    | -1 -> n
    | i -> n.Substring(0, i)
  | i -> n.Substring(0, i)

let path =
  match fsi.CommandLineArgs with
  | [| _; p |] -> p
  | _ ->
    failwith "usage: dotnet fsi tools/analyze-trace.fsx <trace.speedscope.json>"

let doc = JsonDocument.Parse(File.ReadAllText path)
let root = doc.RootElement

let frames =
  root.GetProperty("shared").GetProperty("frames").EnumerateArray()
  |> Seq.mapi(fun i f -> (i, f.GetProperty("name").GetString()))
  |> Seq.toArray

let frameName(i: int) = frames[i] |> snd

let profiles = root.GetProperty("profiles").EnumerateArray() |> Seq.toArray

for profile in profiles do
  let name = profile.GetProperty("name").GetString()

  let events =
    profile.GetProperty("events").EnumerateArray()
    |> Seq.map(fun e ->
      struct (e.GetProperty("type").GetString(),
              e.GetProperty("frame").GetInt32(),
              e.GetProperty("at").GetDouble()))
    |> Seq.toArray

  if events.Length = 0 then
    printfn "profile %s: no events" name
  else
    let firstAt = let struct (_, _, at) = events[0] in at
    let lastAt = let struct (_, _, at) = events[events.Length - 1] in at
    let duration = lastAt - firstAt

    // Inclusive = time a frame was on the stack. Exclusive = interval-based:
    // between two consecutive events, the stack-top frame owns the interval.
    let inclusive = Dictionary<int, float>()
    let exclusive = Dictionary<int, float>()
    // Caller attribution for leaves: when leaf L opens, the current stack
    // top is its caller; tally leaf OPEN OCCURRENCES per caller (each
    // occurrence = one sample where the leaf was on the stack).
    let allocCallers = Dictionary<string, int>()
    let stringCallers = Dictionary<string, int>()

    let isAllocLeaf(n: string) =
      n.Contains "ArrayModule.ZeroCreate" || n.Contains "ArrayModule.Create"

    let isStringLeaf(n: string) =
      n.Contains "String.Concat" || n.Contains "StringPrintfEnv"

    let stack = ResizeArray<struct (int * float)>() // (frame, openAt)
    let mutable prevAt = firstAt

    let topFrame() =
      if stack.Count > 0 then
        let struct (f, _) = stack[stack.Count - 1]
        Some f
      else
        None

    for struct (t, f, at) in events do
      // The interval (prevAt → at) belongs to the current stack top.
      match topFrame() with
      | Some top ->
        let span = at - prevAt

        exclusive[top] <-
          (if exclusive.ContainsKey top then exclusive[top] else 0.0) + span
      | None -> ()

      if t = "O" then
        let caller = topFrame()
        stack.Add(struct (f, at))

        if isAllocLeaf(frameName f) then
          match caller with
          | Some cf ->
            let key = shorten(frameName cf)

            allocCallers[key] <-
              (if allocCallers.ContainsKey key then
                 allocCallers[key]
               else
                 0)
              + 1
          | None -> ()
        elif isStringLeaf(frameName f) then
          match caller with
          | Some cf ->
            let key = shorten(frameName cf)

            stringCallers[key] <-
              (if stringCallers.ContainsKey key then
                 stringCallers[key]
               else
                 0)
              + 1
          | None -> ()
      else if // "C"
        stack.Count > 0
      then
        let struct (cf, openAt) = stack[stack.Count - 1]
        stack.RemoveAt(stack.Count - 1)
        let span = at - openAt

        inclusive[cf] <-
          (if inclusive.ContainsKey cf then inclusive[cf] else 0.0) + span

      prevAt <- at

    printfn ""

    printfn
      "════ profile: %s ════  duration %.1f ms (%.2f s)"
      name
      duration
      (duration / 1000.0)

    printfn ""
    printfn "── library share of wall time (exclusive intervals, incl. idle) ──"

    let byLibrary =
      exclusive
      |> Seq.map(fun kv -> (moduleOf(frameName kv.Key), kv.Value))
      |> Seq.groupBy fst
      |> Seq.map(fun (lib, xs) -> (lib, xs |> Seq.sumBy snd))
      |> Seq.sortByDescending snd

    let accounted = byLibrary |> Seq.sumBy snd
    let idle = max 0.0 (duration - accounted)

    for lib, span in byLibrary do
      printfn "  %6.1f%%  %s" (100.0 * span / duration) lib

    printfn
      "  %6.1f%%  (idle / native wait / unaccounted)"
      (100.0 * idle / duration)

    let adaptiveSlopSpan =
      exclusive
      |> Seq.filter(fun kv ->
        (moduleOf(frameName kv.Key)) = "AdaptiveSlop.Core")
      |> Seq.sumBy(fun kv -> kv.Value)

    printfn ""

    printfn
      "  → AdaptiveSlop total: %.1f%% of wall time (%.2f s of %.2f s)"
      (100.0 * adaptiveSlopSpan / duration)
      (adaptiveSlopSpan / 1000.0)
      (duration / 1000.0)

    printfn ""
    printfn "── sample census (each sample ≈ 1 ms of BUSY time) ──"
    // Reconstruct the stack at each distinct timestamp: count samples whose
    // stack contains an AdaptiveSlop frame (the chain), and samples with no
    // managed stack (idle/native wait — invisible to the profiler).
    let mutable sampleCount = 0
    let mutable adaptiveSlopSamples = 0
    let mutable idleSamples = 0
    let mutable lastSampleAt = -1.0
    let stack = ResizeArray<int>()
    let isAdaptive(n: string) = n.Contains "AdaptiveSlop.Core"

    let census() =
      sampleCount <- sampleCount + 1

      if stack.Count > 0 then
        if stack |> Seq.exists(fun f -> isAdaptive(frameName f)) then
          adaptiveSlopSamples <- adaptiveSlopSamples + 1
      else
        idleSamples <- idleSamples + 1

    for struct (t, f, at) in events do
      if at <> lastSampleAt && lastSampleAt >= 0.0 then
        census()

      if t = "O" then
        stack.Add f
      else if stack.Count > 0 then
        stack.RemoveAt(stack.Count - 1)

      lastSampleAt <- at

    census()

    printfn
      "  samples total        : %d (%.1f s of busy time at 1 kHz)"
      sampleCount
      (float sampleCount / 1000.0)

    printfn
      "  samples in AdaptiveSlop: %d (%.1f%% of busy time)"
      adaptiveSlopSamples
      (100.0 * float adaptiveSlopSamples / float sampleCount)

    printfn "  samples idle/native   : %d" idleSamples
    printfn ""

    printfn
      "  busy share of wall: %.1f%%  →  AdaptiveSlop wall share ≈ %.1f%%"
      (100.0 * float sampleCount / float(duration / 1.0))
      (100.0 * float adaptiveSlopSamples / duration)

    printfn ""
    printfn "── top 14 by INCLUSIVE time ──"

    inclusive
    |> Seq.sortByDescending(fun kv -> kv.Value)
    |> Seq.truncate 14
    |> Seq.iter(fun kv ->
      printfn
        "  %6.1f%%  %s"
        (100.0 * kv.Value / duration)
        (shorten(frameName kv.Key)))

    printfn ""
    printfn "── top 12 by EXCLUSIVE time (leaf-level cost) ──"

    exclusive
    |> Seq.sortByDescending(fun kv -> kv.Value)
    |> Seq.truncate 12
    |> Seq.iter(fun kv ->
      printfn
        "  %6.1f%%  %s"
        (100.0 * kv.Value / duration)
        (shorten(frameName kv.Key)))

    printfn ""
    printfn "── who allocates arrays (occurrences of zeroCreate/create) ──"
    let allocTotal = allocCallers.Values |> Seq.sum

    allocCallers
    |> Seq.sortByDescending(fun kv -> kv.Value)
    |> Seq.truncate 8
    |> Seq.iter(fun kv ->
      printfn
        "  %6.1f%%  (%d samples)  %s"
        (100.0 * float kv.Value / float allocTotal)
        kv.Value
        kv.Key)

    printfn ""
    printfn "── who builds strings (occurrences) ──"
    let stringTotal = stringCallers.Values |> Seq.sum

    stringCallers
    |> Seq.sortByDescending(fun kv -> kv.Value)
    |> Seq.truncate 6
    |> Seq.iter(fun kv ->
      printfn
        "  %6.1f%%  (%d samples)  %s"
        (100.0 * float kv.Value / float stringTotal)
        kv.Value
        kv.Key)
