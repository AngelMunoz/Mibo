// Structure probe for a speedscope evented trace — decides whether the
// per-timestamp census is valid (1 sample ≈ 1 ms busy) or whether the
// trace uses CPU_TIME run markers with multi-ms gaps.
//   dotnet fsi tools/probe-structure.fsx <trace.speedscope.json>
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let path =
  match fsi.CommandLineArgs with
  | [| _; p |] -> p
  | _ ->
    failwith
      "usage: dotnet fsi tools/probe-structure.fsx <trace.speedscope.json>"

let doc = JsonDocument.Parse(File.ReadAllText path)
let root = doc.RootElement

let frames =
  root.GetProperty("shared").GetProperty("frames").EnumerateArray()
  |> Seq.mapi(fun i f -> (i, f.GetProperty("name").GetString()))
  |> Seq.toArray

let frameName(i: int) = frames[i] |> snd

for prof in root.GetProperty("profiles").EnumerateArray() do
  let pname = prof.GetProperty("name").GetString()

  let events =
    prof.GetProperty("events").EnumerateArray()
    |> Seq.map(fun e ->
      struct (e.GetProperty("type").GetString(),
              e.GetProperty("frame").GetInt32(),
              e.GetProperty("at").GetDouble()))
    |> Seq.toArray

  printfn "════ profile: %s  events: %d ════" pname events.Length

  if events.Length > 0 then
    let ts =
      events
      |> Seq.map(fun struct (_, _, at) -> at)
      |> Seq.distinct
      |> Seq.sort
      |> Seq.toArray

    let duration = ts[ts.Length - 1] - ts[0]
    let gaps = ts |> Array.pairwise |> Array.map(fun (a, b) -> b - a)

    let hist = Dictionary<int, int>()

    for g in gaps do
      let k =
        if g < 2.0 then 1
        elif g < 5.0 then 2
        elif g < 12.0 then 5
        elif g < 20.0 then 12
        elif g < 60.0 then 20
        elif g < 120.0 then 60
        else 120

      hist[k] <- (if hist.ContainsKey k then hist[k] else 0) + 1

    printfn "  distinct timestamps: %d  wall: %.1f ms" ts.Length duration

    printfn
      "  gap histogram [<2,<5,<12,<20,<60,<120,≥120 ms]: %s"
      (hist
       |> Seq.sortBy _.Key
       |> Seq.map(fun kv -> sprintf "%d:%d" kv.Key kv.Value)
       |> String.concat " ")

    let cpuOpens =
      events
      |> Seq.filter(fun struct (t, f, _) -> t = "O" && frameName f = "CPU_TIME")
      |> Seq.length

    let cpuSpans =
      // reconstruct: each CPU_TIME open→close pair is one run
      let mutable openAt = -1.0
      let spans = ResizeArray<float>()

      for struct (t, f, at) in events do
        if frameName f = "CPU_TIME" then
          if t = "O" then
            openAt <- at
          elif openAt >= 0.0 then
            spans.Add(at - openAt)
            openAt <- -1.0

      spans

    printfn
      "  CPU_TIME opens: %d  spans: %d  span-avg: %.2f ms  span-max: %.1f ms"
      cpuOpens
      cpuSpans.Count
      (if cpuSpans.Count > 0 then
         (cpuSpans |> Seq.average)
       else
         0.0)
      (if cpuSpans.Count > 0 then (cpuSpans |> Seq.max) else 0.0)

    // samples where the stack contains a managed frame vs CPU_TIME-only stacks
    // (reconstruct per distinct timestamp)
    let mutable stack = ResizeArray<int>()
    let mutable lastAt = -1.0
    let mutable cpuTimeTopOnly = 0
    let mutable managedTop = 0

    let census() =
      let managed = stack |> Seq.exists(fun f -> not(frameName f = "CPU_TIME"))

      if managed then
        managedTop <- managedTop + 1
      else
        cpuTimeTopOnly <- cpuTimeTopOnly + 1

    for struct (t, f, at) in events do
      if at <> lastAt && lastAt >= 0.0 then
        census()

      if t = "O" then
        stack.Add f
      elif stack.Count > 0 then
        stack.RemoveAt(stack.Count - 1)

      lastAt <- at

    census()

    printfn
      "  samples w/ managed frame: %d  CPU_TIME-only (idle/native): %d"
      managedTop
      cpuTimeTopOnly

    // GC frames presence
    let gcFrames =
      frames
      |> Seq.filter(fun (_, n) ->
        n.Contains "System.GC"
        || n.Contains "PollGC"
        || n.Contains "WriteBarrier"
        || n.Contains "GCSettings"
        || n.Contains "Garbage")
      |> Seq.toList

    printfn
      "  GC-related frames: %d %A"
      gcFrames.Length
      (gcFrames |> List.truncate 3 |> List.map snd)
