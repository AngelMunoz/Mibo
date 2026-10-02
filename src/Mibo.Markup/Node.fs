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
type Arg =
  | Number of int
  | Decimal of float
  | Word of string

/// One `name=value` property: the layout channel of a node. `w=6`,
/// `place="center"`, `pack="flow"`.
[<Struct>]
type Prop = { Name: string; Value: Arg }

/// One markup node: `kind args? props? { children }?`. `Position` is the
/// character offset of the node's first token, for error positions.
/// `Label` carries a template definition's name.
[<Struct>]
type Node = {
  Kind: string
  Position: int
  Label: string voption
  Props: ImmutableArray<Prop>
  Args: ImmutableArray<Arg>
  Children: ImmutableArray<Node>
}

/// Format-neutral helpers over the node tree. Every front-end (`Xml`,
/// `Kdl`) produces the same `Node` array; the resolver never sees the
/// text format.
module Markup =

  /// The line and column of a character offset, for positioned error
  /// messages.
  let where (src: string) (position: int) : string =
    let mutable line = 1
    let mutable lineStart = 0
    let last = min (position - 1) (src.Length - 1)

    for i in 0..last do
      if src[i] = '\n' then
        line <- line + 1
        lineStart <- i + 1

    $"{line}:{position - lineStart + 1}"
