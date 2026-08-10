// Per-node census over a speedscope evented trace — same sample semantics
// as analyze-trace.fsx (one sample per distinct timestamp; each ≈ 1 ms busy).
// For each query string: inclusive samples (node anywhere on the stack) and
// the children under that node (direct child attribution).
// Usage: dotnet fsi tools/probe-nodes.fsx <trace.speedscope.json> <query> [<query> ...]
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let path =
  if fsi.CommandLineArgs.Length < 3 then
    failwith
      "usage: dotnet fsi tools/probe-nodes.fsx <trace.speedscope.json> <query> ..."
  else
    fsi.CommandLineArgs[1]

let queries = fsi.CommandLineArgs |> Array.skip 2 |> Array.toList

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

let mutable stack: ResizeArray<int> = ResizeArray()
let mutable samples: ResizeArray<int[]> = ResizeArray()
let mutable lastAt = -1.0

let snapshot() = samples.Add(stack.ToArray())

for struct (t, f, at) in events do
  if at <> lastAt && lastAt >= 0.0 then
    snapshot()

  if t = "O" then
    stack.Add f
  elif stack.Count > 0 then
    stack.RemoveAt(stack.Count - 1)

  lastAt <- at

snapshot()

let total = samples.Count

printfn
  "═══ %s ═══  %d samples (each ≈ 1 ms busy)"
  (Path.GetFileName path)
  total

for q in queries do
  printfn ""
  printfn "════ query: %s ════" q

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

            children[c] <-
              (if children.ContainsKey c then children[c] else 0) + 1

    printfn
      "  %5.1f%%  (%5d)  %s"
      (100.0 * float inclusive / float total)
      inclusive
      name

    for KeyValue(c, n) in
      children |> Seq.sortByDescending _.Value |> Seq.truncate 10 do
      printfn
        "        └─ %5.1f%%  (%5d)  %s"
        (100.0 * float n / float total)
        n
        c
