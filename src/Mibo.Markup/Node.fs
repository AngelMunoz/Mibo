namespace Mibo.Markup

open System
open System.Collections.Immutable

/// The parser's token union. What a front-end hands over before any
/// statement names the expected type. Consumed once, during resolution —
/// never stored past it.
///
/// Three cases, by design: a number token, a decimal token, and a word
/// (a bare identifier, a quoted string, or `true`/`false`). There is no
/// arithmetic case (the resolver computes; the text never does) and no
/// parameter case (sizes come from properties).
///
/// Not a struct union by language rule: a multi-case struct union needs
/// one shared field type, and `Word` carries a string. Args live only
/// at parse and resolve time (never per frame), so the allocations do
/// not reach a hot path.
type Arg =
  | Number of int
  | Decimal of float
  | Word of string

/// One `name=value` property: the layout channel of a node. `w=6`,
/// `place="center"`, `pack="flow"` — in XML terms, an attribute.
[<Struct>]
type Prop = { Name: string; Value: Arg }

/// A cell-space extent: width across, height down. The document model's
/// named pair for element sizes (`w=`/`h=`, extents, per-axis gaps) —
/// the counterpart of `Mibo.Layout.CellPoint`/`CellRect`, which carry
/// points and rectangles.
[<Struct>]
type CellSize = { W: int; H: int }

/// One markup node: `kind args? props? { children }?`. `Position` is the
/// character offset of the node's first token when the front-end tracks
/// positions (KDL), or `-1` when it does not (XML) — resolution errors
/// then name the element instead of pointing at a line. `Label` carries
/// a template definition's name.
[<Struct>]
type Node = {
  Kind: string
  Position: int
  Label: string voption
  Props: ImmutableArray<Prop>
  Args: ImmutableArray<Arg>
  Children: ImmutableArray<Node>
}

/// Format-neutral helpers over the node tree. The front-ends (`Xml`,
/// `Kdl`) produce `Node` trees the resolver consumes without seeing the
/// text format.
module Markup =

  /// The line and column of a character offset, for positioned error
  /// messages. A negative offset means the front-end does not track
  /// positions; the caller gets an empty string and says so itself.
  let inline where (src: string) (position: int) : string =
    if position < 0 then
      ""
    else
      let mutable line = 1
      let mutable lineStart = 0
      let last = min (position - 1) (src.Length - 1)

      for i in 0..last do
        if src[i] = '\n' then
          line <- line + 1
          lineStart <- i + 1

      $"{line}:{position - lineStart + 1}"

  /// The positioned half of an error message: `" (line:col)"` when the
  /// front-end tracks positions, `""` when it does not.
  let inline at (src: string) (position: int) : string =
    match where src position with
    | "" -> ""
    | w -> $" ({w})"
