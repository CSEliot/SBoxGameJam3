# Pants Drop - Shader-Based Design Notes

Status: DESIGN, nothing implemented yet. Last updated 2026-09-22 (open items
triaged with ecs - see each item's DECISION line and "Knockdown: hide + flying
prefab" section).

The obstacle-collision "pants drop" needs a VISUAL: the worn pants slide/bunch
down toward the ankles (then back up via R-mashing). This document records the
chosen technique, the engine findings it rests on (verified on disk, with
citations), the HideBody decision, and the remaining open items.

Related: `Architecture.md` "Pants Down" (clothing slot/layer plan),
`Design.md` (pants levels + gameplay), `Assets/shaders/obstruction_fade.shader`
(the in-project precedent for the rendering pattern used here).

## Target asset

- The pants are FORCED on every player: `GameManager._LongPants`
  (`Code/GameManager.cs:76`), already wired in `Assets/scenes/minimal.scene`
  to the engine addon asset
  `models/citizen_clothes/trousers/trackiebottoms/trackie_bottoms_black.clothing`.
  Assignment logic in GameManager is NOT yet implemented.
- On disk: `~/.local/share/Steam/steamapps/common/sbox/addons/citizen/Assets/
  models/citizen_clothes/trousers/TrackieBottoms/`. Ships COMPILED ONLY:
  `.vmdl_c`/`.vmat_c`/`.clothing_c` plus stray `.fbx.meta`/`.vmdl.meta` with no
  actual FBX/vmdl/vmat source. Consequence: the mesh cannot be edited or have a
  gradient baked into vertex data without re-authoring; calibration constants
  must be measured from an extracted copy (see Open Items).
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
  the ankles; constants calibrated once from this specific mesh.
- Garment-shape-blindness (pockets/belt loops sinking in "melting slabs") was
  the old open question; it is effectively dead for this asset - trackie
  bottoms are a simple waistband + two leg tubes, exactly what a bind-pose
  height gradient handles well.

## Delivery pattern (proven in this project)

Follow `obstruction_fade.shader` + `CCCamera.UpdateObstructionFadeTriangles`
(documented in the sbox-shader-authoring skill, runtime-per-object-effects
reference):

1. Custom `.shader` = drop-in replacement for `shaders/complex.shader`
   (vr_shared_standard family), declaring
   `float g_flPantsDrop < Attribute("PantsDrop"); Default(0.0); >;`
   (+ drop-axis attributes, see Open Items). Forward-only is NOT required here:
   unlike a dissolve, a displacement must ALSO apply in the depth/shadow pass,
   so keep the shared `MainVs` for all modes (MainVs compiles once for
   Forward/Depth, verified in the template).
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
  positions live in the body's world frame - relevant for the drop-axis item.

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

## Open items (triaged with ecs 2026-09-22)

1. ~~Drop axis under ragdoll~~ RESOLVED by decision: pants are hidden during
   knockdown and a flying prefab replaces them (section above). The shader
   drop effect only ever plays on an upright/running character, where
   body-relative vs world-up barely differ; start with the simplest axis and
   revisit only if the CC's lean/roll at high drunkenness makes it look wrong.
2. Calibration measurement - ecs is looking into it. Extract
   `trackie_bottoms_black` to FBX (sbox-asset-extraction skill), measure
   bind-pose vertical bounds (waistband Z, ankle Z), CONFIRM the local up-axis
   convention (assumed Z, only weak evidence: `trunk_bending.hlsl:10`), bake
   the constants. Also settles whether the waistband needs its own clamp so
   it doesn't slide through the hips mid-drop.
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
   TBD. Low risk: the design works under either skin path.
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
