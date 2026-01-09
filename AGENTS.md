# Mibo

Mibo is a micro framework built on top of MonoGame, designed to allow F# developers to write games using familiar patterns for all kinds of game generes and sizes.

Mibo aims to solve 90% of use cases for enabling developers to focus on game logic rather than boilerplate code providing guidelines and architecture for structuring game code, handling input, rendering, asset management, and time management among others.

General setup and usage instructions can be found in the [README.md](README.md) file.

## Project Structure

All of the projects live in the `src` folder:

- `Mibo`: Main library project
- `Mibo.Tests`: Unit and integration tests for the main library
- `3DSample`: Sample project showcasing 3D rendering capabilities
- `2DSample`: Sample project showcasing 2D rendering capabilities

The documentation site is built using [FsDocs](https://fsprojects.github.io/FSharp.Formatting/) and lives in the `docs` folder.

## Project Considerations

The core abstractions of the library MUST NOT incur in performance penalties for users. Abstractions should aim to be zero-cost or close to zero-cost.

- Prefer structs over classes unless struct size is too large.
- Prefer object expressions to classes.
- Avoid heap allocations in hot paths.
- Favor arrays and spans over lists.
- Favor ArrayPool over allocating new arrays where possible.
- Favor functional programming patterns but allow mutable state when necessary for performance.
- Public API should be ergonomic and easy to use.
- Public API should be well documented with XML comments.
- Public API should follow elmish-friendly patterns where applicable.

## Changelog Management

We follow https://github.com/ionide/KeepAChangelog guidelines

Changelog Format:

```markdown
# Changelog

## [Unreleased]

Content that is pending for release goes here.

## [1.0.0] - 2026.01.13

### Added

- Initial release
```

Each section may contain the following categories:

- Added
- Changed
- Deprecated
- Removed
- Fixed
- Security

When adding entries to the changelog, make sure to follow format and categories.
