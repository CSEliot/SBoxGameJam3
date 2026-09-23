# Pants-Calibration - bind-pose measurement source for the pants-drop shader

Purpose: Pants-Drop.md Open Item 2 - measure `trackie_bottoms` bind-pose
vertical bounds (waistband Z, ankle Z), confirm the FBX local up-axis
convention, and settle whether the waistband needs a clamp so it doesn't slide
through the hips mid-drop. Constants feed the `pants_drop.shader` gradient `t`.

File: `trackie_bottoms.fbx` (binary FBX, 590864 bytes,
sha256 fd76d71e75841338271c0b7dbf3b54efb95229786a7d647357aa8210c9a69b36)

Provenance (how we know this is THE production mesh, not a decoy):
- Copied from
  `1-Engine-Builds/Dev/sbox-public/game/addons/citizen/Assets/models/citizen_clothes/trousers/TrackieBottoms/Models/trackie_bottoms.fbx`
  (identical file also in `Vanilla/sbox-public` - same sha256).
- The Steam install ships the pants COMPILED ONLY, but the compiled
  `trackie_bottoms_black.vmdl_c` REDI block records input dependency
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

What to record after measuring in Blender (fill in below or report back):
- Local up axis + handedness of the bind pose (assumed Z-up in FBX; convert to
  the engine convention Blender reports on import).
- Waistband top Z (center-front, or max over the ring).
- Ankle cuff bottom Z (per leg; crotch Z too - useful for the gradient's
  inflection if legs need independent falloff).
- Whether the mesh is centered on the pelvis origin or the world origin.
- Vertex count / whether LOD0 has a continuous tube (no seam vertices) at the
  waistband.
