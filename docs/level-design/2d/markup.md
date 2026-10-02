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
named properties, children, and a character offset for error messages.
The resolver (`Doc.resolve`) never sees the text format — XML today,
KDL in the next release of this stack — and errors always carry a line
and column:

```fsharp
open Mibo.Markup

match Xml.parse src with
| Error e -> printfn "%s" e          // "12:5: unknown element 'plaza'"
| Ok roots -> ...
```

## The XML front-end

Elements are nodes, attributes are properties (typed: int, then float,
then word), and text-only `<a>` children carry positional arguments.
Comments are free:

```xml
<map>
  <a>36</a><a>20</a>
  <!-- the meadow -->
  <field><a>grass</a></field>
  <plot x="1" y="1" w="5" h="5" pack="scatter" seed="13">
    <a>boulder</a><a>boulder</a><a>boulder</a>
  </plot>
</map>
```

*The resolver (`Doc`), the game-supplied surface (words, kernels,
elements), and the Flow emitter (`DocFlow`) land with the rest of this
stack — this page grows with them.*
