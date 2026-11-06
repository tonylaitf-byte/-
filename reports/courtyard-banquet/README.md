# Blender banquet checkpoint

Date: 2026-11-02. Independent first-version static props and Unity integration.
This is authored 3D geometry, not an AI image used as a scene backdrop.

## Saved files

- Scene: `Assets/IntegratedTheatre/Scenes/CourtyardBanquetPreview.unity`.
- Editable Blender source: `ArtSources/CourtyardBanquet/courtyard_banquet_v1.blend`.
- Export: `Assets/Environment/CourtyardBanquet/courtyard_banquet_v1.fbx`.
- Portable textures: `Assets/Environment/CourtyardBanquet/Textures/`.
- Generator: `Tools/BuildCourtyardBanquet.py`, run by Blender 5.1.2 in background.
- Integration: `Assets/IntegratedTheatre/Editor/CourtyardBanquetPreview.cs`.

The Blender source retains logical prop groups. Its export flattens the grouping
but preserves named independent cup, ewer, jar and plate meshes. Static reed
courses, branches and other details are combined per prop; imported Unity geometry
has 28 mesh renderers, 50 material slots and 30,652 triangles. This reduces object
count but is not a measured WebGL frame-rate guarantee. Source/export triangle
counts differ because Unity removes degenerate lathe-center faces on import.

## Changes

Both houses move outward 0.55 world units relative to the approved layout blockout
(still inward 4.75 units relative to the original stage). Central architecture
retains the 1.45 width scale. A separate backdrop material repositions the moon,
with narrow night-sky gaps visible at the roof joints. The original camera and
character placement are retained for the main view.

The low wooden table, floor mat and two sitting mats replace sizing proxies.
Added independent wine vessels/cups, five food dishes, folded cloth, three cloth-
sealed storage jars with tray, two low stone lanterns and a small pine planter.
Materials are a first-pass stylized finish, with baked procedural wood/reed/linen/
ceramic textures and normal maps. No outside model/texture service or API key is
used. No historical reconstruction claim is made.

## Verification and limits

`Integration.json` records imported bounds, geometry counts and scene paths. The
integration rejects incorrect-scale/axis bounds, missing materials/error shaders,
unexpected colliders or more than 64 mesh renderers. The FBX hierarchy unit issue
found during import was corrected in the export, not hidden with arbitrary scaling.

`layout-front.png`, `layout-4x3.png` and `banquet-detail.png` are actual Unity graphical
Play Mode camera renders. All three were inspected. Main characters and banquet
remain framed; peripheral jars/planter are partly cropped at 4:3. No physical camera
or user screen control was used. The known Editor Search-index exception is retained
in `render-status.txt`; the capture routine completed successfully.

Original six production scenes, character control scripts, animation clips,
architecture FBX and production WebGL are unchanged. No production build,
new kneeling/drinking animation, full visual-polish pass or physical interaction was
performed. Props have no colliders; live-character avoidance/navigation around them
is not implemented or verified. Floor-lantern local lights are preview lighting;
the wider scene lighting/shadows still need a separate art pass.

This checkpoint is included in the 2026-11-02 art preview update on PR #1;
see `SCENE_ART_PROGRESS.md` for the sharing and review entry points.

Rendering used Unity 6000.5.3f1; the repository's 6000.5.9f1 ProjectVersion.txt is
restored afterward. The earlier CourtyardLayoutPreview scene is preserved for
comparison. Browser playback still uses the existing production WebGL.
