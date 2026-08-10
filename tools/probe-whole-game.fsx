// One-off: whole-game pie — inclusive sample share of every subsystem.
//   dotnet fsi tools/probe-whole-game.fsx <trace.speedscope.json>
// Same census as analyze-trace.fsx (one sample per distinct timestamp).
open System
open System.Collections.Generic
open System.IO
open System.Text.Json

let path =
  match fsi.CommandLineArgs with
  | [| _; p |] -> p
  | _ -> failwith "usage: dotnet fsi tools/probe-whole-game.fsx <trace.speedscope.json>"

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

let stack = ResizeArray<int>()
let mutable lastAt = -1.0

let queries =
  [ "Towers.tick"; "Enemies.tick"; "Projectiles"; "Waves"; "Spawning"
    "Defli.Diagnostics"; "World.update"; "Renderer2D"; "Application.view"
    "pushMapDelta"; "MapLookupNode"; "ElementMapNode"; "FilterMapNode"
    "MapCountNode"; "AdaptiveNode"; "TransactionBuffer"; "Input" ]

let names = HashSet<string>()

for struct (t, f, at) in events do
  if at <> lastAt && lastAt >= 0.0 then
    for q in queries do
      for f in stack do
        if (frameName f).Contains q then names.Add q |> ignore
  if t = "O" then stack.Add f
  elif stack.Count > 0 then stack.RemoveAt(stack.Count - 1)
  lastAt <- at

let mutable total = 0
let counts = Dictionary<string, int>()

for struct (t, f, at) in events do
  if at <> lastAt && lastAt >= 0.0 then
    total <- total + 1
    let mutable any = false
    for f in stack do
      for q in queries do
        if (frameName f).Contains q then
          counts[q] <- (if counts.ContainsKey q then counts[q] else 0) + 1
          any <- true
  if t = "O" then stack.Add f
  elif stack.Count > 0 then stack.RemoveAt(stack.Count - 1)
  lastAt <- at

printfn "═══ whole-game pie ═══  %d samples" total
counts
|> Seq.sortByDescending(fun kv -> kv.Value)
|> Seq.iter(fun kv -> printfn "  %5.1f%%  (%5d)  %s" (100.0 * float kv.Value / float total) kv.Value kv.Key)
