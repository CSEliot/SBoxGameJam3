# Pants Drop - canonical design + bind-pose calibration document

Status: DESIGN + CALIBRATION MEASURED (2026-09-23). This document merges and
SUPERSEDES `../Pants-Drop.md` (shader-based design notes); the old file is left
on disk unmodified as a git-history pointer only - do not update it. The
superseded doc's status line said "DESIGN, nothing implemented yet"; that was
already stale there (the state machine below is built) - current truth: design
settled, calibration measured, DrunkCC state machine implemented, playtesting
pending, items 8-9 in progress elsewhere.

The obstacle-collision "pants drop" needs a VISUAL: the worn pants slide/bunch
down toward the ankles (then back up via R-mashing). This document records the
chosen technique, the engine findings it rests on (verified on disk, with
citations), the measured bind-pose constants, the HideBody decision, the
knockdown decisions, and the remaining open items.

Related: `Architecture.md` "Pants Down" (clothing slot/layer plan),
`Design.md` (pants levels + gameplay), `Assets/shaders/obstruction_fade.shader`
(the in-project precedent for the rendering pattern used here).

## Target asset

- The pants are FORCED on every player: `GameManager._LongPants`
  (`Code/GameManager.cs:76`), already wired in `Assets/scenes/minimal.scene`
  to the engine addon asset
  `models/citizen_clothes/trousers/trackiebottoms/trackie_bottoms_black.clothing`.
  Assignment logic in GameManager is NOT yet implemented.
- Steam install on disk: `~/.local/share/Steam/steamapps/common/sbox/addons/
  citizen/Assets/models/citizen_clothes/trousers/TrackieBottoms/`. Ships
  COMPILED ONLY: `.vmdl_c`/`.vmat_c`/`.clothing_c` plus stray `.fbx.meta`/
  `.vmdl.meta` with no actual FBX/vmdl/vmat source. Consequence: the mesh
  cannot be edited or have a gradient baked into vertex data without
  re-authoring; calibration constants must be - and now have been - measured
  from an FBX taken from the engine source checkout (see Calibration below).
- Underneath: project-owned `Assets/clothing/heart_pattern_boxers.clothing`
  (boxers revealed when the pants are down), per Architecture.md's Pants Down plan.
- Both garments use the same Citizen skeleton; `trackie_bottoms_black.vmat`
  renders via `shaders/complex.shader` (confirmed in the compiled vmat strings).
- `_m_human` model variants exist (HumanAltModel). This game is Citizen-only
  (Terry Sausages); human variants are out of scope but noted.

## Core technique (v0)

Post-skin world-space vertex displacement in a custom material shader, driven
per-instance from C#. Rationale, from engine shader source (live Steam install,
`core/shaders/`):

- Clothing meshes are multi-bone skinned. The engine resolves posed world
  positions either in a compute pre-pass (`skinning_cs.shader`, feeding the
  `D_CS_VERTEX_ANIMATION` branch of `VS_CommonProcessing`,
  `vr_common_vs_code.fxc:89-108`) or, for rigid single-bone cases, directly in
  the VS (`vr_common_vs_code.fxc:109-140`, single `i.nBoneIndex.x`). Bending
  leg clothing near-certainly takes the compute path. Either way, by the time a
  material's `MainVs` runs, `o.vPositionWs` is ALREADY posed; there is no
  per-material hook to deform pre-skin. (Inferred from the two visible
  branches; the glue file `instancing.fxc` that would prove the scheduling
  ships nowhere in the source trees - see Open Items.)
- Therefore: let `ProcessVertex(i)`/`VS_SharedStandardProcessing(i)` run
  unchanged, then displace `o.vPositionWs` along a drop axis by
  `t * g_flDropAmount * g_flMaxDropDistance`, where `t` is a 0..1 gradient
  derived from the RAW bind-pose position `i.vPositionOs` (always present as a
  vertex stream regardless of skin path, `vertexinput.hlsl:6`; unused by the
  common VS code, available to custom code). `t=1` at the waistband, `t=0` at
  the ankles; constants calibrated once from this specific mesh - now measured,
  see Calibration below.
- Garment-shape-blindness (pockets/belt loops sinking in "melting slabs") was
  the old open question; it is effectively dead for this asset - trackie
  bottoms are a simple waistband + two leg tubes, exactly what a bind-pose
  height gradient handles well (confirmed by measurement: open waistband tube,
  closed ankle cuffs, no seam column).

