/// Reads the GC lifecycle events (gc-verbose) from a nettrace and reports:
///   - number of GCs (GC/Start), their type (blocking vs background)
///   - the stop-the-world suspension windows
///     (GC/SuspendEEStart -> GC/RestartEEStop) in wall ms
/// Usage: dotnet run --project tools/gcprobe -- <trace.nettrace>
module GcProbe

open System
open Microsoft.Diagnostics.Tracing
open Microsoft.Diagnostics.Tracing.Parsers.Clr

[<EntryPoint>]
let main argv =
  if argv.Length < 1 then
    eprintfn "usage: gcprobe <trace.nettrace>"
    1
  else
    use source = new EventPipeEventSource(argv[0])
    let mutable gcCount = 0
    let mutable blockingGcs = 0
    let mutable backgroundGcs = 0
    let gcs = ResizeArray<struct (float * float)>()
    let suspensions = ResizeArray<struct (float * float)>()
    let mutable gcStartMs = None
    let mutable suspendStartMs = None

    // The typed GCStart handler gives the GC type (blocking vs background).
    source.Clr.add_GCStart(fun e ->
      gcCount <- gcCount + 1

      if e.Type = GCType.NonConcurrentGC then
        blockingGcs <- blockingGcs + 1
      else
        backgroundGcs <- backgroundGcs + 1)

    // The Stop/suspend/restart events have no typed accessors on the parser;
    // the EventPipe manifest names are "GC/Stop", "GC/SuspendEEStart",
    // "GC/RestartEEStop" (NOT the ETW _V1 names).
    source.Clr.add_All(fun e ->
      match e.EventName with
      | "GC/Start" -> gcStartMs <- Some e.TimeStampRelativeMSec
      | "GC/Stop" ->
        match gcStartMs with
        | Some s ->
          gcs.Add(struct (s, e.TimeStampRelativeMSec))
          gcStartMs <- None
        | None -> ()
      | "GC/SuspendEEStart" -> suspendStartMs <- Some e.TimeStampRelativeMSec
      | "GC/RestartEEStop" ->
        match suspendStartMs with
        | Some s ->
          suspensions.Add(struct (s, e.TimeStampRelativeMSec))
          suspendStartMs <- None
        | None -> ()
      | _ -> ())

    source.Process()

    printfn
      "GCs: %d  (typed: blocking %d, background %d)"
      gcCount
      blockingGcs
      backgroundGcs

    let durations(xs: ResizeArray<struct (float * float)>) =
      xs |> Seq.map(fun struct (s, e) -> e - s) |> Seq.toArray

    let stats (label: string) (xs: ResizeArray<struct (float * float)>) =
      let d = durations xs

      if d.Length > 0 then
        let sum = Array.sum d
        let avg = sum / float d.Length
        let max = Array.max d

        printfn
          "%s: count %d  total %.1f ms  avg %.3f ms  max %.2f ms"
          label
          d.Length
          sum
          avg
          max

        let bucket lo hi =
          d |> Array.filter(fun v -> v >= lo && v < hi) |> Array.length

        printfn
          "  distribution: <1ms:%d  1-5ms:%d  5-20ms:%d  20-100ms:%d  >=100ms:%d"
          (bucket 0.0 1.0)
          (bucket 1.0 5.0)
          (bucket 5.0 20.0)
          (bucket 20.0 100.0)
          (d |> Array.filter(fun v -> v >= 100.0) |> Array.length)

        printfn "  top 8 (session position s):"

        xs
        |> Seq.mapi(fun i struct (s, e) -> (i, e - s, s / 1000.0))
        |> Seq.sortByDescending(fun (_, d, _) -> d)
        |> Seq.truncate 8
        |> Seq.iter(fun (i, d, pos) ->
          printfn "    #%d: %.2f ms at t=%.1f s" i d pos)

    stats "GC work (Start->Stop)" gcs
    stats "STW pause (SuspendEEStart->RestartEEStop)" suspensions

    if gcCount = 0 then
      printfn "(no GC events in this trace)"

    0
