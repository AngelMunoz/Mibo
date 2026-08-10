// Microscope queries over a speedscope (evented) trace — read-only.
//   dotnet fsi tools/analyze-subtree.fsx <trace.speedscope.json>
// Sample-based, same census semantics as analyze-trace.fsx: one
// sample per distinct event timestamp; each sample ≈ 1 ms of BUSY
// time (idle = no events on the stack). Prints the per-depth
// panorama and subtree + child attribution of the top consumers.
// ─────────────────────────────────────────────────────────────
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let path =
  match fsi.CommandLineArgs with
  | [| _; p |] -> p
  | _ -> failwith "usage: dotnet fsi tools/analyze-subtree.fsx <trace.speedscope.json>"

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

// Reconstruct the stack at each distinct timestamp → one sample.
let mutable stack: ResizeArray<int> = ResizeArray()
let mutable samples: ResizeArray<int[]> = ResizeArray()
let mutable lastAt = -1.0

let snapshot() = samples.Add(stack.ToArray())

for struct (t, f, at) in events do
  if at <> lastAt && lastAt >= 0.0 then
    snapshot()

  if t = "O" then stack.Add f
  elif stack.Count > 0 then stack.RemoveAt(stack.Count - 1)

  lastAt <- at

snapshot()

let total = samples.Count

printfn "═══ panorama ═══  %d samples (each ≈ 1 ms busy)" total

// Per-depth top inclusive
for depth in 0 .. 6 do
  let counts =
    samples
    |> Seq.collect(fun st -> if st.Length > depth then [ (frameName st[depth]), 1 ] else [])
    |> Seq.groupBy fst
    |> Seq.map(fun (n, xs) -> n, xs |> Seq.length)
    |> Seq.sortByDescending snd
    |> Seq.truncate 14

  printfn ""
  printfn "── depth %d (inclusive at that stack depth) ──" depth

  for (n, c) in counts do
    printfn "  %5.1f%%  (%5d)  %s" (100.0 * float c / float total) c n

// Subtree + child attribution per query
let queries = [ "Towers.tick"; "Renderer2D"; "Application.view"; "AdaptiveSlop.Core"; "Enemies+Enemies" ]

for q in queries do
  printfn ""
  printfn "════ subtree: %s ════" q

  let names = HashSet<string>()

  for st in samples do
    for f in st do
      if (frameName f).Contains q then
        names.Add(frameName f) |> ignore

  for name in names |> Seq.sort do
    let mutable inclusive = 0
    let children = Dictionary<string, int>()

    for st in samples do
      for i in 0 .. st.Length - 1 do
        if frameName st[i] = name then
          inclusive <- inclusive + 1

          if i + 1 < st.Length then
            let c = frameName st[i + 1]
            children[c] <- (if children.ContainsKey c then children[c] else 0) + 1

    printfn "  %5.1f%%  (%5d)  %s" (100.0 * float inclusive / float total) inclusive name

    for KeyValue(c, n) in children |> Seq.sortByDescending _.Value |> Seq.truncate 8 do
      printfn "        └─ %5.1f%%  (%5d)  %s" (100.0 * float n / float total) n c