## Calibration - measured bind-pose constants (2026-09-23)

Scope (the old Pants-Drop.md Open Item 2, now closed): measure
`trackie_bottoms` bind-pose vertical bounds (waistband, ankle), confirm the
FBX/local up-axis convention, and settle whether the waistband needs a clamp so
it doesn't slide through the hips mid-drop. Constants feed the
`pants_drop.shader` gradient `t`.

File: `trackie_bottoms.fbx` (binary FBX, 590864 bytes,
sha256 fd76d71e75841338271c0b7dbf3b54efb95229786a7d647357aa8210c9a69b36)

Provenance (how we know this is THE production mesh, not a decoy):
- Copied from
  `1-Engine-Builds/Dev/sbox-public/game/addons/citizen/Assets/models/citizen_clothes/trousers/TrackieBottoms/Models/trackie_bottoms.fbx`
  (identical file also in `Vanilla/sbox-public` - same sha256).
- The Steam install ships the pants COMPILED ONLY (see Target asset), but the
  compiled `trackie_bottoms_black.vmdl_c` REDI block records input dependency
  `models/citizen_clothes/trousers/trackiebottoms/models/trackie_bottoms.fbx` -
  the exact filename this copy has, and the engine source checkout carries its
  source.
- The checkout's `trackie_bottoms_black.vmdl` (kv3 source for the compiled
  model the game forces on every player via `GameManager._LongPants`) loads
  ONLY this FBX for all four LODs (`RenderMeshList` entries, import_filter
  exceptions `trackie_bottoms_LOD0..LOD3`, all with
  `import_translation [0,0,0]`, `import_rotation [0,0,0]`, `import_scale 1.0`).
  No transforms are applied before compile, so FBX bind-pose coordinates map
  1:1 (mod the compile-time scale) to what the shader sees in
  `i.vPositionOs`... minus the scale below.
- Caveat: the checkout's `trackie_bottoms_black.vmdl_c` differs byte-wise from
  the Steam one (rebuilt locally), and its FBX FileCRC is a Source-internal
  CRC that could not be reproduced with plain zlib crc32 - matching by filename
  + vmdl references is the grounding here, not a checksum. If in-engine
  measurement ever disagrees with these numbers, suspect asset drift and
  re-extract.

Units / scale:
- Source FBX are authored in real-world CENTIMETERS (same convention as the
  citizen body export - see 2-Projects/Citizen-Model-Export/README.md).
- The vmdl's `ModelModifier_ScaleAndMirror` applies `scale = 0.3937`
  (cm -> engine inches) at COMPILE time only; `import_scale` stays 1.0.
- IMPORTANT for shader constants: `i.vPositionOs` in the material VS is the
  COMPILED model's object space, i.e. already scaled by 0.3937. Measure in cm
  in Blender, then multiply the constants by 0.3937 (or express the gradient
  as normalized 0..1 between waistband and ankle so the scale cancels out).

Mesh contents:
- Meshes named `trackie_bottoms_LOD0` (full-res, measure this one) through
  `_LOD3` (decimated) share the file.
- Skeleton matches the citizen skeleton (pants skin to leg/pelvis bones).
- Material assignment points at `m_trackies.vmat` remapped to
  `trackie_bottoms_black.vmat` (the shader drop-in target uses
  `shaders/complex.shader`).

### MEASURED (headless Blender 5.2.2 snap, scripts in ~/.hermes/profiles/hermes-sbox/cache/scratch/pants_measure{,2,3}.py)

FBX authoring convention: Y-UP (cm). FBX local coords of LOD0 (1225 verts,
2422 polys, no shape keys) span X=[-19.30,19.26] Y=[11.42,95.78]
Z=[-14.34,22.19]; Blender's importer applies the standard up-axis fix
(local +Y -> world +Z, local +Z -> world -Y) plus cm->m (0.01) at
global_scale=1. The engine's model compiler does the same Y-up->Z-up
conversion, so `i.vPositionOs` in the material VS is Z-up - the old design
notes' "assumed Z, weak evidence `trunk_bending.hlsl:10`" guess is right for
ENGINE space, wrong for the raw FBX (don't bake a Y-up constant into the
shader).

Heights, raw FBX cm (local Y == engine Z before scale):
- Waistband top (max): 95.78 cm  -> engine inches: 95.78 * 0.3937 = 37.71
- Crotch junction (two leg tubes merge into one, centerline verts appear):
  ~67 cm -> 26.4 in
