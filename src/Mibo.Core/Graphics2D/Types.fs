namespace Mibo.Elmish.Graphics2D

// ─────────────────────────────────────────────────────────────────────────────
// Shared 2D render vocabulary.
//
// RenderLayer previously lived in each backend's Command2D.fs (identical
// copies). Units of measure cannot be generic type parameters, so the fluent
// Draw DSL in Core needs ONE definition both backends can reference. It now
// lives here — same namespace, so existing code is unaffected.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Unit of measure for 2D render layer ordering.</summary>
[<Measure>]
type RenderLayer
