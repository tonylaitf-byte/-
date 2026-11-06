# Live arm body avoidance

Implemented on 2026-10-24. This is a first-stage geometric guard for live mapping,
not a cloth simulation or a full physical collision system.

## Scope

- `PuppetBoneTest` keeps the existing input mapping, arm rest pose, angle limits,
  head control and spring parameters. `avoidBody` is enabled by default.
- A torso capsule covers the hip-to-chest region. A second capsule covers the head.
  Dimensions follow shoulder spacing, so the two character sizes use the same ratios.
- Both upper-arm and forearm segments and the wrist are tested after IK angle clamps.
  The first 12% of the upper arm is excluded to allow the shoulder attachment.
- The existing left-hand `Mesh1.0` sword is detected only under the controlled hand.
  Its local mesh bounds define a conservative blade capsule. No mesh vertex readback,
  physics collision layers or external dependency is needed in WebGL.
- If the requested pose intersects a protection volume, the solver searches toward
  the supported outward sleeve pose and refines the smallest correction found along
  that path. It does not teleport to a different authored animation.
- Correction releases exponentially (default 0.12 seconds). The corrected pose is
  checked again because extra correction is not necessarily safer for a long sword.
- Only live-controlled arms are changed. Animation-owned arms retain their existing
  ownership. The original gesture clips are not edited.

## Diagnostics and tuning

Inspector section: `Live Arm Body Avoidance`.

- `avoidBody`: disable for a direct comparison with previous live IK.
- `bodyAvoidanceRadius`: torso radius / shoulder span (default 0.30).
- `headAvoidanceRadius`: head radius / shoulder span (default 0.26).
- `avoidancePadding`: additional clearance / shoulder span (default 0.025).
- `avoidanceReleaseTime`: time constant for releasing correction, not an input delay.
- `avoidanceAngularSpeed`: joint speed limit while avoiding or settling (180 deg/s).
- `showAvoidanceGizmos`: selected-object protection-volume endpoint guides in Scene view.

Runtime diagnostics: `LeftAvoidanceBlend`, `RightAvoidanceBlend`,
`LeftBodyPenetration`, `RightBodyPenetration`, `HasSwordAvoidance`.
Penetration is measured in world units. A residual is reported, not hidden, if the
bounded search cannot find a feasible pose. `CurrentLeftHandTarget` and
`CurrentRightHandTarget` now expose the corrected IK targets.

## Validation

`PuppetAvoidanceValidation.Run` opens SoloMapping, DuetMapping, SoloHybrid and
DuetHybrid in the Unity Editor without a real camera and evaluates six actual rigs.
The test does not save modified scene assets.

- 1,800 synthetic frames per rig, 10,800 total, with independent arms, palm motion,
  center-forward gestures, body rotation/lean and head motion.
- Identical-state original IK comparison with the avoidance disabled, restoring
  correction history after each baseline sample.
- 36 direct intrusion probes: both wrist targets at the abdomen, chest and head.
- Checks for unchanged neutral pose, head-only wrist independence, correction release,
  animation-owned-arm isolation and finite geometry.
- Segment-distance cases include parallel, crossing and zero-length segments.

Latest results are in `reports/arm-avoidance/Validation.json`.
All six rigs passed. Protected synthetic frames had no residual above 0.0002 times
shoulder span. The four sword-bearing rig instances each had 1,001 baseline frames
intersecting the conservative protection volumes and zero above tolerance after
correction. This is a proxy-overlap measure, not a measured mesh penetration rate.

The Unity 6000.5.3f1 WebGL rebuild succeeded. The rebuilt player also passed
`tests/hybrid_browser_validation.cjs` for both player counts and
`tests/browser_validation.cjs` for the four existing configurations, including
40 resets. These browser runs used a separate port 5087 test server and fake camera
frames. That server was stopped afterward; the existing port 5086 service was not
restarted and reported healthy with no camera open. The broader historical Editor
play-mode validation with its known Search-index error was not rerun or reclassified.

Set `AVOIDANCE_RENDER=1` when running the validation with graphics enabled to write
three original/protected PNG pairs. The diagnostic renderer bakes evaluated skin
poses because edit-mode GPU skinning may otherwise be stale. These are synthetic
pose snapshots, not proof of real-camera responsiveness or natural motion.

## Limits

### Continuity correction, 2026-10-24

The initial guard could change its selected solution abruptly. The final upper-arm
and forearm rotations now advance from their previous actual pose with a time-based
angular limit. Eight intermediate samples check arm/sword clearance; boundary
refinement stops progress at an unsafe transition. History resets when animation
owns an arm, protection is disabled, or the rig is rebound. Ordinary unconstrained
mapping is not filtered again. Targets remain desired IK targets, so the actual
wrist can temporarily lag them. This is not a global collision-free path planner;
it can hold at a blocked boundary, and moving body volumes can leave a residual.

A fresh graphical Play Mode audit is in `reports/avoidance-continuity/`. The same
60 Hz warrior sweep improved from 39.902 to 3.000 degrees maximum bone step and
from 0.4967 to 0.0416 shoulder spans maximum wrist step. Protected live cases had
zero proxy intersections and retained their endpoints. Head-only input caused no
wrist drift. `node tests/check_avoidance_continuity.cjs` checks these saved runtime
results against the preserved baseline; it does not launch Unity itself. The known
Editor Search-index exception remains recorded in the audit. These measurements
do not establish real-camera responsiveness or fix the restricted forward reach.

The continuity-fix WebGL rebuild completed successfully. The subsequent isolated
5087 browser check did NOT complete: its Node 24.15.0 process exited abnormally
(second attempt: -1073740791), without producing a fresh validation report. The
older passing browser results above must not be interpreted as verification of
this new build. The temporary test service was stopped afterward. No real camera
or screen interaction was used.

### Remaining limits

- No opposite-arm, opposite-hand, second-character, floor or scenery avoidance yet.
- No precise sleeves, hair, ornaments, cloth simulation or triangle-mesh collision.
- Pre-recorded gestures and animation-release blends are not collision-corrected.
- Frame poses are checked; continuous swept-volume collision between frames is not.
- Only the known rigid sword attached under the left hand is supported automatically.
  Other weapons and skinned props require explicitly defined proxy geometry.
- The conservative sword bounds can reduce some sweeping motion. Real input and
  multi-angle visual review are still needed to tune the clearance for performance.

Rebuild WebGL with `Build-IntegratedTheatre.ps1` after runtime code changes. Backend
hand detection, calibration, transport payloads and Docker configuration are unchanged.
