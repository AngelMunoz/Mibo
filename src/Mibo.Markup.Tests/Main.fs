module Mibo.Markup.Tests.Main

open Expecto

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssemblyWithCLIArgs [] argv
