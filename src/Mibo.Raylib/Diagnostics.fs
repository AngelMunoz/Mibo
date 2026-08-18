namespace Mibo.Diagnostics

open System
open System.IO
open FSharp.NativeInterop
open Raylib_cs

/// <summary>Screen capture for the raylib backend.</summary>
module RaylibDiagnostics =

  /// <summary>
  /// Saves the current frame buffer to a PNG file at the given path.
  /// </summary>
  /// <param name="path">The file path. A missing directory is created.</param>
  /// <remarks>
  /// Call this after the last draw call of the frame, so the frame is
  /// complete. The readback and the PNG encoding run on the calling thread,
  /// so expect one slow frame per capture.
  /// </remarks>
  let captureScreenshot(path: string) =
    let dir = Path.GetDirectoryName(path)

    if not(String.IsNullOrEmpty dir) then
      Directory.CreateDirectory(dir) |> ignore

    let width = Raylib.GetRenderWidth()
    let height = Raylib.GetRenderHeight()
    let pixels = Rlgl.ReadScreenPixels(width, height)

    try
      // rlgl hands back rows in top to bottom order with alpha forced to 255.
      let mutable image = Image()
      image.Data <- NativePtr.toVoidPtr pixels
      image.Width <- width
      image.Height <- height
      image.Mipmaps <- 1
      image.Format <- PixelFormat.UncompressedR8G8B8A8

      // AnsiBuffer is a ref struct without IDisposable, so no `use` here.
      let pathBuffer = AnsiBuffer(path)

      try
        Raylib.ExportImage(image, pathBuffer.AsPointer()) |> ignore
      finally
        pathBuffer.Dispose()
    finally
      Raylib.MemFree(NativePtr.toVoidPtr pixels)
