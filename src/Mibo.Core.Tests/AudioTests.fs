module Mibo.Core.Tests.Audio

open Expecto
open Mibo.Audio

[<Tests>]
let tests =
  testList "Audio" [

    testList "Voice" [
      testCase "center is full volume, centered, normal pitch"
      <| fun _ ->
        Expect.equal
          Voice.center
          {
            Volume = 1.0f
            Pan = 0.0f
            Pitch = 1.0f
          }
          "default voice"

      testCase "ofVolume keeps center pan and pitch"
      <| fun _ ->
        Expect.equal
          (Voice.ofVolume 0.5f)
          {
            Volume = 0.5f
            Pan = 0.0f
            Pitch = 1.0f
          }
          "volume voice"
    ]

    testList "Attenuation2D" [
      testCase "source on the listener is full volume and centered"
      <| fun _ ->
        let voice = Attenuation2D.compute (0.0f, 0.0f, 0.0f) (0.0f, 0.0f) 100.0f

        Expect.equal voice.Volume 1.0f "full volume at zero distance"
        Expect.equal voice.Pan 0.0f "centered at zero distance"
        Expect.equal voice.Pitch 1.0f "pitch untouched"

      testCase "volume falls to zero at max distance"
      <| fun _ ->
        let voice =
          Attenuation2D.compute (0.0f, 0.0f, 0.0f) (100.0f, 0.0f) 100.0f

        Expect.equal voice.Volume 0.0f "silent at max distance"

      testCase "volume is half at half distance"
      <| fun _ ->
        let voice =
          Attenuation2D.compute (0.0f, 0.0f, 0.0f) (50.0f, 0.0f) 100.0f

        Expect.floatClose
          Accuracy.medium
          (float voice.Volume)
          0.5
          "linear falloff"

      testCase "source straight ahead when facing +X is centered"
      <| fun _ ->
        let voice =
          Attenuation2D.compute (0.0f, 0.0f, 0.0f) (80.0f, 0.0f) 100.0f

        Expect.floatClose
          Accuracy.medium
          (float voice.Pan)
          0.0
          "dead ahead is centered"

      testCase "source on the listener's right pans right"
      <| fun _ ->
        // Facing +X with screen coordinates (Y down): a source at +Y is on
        // the listener's right.
        let voice =
          Attenuation2D.compute (0.0f, 0.0f, 0.0f) (0.0f, 80.0f) 100.0f

        Expect.isGreaterThan voice.Pan 0.5f "panned right"

      testCase "source beyond max distance stays silent"
      <| fun _ ->
        let voice =
          Attenuation2D.compute (0.0f, 0.0f, 0.0f) (500.0f, 0.0f) 100.0f

        Expect.equal voice.Volume 0.0f "clamped to silence"
    ]

    testList "Fade" [
      testCase "starts at the start volume"
      <| fun _ ->
        Expect.equal (Fade.volume 0.2f 1.0f 0.0f 2.0f) 0.2f "start value"

      testCase "ends at the target volume"
      <| fun _ ->
        Expect.equal (Fade.volume 0.2f 1.0f 2.0f 2.0f) 1.0f "target value"

      testCase "past the end holds the target"
      <| fun _ ->
        Expect.equal (Fade.volume 0.2f 1.0f 5.0f 2.0f) 1.0f "holds target"

      testCase "half way is the midpoint"
      <| fun _ ->
        Expect.floatClose
          Accuracy.medium
          (float(Fade.volume 0.0f 1.0f 1.0f 2.0f))
          0.5
          "midpoint"

      testCase "zero duration jumps to the target"
      <| fun _ ->
        Expect.equal (Fade.volume 0.2f 0.7f 0.0f 0.0f) 0.7f "instant fade"
    ]
  ]
