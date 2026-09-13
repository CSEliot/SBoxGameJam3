# CodeTestPortable

Offline, editor-independent compile gate for any s&box project's `Code/`. Generalized from
kingofgamesmuseum's `kingofgamesmuseum-verify/` (itself modeled on terrygotchi's `terrygotchi-verify/`).

## Why

A project's own `Code/<name>.csproj` usually isn't buildable standalone - its DLL references are
either relative to a specific Steam library path or missing entirely (the file is meant to be opened
by the s&box editor, which supplies the engine references itself). This folder is a scratch SDK-style
project that binds against the real engine + base DLLs from your local install and compiles the
project's actual `Code/**` sources (plus transpiled `.razor`) with the real `Sandbox.Generator` /
`Sandbox.CodeUpgrader` analyzers - so `dotnet build` here catches the same compile errors the editor
would, without opening it.

## Install

Copy this whole `CodeTestPortable/` folder so it sits as a SIBLING of the target project's `Code/`
folder (same level as `Code/`, `Editor/`, the `.sbproj`). Nothing inside needs editing for a normal
setup - it doesn't hardcode a project name, assembly name, or install path.

Rename the folder if you like (e.g. `myproject-verify/`) - nothing inside depends on the folder's own
name, only on its position relative to `../Code`.

The `.sbproj` only compiles `Code/**`, so the s&box editor ignores this whole directory - no collision
with the real game assembly.

## Rebuild loop

```
cd CodeTestPortable
dotnet run -c Release --project razorgen/razorgen.csproj   # transpile .razor -> gen/*.cs (run FIRST)
dotnet build GameCompileCheck.csproj -c Release              # compile-check the game code
```

Run `razorgen` every time a `.razor` file is added, removed, or edited - it wipes stale `gen/*.razor.cs`
first so a renamed/removed component leaves no ghost type. If the project has no `.razor` files, or you
only touched `.cs` files, skip straight to the `dotnet build` step (razorgen still runs fine against a
project with zero `.razor` files - it just reports 0 transpiled).

## Portability

`SboxRoot.props` (imported by both csproj) resolves the s&box engine install root - no hardcoded
per-machine path anywhere in the project files:

1. `SBOX_ROOT` environment variable, if set - use this for a non-standard install.
2. Otherwise probes known Steam library locations on both Linux and Windows (checks each for
   `bin/managed/Sandbox.Generator.dll`) and uses the first that exists.

If neither resolves, the build fails fast with a clear error instead of a confusing
`CS0006`/`MSB3245` "metadata file not found" buried in compiler output. To add a new machine's install
location, either set `SBOX_ROOT` or add a candidate line to `SboxRoot.props`.

All other paths (`Code/`, `gen/`) are relative to the csproj files, so this whole `CodeTestPortable/`
directory travels with whatever repo it's dropped into, regardless of where that repo is cloned.

## Folding in a source Library

Some projects need a third-party addon compiled from source instead of resolved as a packaged DLL
(e.g. terrygotchi's `carsonk.twitchapi`, when the packaged binary targets a different Sandbox.Game
identity than the dev compile). To add one, uncomment and adjust the commented `<Compile Include>`
block in `GameCompileCheck.csproj`, pointing at `../Libraries/<addon>/Code/**`, and exclude any
`Examples/**` or `obj/**` that shouldn't compile.

## What this does NOT prove

Compiling here proves the code binds against real engine types/APIs. It does NOT prove:

- Runtime behavior - networking, WebSocket/backend authority, any non-compile-time logic.
- Razor layout/scroll/styling - `.razor` files compiling 0/0 here says nothing about how they render.

Always playtest in-editor for anything gameplay- or layout-affecting. This gate is compile-only.

## Layout

- `SboxRoot.props` - shared engine-root resolution, imported by both csproj below.
- `GameCompileCheck.csproj` - compiles `../Code/**/*.cs` + `gen/**/*.cs` (transpiled razor) against
  the real engine DLLs.
- `razorgen/` - console exe that transpiles any `.razor` under `../Code` to `gen/*.cs` via the same
  `Sandbox.Razor.RazorProcessor.GenerateFromSource` call the editor uses.
- `gen/` - generated output, gitignored.
