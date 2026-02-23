# Sketchy.gml

Sketchy is a source-to-source preprocessor for GameMaker `.gml` files. It runs as part of your project’s build pipeline and expands a small set of authoring-time conveniences into plain GML that the GameMaker compiler can consume, without requiring any IDE changes.

## Features

### Macros
- Template-style macros with optional parameters:
  - `#macro NAME(A, B) <expression>`
- Supports `\` line continuations (definitions are normalized to single-line output to keep line numbers stable).
- Expansion is token-based (identifier matches only) and does not occur inside comments or strings.
- Nested macro expansion is supported, with recursion detection and hard caps to prevent runaway growth.

### Const
- Scoped constants:
  - `const NAME = <value>`
- Scope is limited to the current script top-level or current function body.
- Conservative lowering rules:
  - Inline substitution for provable literals (numbers and string literals, including single-line raw strings).
  - Template strings with `{}` interpolation are treated as dynamic and lowered via the standard non-inline path.
  - Arrays/structs (and other non-provable values) are not inlined.
  - Multi-line raw strings are preserved by rewriting `const` -> `var` (to keep embedded line breaks and line mapping stable).

### Nullish chaining (`?.`)
- null-safe member access operator:
  - `a?.b` evaluates to `undefined` if `a` is `undefined`, otherwise it evaluates to `a.b`.
  - Chains short-circuit left-to-right: `a?.b?.c` stops as soon as any hop is `undefined`.

### Closure directive (`closure(...)`)
- GameMaker functions do not capture locals from outer scopes the way closures do in languages like JS. Sketchy provides an explicet directive:
  - `closure(function(){ ... })`
- Key rules:
  - Capture analysis is conservative and token-based.
  - Only identifiers referenced in the closure’s *immediate body* are captured.
	  - References inside nested inner functions do **not** contribute to the capture set.

## Installation
1. Download and unzip.
2. Import the `.yypms` into GameMaker (include all assets).
3. Copy the contents of `-- Copy To Project Root --` into the root of your project (next to your `*.yyp` and `*.resource_order`).

## Credits
Sketchy is heavily inspired by **Shady.gml**, which pioneered this workflow for shaders via compiler scripts and safe preprocessing. Sketchy follows the same philosophy and packaging pattern, but targets GML scripts instead of shader sources.

https://github.com/KeeVeeGames/Shady.gml
