// One-off probe: sink-dispatch growth + lookup-node lifecycle over time.
//   dotnet fsi tools/probe-sinks.fsx <trace.speedscope.json>
// Counts, per 30 s bucket, the samples whose stack contains pushMapDelta /
// Towers.tick / OnDeltas, and tallies open occurrences of MapLookupNode
// members (ctor / Register / GetValue) and AddOrUpdate across the session.
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let path =
  match fsi.CommandLineArgs with
  | [| _; p |] -> p
  | _ -> failwith "usage: dotnet fsi tools/probe-sinks.fsx <trace.speedscope.json>"

let doc = JsonDocument.Parse(File.ReadAllText path)
let root = doc.RootElement

let frames =
  root.GetProperty("shared").GetProperty("frames").EnumerateArray()
  |> Seq.mapi(fun i f -> (i, f.GetProperty("name").GetString()))
  |> Seq.toArray

let frameName(i: int) = frames[i] |> snd

let events =
  root.GetProperty("profiles").EnumerateArray()
  |> Seq.collect(fun p ->
    p.GetProperty("events").EnumerateArray()
    |> Seq.map(fun e ->
      struct (e.GetProperty("type").GetString(),
              e.GetProperty("frame").GetInt32(),
              e.GetProperty("at").GetDouble())))
  |> Seq.sortBy(fun struct (_, _, at) -> at)
  |> Seq.toArray

// Per-frame-name open-occurrence tally (each "O" = one call on a stack).
let opens = Dictionary<string, int>()
let stack = ResizeArray<int>()
let bucketSize = 30000.0 // 30 s
let struct (_, _, lastAt0) = events[events.Length - 1]
let maxBucket = int (lastAt0 / bucketSize) + 1

// (bucket, inPush, inTowersTick, inOnDeltas) sample census
let buckets = Array.init maxBucket (fun _ -> struct (0, 0, 0, 0))
let mutable lastAt = -1.0
let mutable lastBucket = -1

let snapshot() =
  let b = int (lastAt / bucketSize)
  if b < maxBucket then
    let mutable struct (s, p, t, o) = buckets[b]
    s <- s + 1
    let mutable inPush = false
    let mutable inTick = false
    let mutable inDelta = false
    for f in stack do
      let n = frameName f
      if n.Contains "pushMapDelta" then inPush <- true
      if n.Contains "Towers.tick" then inTick <- true
      if n.Contains "OnDeltas" then inDelta <- true
    if inPush then p <- p + 1
    if inTick then t <- t + 1
    if inDelta then o <- o + 1
    buckets[b] <- struct (s, p, t, o)

for struct (t, f, at) in events do
  if at <> lastAt && lastAt >= 0.0 then snapshot()
  if t = "O" then
    let n = frameName f
    if n.Contains "MapLookupNode" || n.Contains "ChangeableMap" then
      opens[n] <- (if opens.ContainsKey n then opens[n] else 0) + 1
    stack.Add f
  elif stack.Count > 0 then
    stack.RemoveAt(stack.Count - 1)
  lastAt <- at

snapshot()

printfn "═══ opens per frame (whole session) ═══"
opens
|> Seq.sortByDescending(fun kv -> kv.Value)
|> Seq.truncate 20
|> Seq.iter(fun kv -> printfn "  %7d  %s" kv.Value kv.Key)

printfn ""
printfn "═══ per-30s buckets: total / inPush / Towers.tick / OnDeltas samples ═══"
for i in 0 .. maxBucket - 1 do
  let struct (s, p, t, o) = buckets[i]
  if s > 0 then
    printfn "  t=%6.0f–%6.0f s  total=%5d  push=%5d (%4.1f%%)  tick=%5d  onDeltas=%5d" (float i * bucketSize / 1000.0) (float (i + 1) * bucketSize / 1000.0) s p (100.0 * float p / float s) t o
