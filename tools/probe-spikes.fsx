// Per-minute census + spike locator for the Defli adaptive trace.
//   dotnet fsi tools/probe-spikes.fsx <trace.speedscope.json>
// Per-minute: busy samples, AdaptiveSlop samples, dominant frame.
// Spikes: the longest CPU_TIME spans (start, end, duration, thread),
// with the stack at the sample nearest the span's start.
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let path =
  match fsi.CommandLineArgs with
  | [| _; p |] -> p
  | _ ->
    failwith "usage: dotnet fsi tools/probe-spikes.fsx <trace.speedscope.json>"

let doc = JsonDocument.Parse(File.ReadAllText path)
let root = doc.RootElement

let frames =
  root.GetProperty("shared").GetProperty("frames").EnumerateArray()
  |> Seq.mapi(fun i f -> (i, f.GetProperty("name").GetString()))
  |> Seq.toArray

let frameName(i: int) = frames[i] |> snd

let profiles =
  root.GetProperty("profiles").EnumerateArray()
  |> Seq.map(fun p ->
    let name = p.GetProperty("name").GetString()

    let events =
      p.GetProperty("events").EnumerateArray()
      |> Seq.map(fun e ->
        struct (e.GetProperty("type").GetString(),
                e.GetProperty("frame").GetInt32(),
                e.GetProperty("at").GetDouble()))
      |> Seq.toArray

    struct (name, events))
  |> Seq.toArray

// ── 1. Per-minute census over the busiest profile ─────────────
let struct (mainName, mainEvents) =
  profiles
  |> Array.maxBy(fun struct (_, evs) ->
    evs |> Seq.filter(fun struct (t, _, _) -> t = "O") |> Seq.length)

printfn "════ main profile: %s ════" mainName

// per-minute: samples + AdaptiveSlop + CPU_TIME span count
let minutes = Dictionary<int, struct (int * int * int)>()
let mutable minStart = Double.MaxValue
let mutable maxEnd = Double.MinValue

let stack = ResizeArray<int>()
let mutable lastAt = -1.0
let mutable sampleMin = 0
let mutable aslopMin = 0
let mutable spansMin = 0

let mutable curMin = -1

let bump() =
  if curMin >= 0 then
    let mutable v = Unchecked.defaultof<struct (int * int * int)>

    if minutes.TryGetValue(curMin, &v) then
      let struct (s, a, sp) = v
      minutes[curMin] <- struct (s + sampleMin, a + aslopMin, sp + spansMin)
    else
      minutes[curMin] <- struct (sampleMin, aslopMin, spansMin)

for struct (t, f, at) in mainEvents do
  let m = int(at / 60000.0)

  if m <> curMin then
    bump()
    curMin <- m
    sampleMin <- 0
    aslopMin <- 0
    spansMin <- 0

  match t with
  | "O" -> stack.Add f
  | "C" ->
    if stack.Count > 0 then
      stack.RemoveAt(stack.Count - 1)
  | "M" -> stack[stack.Count - 1] <- f
  | "X" ->
    if at <> lastAt then
      lastAt <- at
      sampleMin <- sampleMin + 1
      let mutable inAslop = false

      for fi in stack do
        if (frameName fi).Contains "AdaptiveSlop" then
          inAslop <- true

      if inAslop then
        aslopMin <- aslopMin + 1
  | "B" ->
    if at <> lastAt then
      lastAt <- at
      spansMin <- spansMin + 1
  | _ -> ()

bump()

printfn "── per-minute census (samples ≈ ms busy) ──"
printfn "min  samples  adaptive  aslop%%  spans"

minutes.Keys
|> Seq.sort
|> Seq.iter(fun m ->
  let struct (s, a, sp) = minutes[m]
  let pct = if s > 0 then 100.0 * float a / float s else 0.0
  printfn "%3d  %6d  %6d  %5.1f  %4d" m s a pct sp)

// ── 2. Spike locator: the longest CPU_TIME spans ──────────────
// Reconstruct spans: an "O" of CPU_TIME opens a span, its "C" closes it.
let spans = ResizeArray<struct (float * float * float)>() // start, end, dur
let mutable spanStart = -1.0

for struct (t, f, at) in mainEvents do
  if frameName f = "CPU_TIME" then
    match t with
    | "O" -> spanStart <- at
    | "C" when spanStart >= 0.0 ->
      spans.Add(struct (spanStart, at, at - spanStart))
      spanStart <- -1.0
    | _ -> ()

printfn ""
printfn "── longest CPU_TIME spans (the slowliness candidates) ──"
printfn "start(s)   end(s)     dur(ms)"

spans
|> Seq.sortByDescending(fun struct (_, _, d) -> d)
|> Seq.truncate 15
|> Seq.iter(fun struct (s, e, d) -> printfn "%8.1f  %8.1f  %8.1f" s e d)

// The stacks at the longest spans' starts
printfn ""
printfn "── stack at each of the 5 longest spans ──"

let stacksByAt = Dictionary<float, string[]>()

let stack2 = ResizeArray<int>()
let mutable lastAt2 = -1.0

for struct (t, f, at) in mainEvents do
  if at <> lastAt2 && lastAt2 >= 0.0 then
    stacksByAt[lastAt2] <- stack2 |> Seq.map frameName |> Seq.toArray

  match t with
  | "O" -> stack2.Add f
  | "C" ->
    if stack2.Count > 0 then
      stack2.RemoveAt(stack2.Count - 1)
  | "M" -> stack2[stack2.Count - 1] <- f
  | _ -> ()

  lastAt2 <- at

if mainEvents.Length > 0 then
  let struct (_, _, lastAtV) = mainEvents[mainEvents.Length - 1]
  stacksByAt[lastAtV] <- stack2 |> Seq.map frameName |> Seq.toArray

spans
|> Seq.sortByDescending(fun struct (_, _, d) -> d)
|> Seq.truncate 5
|> Seq.iter(fun struct (s, _, d) ->
  // nearest sample timestamp at or after span start
  let keys =
    stacksByAt.Keys
    |> Seq.filter(fun k -> k >= s - 2.0 && k <= s + 30.0)
    |> Seq.sort
    |> Seq.toArray

  let key = if keys.Length > 0 then keys[0] else s
  printfn ""
  printfn "── span %.1f ms at t=%.1fs ──" d s

  match stacksByAt.TryGetValue key with
  | true, stk ->
    for i in stk.Length - 1 .. -1 .. 0 do
      printfn
        "  %s"
        (if stk[i].Length > 130 then
           stk[i].Substring(0, 127) + "..."
         else
           stk[i])
  | _ -> printfn "  (no stack captured)")