- Ankle cuff bottom (min, both legs): 11.42 cm -> 4.50 in
  (cuffs sit 11.4cm above the model's ground/origin, not at 0)
- Full drop travel waistband->ankles: 84.36 cm -> 33.21 in

Pelvis bone rest head at local (0, 78.93, 3.22) - waistband sits ~17cm ABOVE
pelvis (high-waisted on the sausage body). Clamp question (was part of open
item 2): a linear slide with `g_flMaxDropDistance` > ~29cm (waistband->crotch)
pushes the band below the crotch split; that is the natural ceiling for "fully
down bunched at ankles" unless the gradient gets a per-leg falloff.

Mesh structure answers:
- Origin at ground/feet level (mesh hangs at Y=11..96cm), NOT pelvis-centered.
- Waistband is an OPEN tube: 20 boundary (loose) edges, all at the top rim
  (~93-96cm), elliptical ring ~37cm x 35cm, ~52 verts near the top. No
  duplicated seam column at the waistband - safe to key `t` off local height.
- Ankle cuffs are closed (no boundary edges down there).
- LOD0-3 share the file (objects `trackie_bottoms_LOD0..LOD3`; LOD3 42 verts).
- Bound bones (vertex groups): pelvis, spine_0, spine_1, leg_upper_L/R(+twist,
  knee helpers), leg_lower_L/R(+twist), ankle_L/R.

Shader constants (prefer normalized form - scale cancels):
- t = (vPositionOs.z - Z_ANKLE) / (Z_WAIST - Z_ANKLE) clamped 0..1 with
  engine-space Z_ANKLE = 4.50, Z_WAIST = 37.71 inches (or cm equivalents
  11.42 / 95.78 if you divide out the 0.3937 yourself - verify which one
  vPositionOs actually carries with a quick in-engine probe, Open Item 6).
- g_flMaxDropDistance v0: ~29-33 in engine units is the geometric range;
  tune visually.

Blender MCP status (session 2026-09-23): stdio server registered in Hermes
profile hermes-sbox as `blender` (uv --directory
~/Applications/MCPs/blender_mcp/mcp run blender-mcp, 26 tools enabled);
the Blender-side addon (lab.blender.org extension, installed at
~/.config/blender/5.2/extensions/lab_blender_org/mcp) listens on
127.0.0.1:9876. Tools load in NEW Hermes sessions only.

## Delivery pattern (proven in this project)

Follow `obstruction_fade.shader` + `CCCamera.UpdateObstructionFadeTriangles`
(documented in the sbox-shader-authoring skill, runtime-per-object-effects
reference):

1. Custom `.shader` = drop-in replacement for `shaders/complex.shader`
   (vr_shared_standard family), declaring
   `float g_flPantsDrop < Attribute("PantsDrop"); Default(0.0); >;`
   (+ drop-axis attributes - axis question resolved, see Open Items).
   Forward-only is NOT required here: unlike a dissolve, a displacement must
   ALSO apply in the depth/shadow pass, so keep the shared `MainVs` for all
   modes (MainVs compiles once for Forward/Depth, verified in the template).
2. Per-player C# controller: `material.CreateCopy()` -> swap `.Shader` ->
   `renderer.Materials.SetOverride(slot, copy)`, cached in a dictionary keyed
   by source material. Build the copy from the EFFECTIVE material
   (`HasOverride(i) ? GetOverride(i) : GetOriginal(i)`), never `GetOriginal`.
3. Push `PantsDrop` (and axis) every frame while active via
   `renderer.SceneObject.Attributes.Set(...)`. Per-instance by construction -
   every player wears the same shared pants material, so `Material.Set` is
   banned here (would couple all players to one drop value).
   `Renderer.Attributes` is public (`Renderer.cs:43`, engine source).
4. Target renderer: the clothing's OWN child `SkinnedModelRenderer` (created by
   `ClothingContainer.Apply`, `ClothingContainer.Dressing.cs:226-252`, tagged
   `clothing`, `BoneMergeTarget = body`), found by walking the body renderer's
   children. NOT the body renderer.

## HideBody finding (RESOLVED - bodygroup toggle, do not own the .clothing)

Problem: with pants (or boxers) worn, the citizen's leg meshes are hidden, so
pants slid to the ankles would reveal a gap, not legs.

Mechanism, verified in engine source (`1-Engine-Builds/Dev/sbox-public`,
matches shipped managed layer):

- `Clothing.HideBody` is a public `[Flags] BodyGroups` property
  (`Game/Avatar/Clothing.cs:213`), `"HideBody": "Legs"` in the .clothing JSON.
  Set on BOTH trackie_bottoms_black.clothing AND heart_pattern_boxers.clothing.
- `ClothingContainer.GetBodyGroups` ORs HideBody across ALL worn items
  (`Game/Avatar/ClothingContainer.cs:211`) and applies them via
  `body.SetBodyGroup(name, HiddenChoice(...))`
  (`ClothingContainer.Dressing.cs:257-262`).
- `HiddenChoice` = last choice index = the empty-mesh choice
  (`ClothingContainer.cs:223-224`). `citizen_bodygrouplist.vmdl_prefab`
  (citizen addon): Legs choice 0 = Legs_LOD0-3 meshes, choice 1 = empty.

Decision: leave both .clothing files unmodified; toggle at runtime with
public API `ModelRenderer.SetBodyGroup("Legs", 0/1)` (game-API surface,
`Sandbox.Engine.xml` member `M:Sandbox.ModelRenderer.SetBodyGroup`).
Reasons NOT to own the file / mutate `HideBody`:

1. The mask is OR'd across all items - zeroing it on the pants copy would do
   nothing while the boxers (also Legs-hiding) are worn, which is always.
2. `Clothing.HideBody`'s setter mutates the shared Resource asset (global side
   effect, same bug class as shared `Material.Set`) and only takes effect on a
   full re-`Apply`. Wrong tool for a per-player runtime toggle.

Integration rules:

- Drop start: `SetBodyGroup("Legs", 0)` on the body renderer. Pants fully up:
  back to the hidden choice. Compute the hidden index as choices.Count - 1
  (like the engine) rather than hardcoding 1.
- Any re-dress (`ClothingContainer.Apply`, incl.
  `Extensions.ApplyClothingOnlyAsync` used at bars) runs `Reset` (restores
  default bodygroups, DESTROYS clothing children) then re-hides Legs and
  recreates the clothing renderers fresh. The pants-drop controller must
  re-acquire the pants renderer, re-apply override + attributes, and re-assert
  Legs visibility AFTER any re-dress if the player is mid-drop.
- Bodygroup + drop-amount are per-client visuals: the same replicated trigger
  that starts the drop tween must flip the bodygroup on every client
  (see networking Open Item).
- Bone-merge note: `MergeDescendants` sets the clothing SceneModel's Transform
  to the BODY's (`SkinnedModelRenderer.cs:564-603`), so clothing world-space
  positions live in the body's world frame - relevant to keying the drop off
  the mesh's own Z-up object space (measured above) rather than any assumed
  world axis.

## Knockdown: hide worn pants + flying prefab (decided 2026-09-22)

The old open item 1 (drop axis breaking under ragdoll poses) is MOOT: during
KnockedDown, the worn pants are NOT rendered at all. ecs spawns a separate
pants prefab that flies off the character (physics/visual prop, its own
concern - not part of this shader system). The worn-pants requirement during
knockdown is only: make sure they're hidden.

Implementation notes for the hide:

- The pants render via their own clothing child GameObject (tag `clothing`,
  SkinnedModelRenderer, BoneMergeTarget = body). Hiding = disable that
  GameObject (or the renderer) - no bodygroup trickery needed, and it must be
  done in addition to (not instead of) whatever the drop shader state is:
  a hidden body's Legs group is irrelevant if the pants object itself is off.
- CRITICAL interaction: the heart boxers ALSO carry `"HideBody": "Legs"`, and
  the citizen Legs bodygroup is all-or-nothing (Legs_LOD0-3 is one mesh, no
  partial choice). Hiding the worn pants while Legs stays hidden = floating
  boxers with no legs under them. The knockdown hide must flip Legs back ON
  (`SetBodyGroup("Legs", 0)`) at the same moment the pants renderer is
  disabled, and the restore must flip it back off (only if still fully
  dressed - i.e. driven by the pants state, not by knockdown alone).
- Restore path: on knockdown recovery / pants-pulled-up, re-enable the pants
  renderer, and re-assert the Legs bodygroup + material override + PantsDrop
  attribute if a re-dress happened in between (Reset destroys clothing
  children - see HideBody integration rules above).
