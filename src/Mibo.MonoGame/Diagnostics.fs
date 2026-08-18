namespace Mibo.Diagnostics

open System
open System.IO
open Microsoft.Xna.Framework
open Microsoft.Xna.Framework.Graphics
open MonoGame.Framework.Utilities

/// <summary>Screen capture for the MonoGame backends.</summary>
module MonoGameDiagnostics =

  /// <summary>
  /// Saves the back buffer to a PNG file at the given path.
  /// </summary>
  /// <param name="device">The graphics device of the running game.</param>
  /// <param name="path">The file path. A missing directory is created.</param>
  /// <remarks>
  /// Call this at the end of draw, before the framework presents the frame.
  /// The readback and the PNG encoding run on the calling thread, so expect
  /// one slow frame per capture.
  /// </remarks>
  let captureScreenshot (device: GraphicsDevice) (path: string) =
    let parameters = device.PresentationParameters
    let width = parameters.BackBufferWidth
    let height = parameters.BackBufferHeight

    let dir = Path.GetDirectoryName(path)

    if not(String.IsNullOrEmpty dir) then
      Directory.CreateDirectory(dir) |> ignore

    let data = Array.zeroCreate<Color>(width * height)
    device.GetBackBufferData(data)

    // The OpenGL readback hands back rows bottom to top. The other backends
    // hand back rows top to bottom. Flip on OpenGL so every backend matches.
    if PlatformInfo.GraphicsBackend = GraphicsBackend.OpenGL then
      let mutable top = 0
      let mutable bottom = (height - 1) * width
      let mutable row = 0

      while row < height / 2 do
        let mutable column = 0

        while column < width do
          let swap = data[top + column]
          data[top + column] <- data[bottom + column]
          data[bottom + column] <- swap
          column <- column + 1

        top <- top + width
        bottom <- bottom - width
        row <- row + 1

    use texture =
      new Texture2D(device, width, height, false, SurfaceFormat.Color)

    texture.SetData(data)

    use stream = File.Create(path)
    texture.SaveAsPng(stream, width, height)
