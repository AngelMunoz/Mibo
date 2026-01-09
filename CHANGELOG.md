# Changelog

## [Unreleased]

## [1.0.1] - 2026-01-09

### Fixed

- Templates: fixed broken templates that wouldn't even compile. (my bad)

## [1.0.0] - 2026-01-09

### Added

- Changed something in the package
- Updated the target framework
- Core: `ElmishGame` runtime loop integrating Elmish architecture with MonoGame lifecycle.
- Core: `Program` module for configuring init, update, renderers, and services.
- System: `System` pipeline for organizing frame logic into mutable and read-only phases (`pipeMutable`, `snapshot`, `pipe`).
- Input: Reactive `Input` service for Keyboard, Mouse, Touch, and Gamepad polling.
- Input: `InputMapper` for binding hardware triggers to semantic Game Actions.
- Input: Subscription helpers (`Keyboard`, `Mouse`, `Gamepad`, `Touch`) for Elmish event handling.
- Rendering: `RenderBuffer` for allocation-friendly command sorting and batching.
- Rendering: `Batch2DRenderer` for layer-sorted 2D sprite rendering.
- Rendering: `Camera2D` with viewport bounds and screen-to-world conversion.
- Rendering: `Batch3DRenderer` supporting `BasicEffect`, `SkinnedEffect`, and custom effects.
- Rendering: `Camera3D` with perspective/orbit modes, raycasting, and frustum generation.
- Rendering: Specialized batchers for 3D Sprites (`BillboardBatch`, `SpriteQuadBatch`) and Quads.
- Assets: `IAssets` service for loading and caching Textures, Models, Fonts, and Sounds.
- Time: `FixedStep` configuration for deterministic physics/simulation steps.
- Math: `Culling` module for frustum containment checks.
