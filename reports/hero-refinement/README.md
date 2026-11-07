# Sword puppet material refinement

Date: 2026-11-02. First material-only refinement of the left sword character.

Scene: `Assets/IntegratedTheatre/Scenes/HeroRefinementPreview.unity`, copied from
the independent banquet preview. Source meshes, bone transforms, skin weights,
animations, controller scripts and the production WebGL remain unchanged. The
right musician and courtyard keep their earlier materials and lighting.

## Surface changes

The source FBX has nine body-material regions plus sword and jug materials. They
contain flat colors and no albedo/normal textures. All had metallic set to zero.
Eleven independent preview materials now distinguish subdued blue fabric, skin,
white trim/hair, gold thread, crown metal, aged gold, steel and red lacquer.
Cloth gloss is reduced; metal profiles use metallic response and varied roughness.
Skin is less orange/glossy, and the sword no longer shares a painted-plastic response.

`RefinedPuppetSurface.shader` adds restrained UV-anchored micro variation, with
derivative-based distance fading. It does not paint a new texture atlas or create
new embroidery, hair strands, face geometry or skin pores at sculpted resolution.
The existing white hair/cuff areas share a source material and remain one region.

`HeroSurfaceRefinement` is opt-in on this scene only. It runs after the existing
automatic skin/color passes, caches their actual baseline materials, and allows
reversible before/after comparison without editing shared FBX material assets.

## Actual verification

`HeroRefinementPreview.Create` generates matched before/after stage, full-costume
and head views in graphical Unity Play Mode. Camera, light and pose are identical
within each pair; only material assignments change. Background coverage is fitted
temporarily for the square close-up renders, then restored. Shader compilation
was checked for the current Editor graphics target, not every WebGL device.

`Validation.json` confirms the same mesh references, bone bindings and root-bone
references, pose-preserving material swaps, and visible arm/head response over 240
synthetic frames. Arms and head are exercised separately to respect the existing
head-only input gate. No tracking camera or user-screen interaction was used.
The known Editor Search-index exception remains in engineDiagnostics; these tests
do not assert a globally clean Editor, real-hand latency or complete action coverage.

## Remaining source limitations

Close-ups still reveal faceted geometry and irregular material boundaries at the
face, hair, crown and costume edges. This pass does not repair those defects.
Improving silhouettes/face structure requires a separately scoped mesh/region
cleanup, followed by skinning and animation checks. Material changes alone do
not reproduce the AI concept image's detailed character model.

The original scenes and production website were not replaced or rebuilt. This
checkpoint is included in the 2026-11-02 art preview update on PR #1. Rendering used Unity
6000.5.3f1; ProjectVersion.txt is restored to the repository's 6000.5.9f1 afterward.
