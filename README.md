# Sketchy.gml

Sketchy is a source-to-source preprocessor for GameMaker `.gml` files. It runs during your project’s build pipeline and expands a small set of authoring-time conveniences into plain GML that the GameMaker compiler can consume, without requiring IDE changes.

It implements:
- **Template macros** with optional parameters: `#macro NAME(A, B) <expression>` (including `\` line continuations, normalized to single-line output)
- **Scoped const** declarations: `const NAME = <value>` limited to the current script/function scope, with conservative literal inlining and small compile-time folding (basic numeric ops and string concatenation).

## Installation
- Download and Unzip.
- Import the `.yypms` into gamemaker to import, include all assets.
- Copy the contents of `-- Copy To Project Root --` into the root of your project folder (with `*.yyp` and `*.resource_order`)

Sketchy is heavily inspired by **Shady.gml**, which pioneered this workflow for shaders via compiler scripts and safe preprocessing. Sketchy follows the same philosophy and packaging pattern, but targets GML scripts instead of shader sources.  
Credit: https://github.com/KeeVeeGames/Shady.gml
