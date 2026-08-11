// Per-minute census + gap analysis for the main game thread.
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let path = fsi.CommandLineArgs[1]
let doc = JsonDocument.Parse(File.ReadAllText path)
let root = doc.RootElement

let frames =
  root.GetProperty("shared").GetProperty("frames").EnumerateArray()
  |> Seq.mapi(fun i f -> (i, f.GetProperty("name").GetString()))
  |> Seq.toArray

let frameName(i: int) = frames[i] |> snd

let events =
  root.GetProperty("profiles").EnumerateArray()
  |> Seq.maxBy(fun p ->
    p.GetProperty("events").EnumerateArray()
    |> Seq.filter(fun e -> e.GetProperty("type").GetString() = "O")
    |> Seq.length)
  |> fun p -> p.GetProperty("events").EnumerateArray()
  |> Seq.map(fun e ->
    struct (e.GetProperty("type").GetString(),
            e.GetProperty("frame").GetInt32(),
            e.GetProperty("at").GetDouble()))
  |> Seq.toArray

let isAdaptive(n: string) = n.Contains "AdaptiveSlop"

// per-minute: samples, adaptive samples, and the ≥120 ms gap count
let minutes = ResizeArray<struct (int * int * int * int * int)>() // samples, aslop, bigGaps, totalGaps
let stack = ResizeArray<int>()
let mutable lastAt = -1.0
let mutable curMin = -1
let mutable sMin = 0
let mutable aMin = 0
let mutable gapsMin = 0
let mutable bigGapsMin = 0

for struct (t, f, at) in events do
  let m = int(at / 60000.0)

  if m <> curMin then
    if curMin >= 0 then
      minutes.Add(struct (curMin, sMin, aMin, gapsMin, bigGapsMin))

    curMin <- m
    sMin <- 0
    aMin <- 0
    gapsMin <- 0
    bigGapsMin <- 0

  if at <> lastAt && lastAt >= 0.0 then
    sMin <- sMin + 1

    if stack |> Seq.exists(fun fi -> isAdaptive(frameName fi)) then
      aMin <- aMin + 1

    let gap = at - lastAt

    if gap >= 120.0 then
      bigGapsMin <- bigGapsMin + 1

    gapsMin <- gapsMin + 1

  match t with
  | "O" -> stack.Add f
  | _ ->
    if stack.Count > 0 then
      stack.RemoveAt(stack.Count - 1)

  lastAt <- at

minutes.Add(struct (curMin, sMin, aMin, gapsMin, bigGapsMin))

printfn "min  samples  aslop  aslop%%  gaps  bigGaps(≥120ms)"
let mutable totS = 0
let mutable totA = 0
let mutable totB = 0

for struct (m, s, a, g, b) in minutes do
  totS <- totS + s
  totA <- totA + a
  totB <- totB + b
  printfn "%3d  %6d  %6d  %5.1f  %5d  %4d" m s a (100.0 * float a / float s) g b

printfn "── totals: samples %d  aslop %d  bigGaps %d" totS totA totB

// where are the big gaps: distribution over the session
printfn ""
printfn "── gap histogram (all gaps) ──"
let bins = [| 2.0; 5.0; 12.0; 20.0; 40.0; 60.0; 120.0; 250.0; 500.0; 1000.0 |]
let counts = Array.zeroCreate(bins.Length + 1)
let mutable prev = -1.0

for struct (_, _, at) in events do
  if prev >= 0.0 then
    let g = at - prev
    let mutable i = 0

    while i < bins.Length && g >= bins[i] do
      i <- i + 1

    counts[i] <- counts[i] + 1

  prev <- at

let mutable acc = 0.0
printfn "  <2ms   : %d" counts[0]

for i in 0 .. bins.Length - 1 do
  printfn
    "  %4.0f..%4.0f: %d"
    bins[i]
    (if i + 1 < bins.Length then bins[i + 1] else 1.0e9)
    counts[i + 1]
