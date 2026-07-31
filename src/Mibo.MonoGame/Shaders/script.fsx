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

  let DirectX12 input output =
    mgfxc.WithArguments [ "mgfxc"; input; output; "/Profile:DirectX_12" ]

  let Vulkan input output =
    mgfxc.WithArguments [ "mgfxc"; input; output; "/Profile:Vulkan" ]


// Cross-backend shaders — compiled for all 4 backends.
let ShaderList = [
  "LitSprite.fx"
  "LitSpriteNormalMap.fx"
  "Instanced.fx"
  "ForwardPbr.fx"
  "DepthShadow.fx"
]

// DX12-only shaders — use a named cbuffer (register b1) which other backends reject.
// ForwardPbrGrouped.fx and DepthShadowGrouped.fx are loaded only on DX12 at runtime.
let Dx12OnlyShaderList = [ "ForwardPbrGrouped.fx"; "DepthShadowGrouped.fx" ]

let commandsToExecute = [
  for shader in ShaderList do
    let shaderPath = Path.Combine(__SOURCE_DIRECTORY__, shader)
    let dxPath = shaderPath.Replace(".fx", ".dx.mgfx")
    let oglPath = shaderPath.Replace(".fx", ".ogl.mgfx")
    let dx12Path = shaderPath.Replace(".fx", ".dx12.mgfx")
    let vkPath = shaderPath.Replace(".fx", ".vk.mgfx")

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

    async {
      printfn $"Compiling DirectX 12 For: {shader}"
      let cmd = MGFXC.DirectX12 shaderPath dx12Path
      let! result = cmd.ExecuteBufferedAsync().Task |> Async.AwaitTask
      printfn $"{result.StandardOutput}"

      if result.ExitCode <> 0 || not result.IsSuccess then
        eprintfn $"{result.StandardError}"

      printfn $"Finished with Exit Code: {result.ExitCode}"
    }

    async {
      printfn $"Compiling Vulkan For: {shader}"
      let cmd = MGFXC.Vulkan shaderPath vkPath
      let! result = cmd.ExecuteBufferedAsync().Task |> Async.AwaitTask
      printfn $"{result.StandardOutput}"

      if result.ExitCode <> 0 || not result.IsSuccess then
        eprintfn $"{result.StandardError}"

      printfn $"Finished with Exit Code: {result.ExitCode}"
    }
]

Async.Sequential commandsToExecute |> Async.Ignore |> Async.RunSynchronously

// DX12-only shaders: compile just for DirectX_12 profile.
let dx12OnlyCommands = [
  for shader in Dx12OnlyShaderList do
    let shaderPath = Path.Combine(__SOURCE_DIRECTORY__, shader)
    let dx12Path = shaderPath.Replace(".fx", ".dx12.mgfx")

    async {
      printfn $"Compiling DirectX 12 For: {shader}"
      let cmd = MGFXC.DirectX12 shaderPath dx12Path
      let! result = cmd.ExecuteBufferedAsync().Task |> Async.AwaitTask
      printfn $"{result.StandardOutput}"

      if result.ExitCode <> 0 || not result.IsSuccess then
        eprintfn $"{result.StandardError}"

      printfn $"Finished with Exit Code: {result.ExitCode}"
    }
]

Async.Sequential dx12OnlyCommands |> Async.Ignore |> Async.RunSynchronously