- These are per-client visuals; the hide/show must be driven by the same
  replicated knockdown + pants state every client already needs (item 4),
  not decided locally.
- The flying-pants prefab spawn point should read the same state so it only
  spawns when pants are actually down (hit-at-ankles trip per Design.md),
  not on every knockdown - confirm against the state machine when it exists.

## Open items (triaged with ecs 2026-09-22; item 2 closed 2026-09-23)

1. ~~Drop axis under ragdoll~~ RESOLVED by decision: pants are hidden during
   knockdown and a flying prefab replaces them (section above). The shader
   drop effect only ever plays on an upright/running character, where
   body-relative vs world-up barely differ; start with the simplest axis and
   revisit only if the CC's lean/roll at high drunkenness makes it look wrong.
2. ~~Calibration measurement~~ RESOLVED (2026-09-23) - all numbers are in the
   Calibration section above. History of this item (was: "ecs is looking into
   it; extract `trackie_bottoms_black` to FBX via the sbox-asset-extraction
   skill, measure bind-pose vertical bounds, CONFIRM the local up-axis
   convention"): extraction done via the engine source checkout instead
   (Steam install has no FBX source); the assumed Z-up convention was only
   weakly evidenced (`trunk_bending.hlsl:10`) and measurement corrected it -
   raw FBX is Y-up, engine `i.vPositionOs` is Z-up; the waistband-clamp
   question was settled at ~29cm (waistband->crotch) as the geometric ceiling.
3. Normals/shear correction - ACCEPTED as scoped: defer until seen in-engine;
   if the falloff band lights wrong, add the analytic-derivative correction.
4. Networking / pants state machine - DONE (2026-09-23, ecs spec). Lives
   INSIDE DrunkCC: `[Sync] PantsState CurrentPantsState` (Up/Down), owner-only
   `SetPantsState()` writer; only the STATE travels over the wire, proxies read
   it for visuals (drop tween, Legs bodygroup flip, knockdown hide/show,
   flying-prefab spawn). Movement effects owner-only in OwnerHandleRunningHelper:
   `_SpeedPantsDownDisabler` (0-100%, scales the velocity ceiling AND actively
   strips forward velocity above it - the sphere coasts frictionlessly; 100
   pins forward speed at zero and, per the steer-while-moving-forward model,
   turning with it - deterministically, via a `pantsSpeedScale > 0f` gate
   instead of reading the float-noise residual), `_RotationPantsDownIncreaser`
   (FRACTION not percent, no cap, default 1.0 = doubled turn rate; multiplies
   the target yaw rate), `_JumpPantsDownDisabler` (0-100%, scales _JumpForce;
   100 ignores the jump press entirely - no force, no anim trigger, no gate
   latch). CONTROL WIRED: hold Shift = Up, released = Down (Design #13-#16) -
   `Input.Down("Run")` polled every owner tick in OnFixedUpdate:412, below the
   IsProxy gate, so it tracks the hold in Running AND KnockedDown. GOTCHA:
   the action is "Run" (Shift is its KeyboardCode); `Input.Down("Shift")`
   would warn + always read false (docs corrected). Knockdown ownership
   gating (ecs): OnFixedUpdate IsProxy gate + Owner* methods + [Rpc.Broadcast]
   RagdollifyHelper - VERIFIED correct by adversarial review, see items 8-9
   for what that review found still open. All compiled via CodeTestPortable
   gate; playtest pending (defaults need tuning: speed disabler = 0).
5. Override collision with CCCamera obstruction fade - NOT A PRIORITY
   (parked). Revisit if in-engine testing shows the two systems fighting over
   the pants material slot.
6. Verify the compute-skin inference (RenderDoc capture or in-editor probe) -
   TBD. Low risk: the design works under either skin path. Same probe settles
   which space `i.vPositionOs` carries for the compiled pants (cm vs engine
   inches - see Shader constants above).
7. v1 polish (out of v0): fold/bunch profile instead of linear slide, jiggle
   on stop, wider "pooled" silhouette at ankles. Only matters again if a
   second pants model is introduced.
8. FIXED (2026-09-23) - camera hijack: DrunkCC.OnUpdate was ungated, so EVERY
   player's DrunkCC incl. remote proxies wrote `_ccCamera.UseAltTargets`/
   `StayBehind` on the one scene-wide cccamera - any remote knockdown flipped
   YOUR camera. Fix applied: a `bool driveCamera = !IsProxy && _ccCamera != null`
   guard wraps the camera writes in both switch branches; the animgraph writes
   (MoveStyle/move_style/move_x) stay ungated because proxies need them to
   blend the remote body. Also fixes the unconditional _ccCamera deref (NRE if
   the tag is missing). Compiles clean; needs a 2-client playtest to confirm.
9. FIXED (2026-09-23) - host dresses remote players in the HOST's clothes:
   OnSpawnHelper broadcast DressPlayerHelper BEFORE its IsProxy gate, so the
   host (running OnStart for every remote prefab with Connection.Local = host)
   dressed remote bodies in the host's account clothing, racing the owner's
   broadcast. Fix applied at TWO layers, because a [Rpc.Broadcast] SENDS on any
   local call before permission checks (Rpc.InstanceRpc.cs:283-286 ->
   SendInstanceRpc:331-335): (a) the OnStart CALL SITE is now gated
   `if (!IsProxy) OnSpawnHelper(...)` so a proxy never broadcasts its own
   connection (without this, the owner would receive a proxy's broadcast and
   dress itself in the PROXY's clothes - the body-gate alone does NOT stop
   that); (b) the IsProxy gate moved to the TOP of OnSpawnHelper's body so
   proxy-side broadcast executions don't re-dress/re-register. Verified safe:
   OnStart is deferred to an update tick (Component.Update.cs:53/70), which
   runs after the spawn window clears IsProxy (NetworkObject.cs:149-150), so
   IsProxy is correct by OnStart time. Compiles clean; needs a 2-client
   playtest to confirm outfits are per-player correct.
   Still noted, no fix planned (nits): RagdollifyHelper's ApplyVelocity reads
   receiver-local velocity inside the broadcast (remote ragdolls crumple
   instead of flying with the owner's momentum - fix would pass velocity as a
   parameter); the IsProxy check in ResetFromKnockdownHelper (:695) is dead
   code (harmless - RagdollifyHelper(true) at :694 correctly precedes it);
   _hasCheckedHistObstacle (:138) is assigned-never-read (the build gate's one
   CS0414 warning).
   NOTE (2026-09-23): items 8 and 9 are real multiplayer defects being
   actively worked RIGHT NOW by a separate agent; treat their text as
   in-progress elsewhere, not as this document's to change.

## History

Design originated in the Sep 19 "pants-shader" working session (engine shader
pipeline trace + v0 plan), hardened by a critical subagent review against
engine source (findings: world-up axis defect, normals/shear, shared-material
coupling, networking gap, calibration placeholders), then narrowed by the
Sep 22 decisions recorded above: forced single pants asset kills
calibration-as-a-system and shape-blindness; HideBody resolved via runtime
bodygroup toggle instead of owning the .clothing file.

Sep 23 implementation session: knockdown drop replaced by hide-worn-pants +
flying-prefab (item 1 retired). Pants state machine BUILT inside DrunkCC per
ecs spec (item 4): binary Up/Down, owner-authoritative [Sync] state, three
Down-modifiers (speed / turn / jump), Shift-hold control via Input.Down("Run").
Two adversarial subagent reviews run against engine source: the first hardened
the modifier math (found the X=100 yaw-gate flicker - now a deterministic
pantsSpeedScale gate); the second verified ecs's ragdoll/knockdown ownership
gating as correct and complete, and surfaced two real multiplayer-only defects
riding along in the same commit (items 8-9: ungated camera writes, host-dresses-
remote-players). Both were FIXED the same session - item 9 needed a call-site
gate, not just a body gate, because [Rpc.Broadcast] sends before any permission
check (Rpc.InstanceRpc.cs:283-286). All work compiles clean via the
CodeTestPortable gate; nothing playtested yet, and the Down-modifier defaults
still need tuning.

Sep 23 calibration session (this directory): extracted the production FBX from
the engine source checkout, verified the provenance chain (vmdl_c REDI
dependency + vmdl RenderMeshList), and measured LOD0 bind-pose bounds in
headless Blender - waistband/crotch/ankle heights, mesh structure, normalized
shader constants - closing old open item 2 and correcting its assumed-Z
weakness (raw FBX Y-up, engine object space Z-up). Same day: the two
documents merged - `../Pants-Drop.md`'s design sections folded in verbatim
with stale calibration/axis claims marked as corrected history - and this file
became the single canonical pants-drop document.
