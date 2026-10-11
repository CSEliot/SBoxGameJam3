# Fake Light Beam

A fake volumetric light beam for s&box. It is a translucent, additive cone mesh
drawn along a SpotLight's aim so the beam reads as light in air. It is not real
volumetric fog: it costs one small translucent draw per beam and gives you direct
art control over the look.

The beam renders in the editor scene view as well as in play, because the
component runs with Component.ExecuteInEditor.

## What is in the box

- Code/FakeLightBeam.cs     the component (namespace Sandbox)
- Assets/fakelightbeam/fake_light_beam.shader   the additive cone shader
- Assets/fakelightbeam/fake_light_beam.prefab   a drop-in: one GameObject with a
  SpotLight and a FakeLightBeam already wired to it

## Install

Copy the whole fakelightbeam folder into your project's Libraries/ folder:

    <YourProject>/Libraries/fakelightbeam/

Restart the editor. Libraries are scanned at project load, so a newly copied
library is not picked up until the editor reloads. The library's Assets folder is
mounted at the content root next to your own Assets, which is why every shipped
file lives under the fakelightbeam/ subfolder. The shader resource path is
therefore exactly "fakelightbeam/fake_light_beam.shader".

## Use

Two ways:

1. Drop Libraries/fakelightbeam/Assets/fakelightbeam/fake_light_beam.prefab into
   your scene. It already has a SpotLight and a FakeLightBeam wired to it; rotate
   the GameObject to aim it (the light shines along its +X).
2. Or add a FakeLightBeam component next to an existing SpotLight on the same
   GameObject. Leave its Light property empty and it auto-resolves the SpotLight
   on the same GameObject, or point Light at a specific SpotLight.

The component owns its own SceneObject. It creates no child GameObjects, so there
is nothing to save and nothing to orphan on a hotload.

## Tunables (on the component)

- Light            the SpotLight to follow. Empty = the SpotLight on this GameObject.
- MaxLength        longest the beam can be, in world units. Default 400.
- AngleScale       fraction of the light's outer cone half-angle used for the
                   visible beam, 0 to 1. Default 0.85.
- Intensity        overall beam brightness. Default 0.15.
- ClipToWorld      trace to the first world hit and stop the beam there. Default on.
                   The trace hits physics colliders only, so geometry with no
                   collider does not stop the beam.
- ClipOvershoot    extra length past the trace hit so the depth feather can blend
                   into the surface. Default 16.
- TraceIgnore      hierarchy the clip trace ignores. Empty = this GameObject. Set it
                   to a vehicle root when the beam is on a car so the beam does not
                   clip against its own body.

## Material-editor Beam sliders (on the shader)

These are on the material, not the component, and apply to every beam sharing the
material. They are under the "Beam" group in the material editor:

- EdgePower        how sharply the beam fades at its silhouette.
- LengthPower      how fast the beam fades along its length.
- DepthFeather     softness of the intersection with scene geometry.
- CameraFadeStart  distance at which a beam starts to fade as the camera nears it.
- CameraFadeEnd    distance at which a beam is fully faded for a nearby camera.

## Caveat: additive light and daylight

The beam is additive, so it only shows against a darker background. On a bright
daylight scene it adds almost nothing, and that is physically correct: real beams
are invisible in daylight. Do not fight it with a high intensity. Darken the scene
to dusk or night and the beam reads. Bloom on the camera helps.

## Limitations

- First open without a compiled shader: the library ships
  fake_light_beam.shader_c. If it is missing, the editor compiles the shader while
  the project loads, and beams created during that load can show a red error
  material for the rest of the session. Reopen the editor once and it is fine.

From the design research (section 8, known limitations and gotchas):

- First-person flashlight: the camera sits at the apex, so every view ray runs along
  the cone wall and the edge term goes to about zero. The beam nearly disappears for
  the light's holder. That is usually fine, because the holder sees the lit spot. To
  hide the cone for the holder only, gate the driver on the project's "is this the
  local player" check. Checking !IsProxy is the wrong test for that.
- Camera passing through someone else's beam: the camera fade distances stop a
  full-screen flash. Tune them against the camera's near distance.
- No shadows inside the beam: an object standing in the cone does not cut a dark
  shaft through it. Only real volumetric fog with a shadowed light does that.
- Bright scenes: additive on a bright background adds almost nothing. Expected.
- Overlays: the AfterUI layer has no scene depth, so keep the beam in the normal
  world layer or the depth feather breaks.
- Many beams: each is one small translucent draw with no sorting issues, and
  additive blending does not depend on draw order. Overdraw is the cost, so a
  camera looking straight down a long wide cone fills many pixels. The camera fade
  limits the worst case.

For the real thing, s&box also has VolumetricFogVolume plus the per-light FogMode
and FogStrength. It is correct from every angle including inside the beam and it
casts shadow shafts, at the cost of whole-scene cost and less per-beam art control.
The two approaches combine: low global fog for mood, fake cones for hero beams.
