---
title: Markup - Authored Level Documents
category: Level Design
categoryindex: 8
index: 11
---

# Markup: authored level documents

Flow authors levels as F# values. **Mibo.Markup** adds a text front-end
to the same model: level documents you can edit while the game runs,
keep in version control, and hand to a level designer who does not read
F#. Build-time only — a document parses and lays out once, through the
same `Flow.run` your code uses, and the game never sees the text again.

Two rules keep the format honest:

1. **Content and layout are two concerns.** A node's body paints its own
   box in local coordinates; layout lives in node properties (`x=`, `y=`,
   `w=`, `h=`, `pack=`, ...). Layout edits touch properties only.
2. **The engine owns statements; the game owns vocabulary.** Every
   statement (`fill`, `set`, `generate`, `plot`, ...) means the same
   thing in every game. Cell words, kernels, and element libraries are
   the game's say — a document stays portable at the statement level.

## The node tree is the contract

Every front-end produces the same `Node` tree: a kind, positional args,
named properties, and children. The resolver (`Doc.resolve`) never sees
the text format. The two front-ends differ in one channel: KDL spells
scalars as positional arguments (`map 36 20`), XML spells them as
attributes (`map w="36" h="20"`), and the resolver reads every scalar by
name so both resolve identically:

```fsharp
open Mibo.Markup

match Kdl.parse src with
| Error e -> printfn "%s" e          // "unknown element 'plaza' (12:5)"
| Ok roots -> ...
```

KDL documents carry node positions, so resolution errors point at the
authoring line. XML does not track node positions (the BCL line-info
surface is not reachable from F# without fragile reference tricks), so
XML resolution errors name the element; XML parse errors carry the
parser's own line and position.

## The XML front-end

XML the way XML means it: elements are nodes and children, attributes
are the scalar channel. Comments are free, and the BCL parser does all
the parsing:

```xml
<map w="36" h="20">
  <!-- the meadow -->
  <field><fill cell="grass" /></field>
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13">
    <boulder /><boulder /><boulder />
  </plot>
</map>
```

An attribute value types as int, then finite float, then word (`x="1"`
is a number, `cell="grass"` is a word, `f="NaN"` stays a word — the
resolver never computes with non-finite floats). An `element`
definition names itself with the `name` attribute.

*The resolver (`Doc`), the game-supplied surface (words, kernels,
elements), and the Flow emitter (`DocFlow`) land with the rest of this
stack — this page grows with them.*
