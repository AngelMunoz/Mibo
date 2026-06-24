#r "nuget: CliWrap"

open System
open System.IO
open CliWrap
open CliWrap.Buffered



module MGFXC =

  let mgfxc =
    if OperatingSystem.IsWindows() then
      Cli.Wrap("dotnet.exe").WithValidation(CommandResultValidation.None)
    else
      Cli.Wrap("dotnet").WithValidation(CommandResultValidation.None)

  let OpenGl input output =
    mgfxc.WithArguments [ "mgfxc"; input; output; "/Profile:OpenGL" ]

  let DirectX input output =
    mgfxc.WithArguments [ "mgfxc"; input; output; "/Profile:DirectX_11" ]


let ShaderList = [
  "LitSprite.fx"
  "LitSpriteNormalMap.fx"
  "Instanced.fx"
  "ForwardPbr.fx"
  "DepthShadow.fx"
  "Toon.fx"
]

let commandsToExecute = [
  for shader in ShaderList do
    let shaderPath = Path.Combine(__SOURCE_DIRECTORY__, shader)
    let dxPath = shaderPath.Replace(".fx", ".dx.mgfx")
    let oglPath = shaderPath.Replace(".fx", ".ogl.mgfx")

    async {
      printfn $"Compiling OpenGL for: {shader}"
      let cmd = MGFXC.OpenGl shaderPath oglPath

      let! result = cmd.ExecuteBufferedAsync().Task |> Async.AwaitTask
      printfn $"{result.StandardOutput}"

      if result.ExitCode <> 0 || not result.IsSuccess then
        eprintfn $"{result.StandardError}"

      printfn $"Finished with Exit Code: {result.ExitCode}"
    }

    async {
      printfn $"Compiling DirectX 11 For: {shader}"
      let cmd = MGFXC.DirectX shaderPath dxPath
      let! result = cmd.ExecuteBufferedAsync().Task |> Async.AwaitTask
      printfn $"{result.StandardOutput}"

      if result.ExitCode <> 0 || not result.IsSuccess then
        eprintfn $"{result.StandardError}"

      printfn $"Finished with Exit Code: {result.ExitCode}"
    }
]

Async.Sequential commandsToExecute |> Async.Ignore |> Async.RunSynchronously
