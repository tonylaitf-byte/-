# Courtyard layout checkpoint

Date: 2026-11-02. Scope: spatial blockout only, pending user approval.

Scene: `Assets/IntegratedTheatre/Scenes/CourtyardLayoutPreview.unity`.
This independent copy of DuetHybrid preserves the original six performance scenes,
control scripts, architecture FBX and existing WebGL output. It is not registered
as a production build scene. This checkpoint is included in the 2026-11-02 art
preview update on PR #1; see `SCENE_ART_PROGRESS.md` for current sharing status.

Changes: both side houses moved inward 5.3 world units; central gate and inner hall
width scaled 1.45x. Existing rear lantern groups move inward 3.5 units. Side-wall
proxies move outward 0.35 units to expose existing columns/lattice. A separate
backdrop material reframes the moon without altering the original shared material.
The original camera and character root positions are retained.

Plain geometry proxies mark a 2.15 x 1.30 low table, a 4.25 x 1.80 main mat and two
0.75 x 1.25 kneeling mats. They use flat color and contain no collision components.
No wine vessels, dishes, food, weave detail, bonsai or other decorations are made.
No new kneeling, drinking or interaction animation is implemented. Placement is a
visual proposal, not a tested collision/navigation guarantee for every live gesture.

`layout-front.png` (1600 x 900) and `layout-4x3.png` (1200 x 900) are actual graphical
Unity Play Mode camera renders, not AI images. Synthetic neutral controls settle
the two rigs; no physical camera or screen interaction is used. Both frames were
inspected for framing and nonblank output. The known Editor Search-index exception
is preserved in `render-status.txt`; the capture routine itself completed.

Rendering used Unity 6000.5.3f1. ProjectVersion.txt is restored to the repository's
6000.5.9f1 after rendering. Open the independent preview scene to inspect the layout;
the browser still serves the unchanged production WebGL stage.
