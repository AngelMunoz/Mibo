module Defli.Tests.Main

open Expecto

// This exposes the test lists to Expecto's assembly scanner
[<Tests>]
let allTests =
  testList "All" [
    DomainTests.tests
    MapTests.tests
    MapTests.proceduralTests
    MapTests.probeTests
    EnemiesTests.tests
    SpawningTests.tests
    WavesTests.tests
    EconomyTests.tests
    ProjectionTests.tests
    TowersTests.tests
    ProjectilesTests.tests
    DiagnosticsTests.tests
    RouterTests.tests
    CameraTests.tests
  ]

[<EntryPoint>]
let main argv =
  Tests.runTestsInAssemblyWithCLIArgs [] argv
