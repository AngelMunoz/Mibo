module Mibo.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssemblyWithCLIArgs [] argv
