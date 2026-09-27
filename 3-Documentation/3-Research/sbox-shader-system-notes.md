# s&box Shader System — Research Notes (2026-09-27)

Compiled from a source-verified dig through the local engine checkout while building the
bar-exit event system's first event (`Code/DrunkChromaticEvent.cs`). Paths below are relative
to `1-Engine-Builds/Dev/sbox-public/` (engine) unless noted; the project file discussed is
`2-Projects/sboxgamejam3/Assets/shaders/drunkfx/drunkchromatic.shader` (+ `.shdrgrph`,
`.shader_c`).

See also: `3-Documentation/3-Research/shader-authoring-in-sbox.md` (the workspace's
shader-authoring single-source-of-truth doc; re-verified accurate against this checkout) and
`3-Documentation/2-Docs-Github-Repo/docs/rendering/shaders/` (mirrored official manual).

## 1. `.shader` file anatomy
- Top-level blocks: `HEADER` / `FEATURES` / `MODES` / `COMMON` / struct defs / `VS` / `PS`
  (or `CS`). Body is HLSL plus a compiler-annotation DSL. Post-process example in core:
  `game/core/shaders/postprocess/pp_chromaticaberration.shader` (`DevShader=true` header,
  passthrough NDC-quad VS, `CalculateViewportUv(i.vPositionSs.xy)` in MainPs).
- Annotation macros (`Attribute()`, `Default1/2/3/4`, `Range1`, `UiGroup`, `Semantic`) are NOT
  HLSL: they `#define` to `;` in `game/core/shaders/system.fxc` so raw HLSL compiles; the
  compiler *driver* parses them (they survive inside the embedded source in `.shader_c`).

## 2. Shader Graph (`.shdrgrph`) is a frontend
- `.shdrgrph` is a JSON node graph; editor save generates the sibling `.shader` beside it
  (`game/editor/ShaderGraph/Code/MainWindow.cs:141`) and compiles it. The generated PS body is
  SSA-style `l_0, l_1, ...` temp code — hand-editing such a `.shader` is futile if the graph is
  resaved; fix the graph or drive parameters from C# instead.
- `Domain: "PostProcess"` graphs auto-emit
  `Texture2D g_tColorBuffer < Attribute( "ColorBuffer" ); SrgbRead( true ); >;`
  (`game/addons/tools/Code/ShaderGraph/Compiler/GraphCompiler.cs:951-954`).
- Graph Float constants become `g_fl<CleanName>` shader variables (`GraphCompiler.cs:483`) and
  get an `Attribute("...")` annotation ONLY if the node's `IsAttribute` checkbox is on
  (`GraphCompiler.cs:495-506`). In `drunkchromatic.shdrgrph` all five Float nodes have
  `IsAttribute: false` — so `g_flScalingFactor` / `g_flDontTouch` / `g_flDrunkeneffectopacity`
  are plain material parameters (baked `Default1` values 0.1 / 0.23481488 / 0.39510602), NOT
  render attributes. (The `.shdrgrph` `Path` field still says `shaders/zoomshader.shdrgrph` —
  copy leftover from the original graph.)

## 3. Attributes vs material parameters (the binding rule)
- Official manual (`docs/rendering/shaders/attributes-and-variables.md`, "What's an attribute"):
  a shader variable bridges CPU→GPU via the RenderAttributes bag ONLY when declared
  `< Attribute("Name"); >`; it is then set from C# with `Attributes.Set("Name", v)`.
  Engine precedent: `pp_chromaticaberration.shader:59-60` declares
  `caAmount < Attribute("amount") >` / `caScale < Attribute("scale") >` and
  `engine/Sandbox.Engine/Scene/Components/PostProcessing/Effects/ChromaticAberration.cs:37-38`
  sets exactly `"scale"`/`"amount"`.
- Material parameters (no `Attribute()`) are instead addressed BY VARIABLE NAME on the
  Material: `Material.Set(string param, float value)` (`Material.Parameters.cs:96`, packs to
  Vector4; `Get/Set` are name-keyed on the native material). Engine precedent:
  `Scene/Components/Effects/BeamEffect.cs:412` does `_defaultMaterial.Set("g_tColor", Texture)`
  where `g_tColor` is a material parameter, not an attribute.
- UNVERIFIED (never confirmed live): whether `RenderAttributes.Set("g_flX", ...)` binds an
  UN-annotated shader constant. Every engine attribute precedent has the `Attribute()`
  annotation, so the docs-faithful assumption is that it does NOT bind. Mitigation shipped in
  `DrunkChromaticEvent.ChromaticPostProcess`: push the values BOTH ways — `Material.Set` by
  variable name in `OnUpdate` (the proven route) and `Attributes.Set` in `Render()` (harmless
  fallback if the shader ever gains `Attribute()` annotations).

## 4. `Material.FromShader` and material-copy discipline
- `Material.FromShader(path)` (`Material.Static.cs:38-78`) synthesizes an anonymous
  `__shader_<path>.vmat` and CACHES it globally per shader path — all callers share one
  Material instance. Mutating it (`.Set`) is project-wide by design; it is safe only for a
  single shared fullscreen effect, NOT per-object values.
- Per-object values need `material.CreateCopy(name)` (`Material.cs:64`) +
  `Renderer.Materials.SetOverride(slot, copy)` — engine precedent `BeamEffect.cs:266`; project
  precedent `Code/PantsDropController.cs:287,326` (its "Material.Set is banned" comment is
  exactly this shared-cache coupling hazard).
- Authored `.vmat` files reference the COMPILED path (`shaders/<name>.shader_c`) and serialize
  the Default()-parameter values; `FromShader` is the code-driven bare-shader route.
  Project examples of both: `Assets/shaders/bar_glow.shader` (used via a .vmat) vs
  `pants_drop.shader`/`obstruction_fade.shader` (true attributes driven per-object via
  `SceneObject.Attributes.Set` — `pants_drop.shader:114-127`, `Code/CCCamera.cs:600-608`).

## 5. Post-process plumbing (C# side)
- `BasePostProcess` (`Scene/Components/PostProcessing/BasePostProcess.cs`) is
  `ExecuteInEditor` + `DontExecuteOnServer`; generic `BasePostProcess<T>` adds volume-weighted
  `GetWeighted(x => x.Prop)` merging (L140-163). Effects `Blit(BlitMode...)` from `Render()`;
  `BlitMode.WithBackbuffer` makes `Blit` do `cl.Attributes.GrabFrameTexture("ColorBuffer", mips)`
  first (L100-114) — which is what feeds the graph shader's `g_tColorBuffer`.
- Collection: `PostProcessSystem.End()` gathers `BasePostProcess` under the camera component's
  children AND from `PostProcessVolume`s, groups by type, calls `Render()` per group — so a
  loose event node must be parented under the camera or carry an infinite volume to have any
  effect (`PostProcessSystem.cs:26-129`).
- `Stage` enum (`Systems/Render/Stage.cs`): AfterDepthPrepass 1000 → AfterOpaque 2000 →
  AfterSkybox 3000 → AfterTransparent 4000 → AfterViewmodel 5000 → EarlyUI 5500 →
  BeforePostProcess 6000 → Tonemapping 6500 → AfterPostProcess 7000 → UI 7500 → AfterUI 8000.
  Within a stage, `Order` sorts ascending (`PostProcessLayers.cs:60-66`); core effects' orders:
  ChromaticAberration 1000, ColorGrading 4000, Pixelate 10000.
- `g_flTime` is an engine per-view global (`game/core/shaders/common.fxc:40`,
  `docs/rendering/shaders/reference/global-variables.md`) — never set it from C#.

## 6. Compiled artifacts and validation loop
- `.shader_c` is the compiled output (embedded masked HLSL source + parameter table; confirmed
  via `strings` on `drunkchromatic.shader_c`, which lists the three `g_fl*` names and
  `ColorBuffer`). The editor regenerates it on save via hot reload
  (`game/addons/tools/Code/Shaders/ShaderHooks.cs` — `content.changed` / `compile.shader` →
  `EditorUtility.CompileShader`); there is NO offline C#-style compile gate for shaders —
  validate in the live editor or grep `logs/sbox-dev.log` for `DXC Err` (see
  `shader-authoring-in-sbox.md` §6 / pitfall 17).
