# Current motion review

Date: 2026-10-24. Runtime source and authored animation assets were not changed.
This sentence describes the original review. The first finding was subsequently
fixed in runtime code; see the update below. The baseline reports are preserved.
The new `LivePuppetMotionAudit` Editor utility runs the current DuetHybrid scene
in Play Mode, evaluates synthetic controls at 60 Hz and captures actual rendered
poses. Both characters receive identical inputs. No physical camera is used.

## P1: Avoidance introduces a large discontinuity during smooth arm motion

Status: fixed for the reproduced live sweep. A fresh Play Mode run in
`reports/avoidance-continuity/` measured 3.000 degrees maximum step instead of
39.902, zero protected live proxy intersections, and unchanged final wrist poses.
The guard now rate-limits actual corrected joints and checks intermediate poses.
See `ARM_BODY_AVOIDANCE.md` for validation and limits. The remaining findings below
are not addressed by this fix.

Source: `unity/UnityPuppetTest/Assets/Scripts/PuppetBoneTest.cs:781-829`, especially
the candidate search and `Mathf.Max(required, released)` at line 813.

The `sweep` case uses smooth sine inputs for arm height and depth. With avoidance
off, the warrior's largest bone step is 0.982 degrees and wrist step is 0.0181
shoulder spans. With avoidance on, these rise to 39.902 degrees and 0.4967 shoulder
spans in a single 1/60-second simulation step. The musician's motion is unchanged.

The solver can select a different feasible correction abruptly. Correction is
immediate on entry; only its release is smoothed, and the final validity fallback
can also bypass that smoothing. The current collision-free-frame tests do not
check this temporal discontinuity. This is a regression from the avoidance layer.

Recommended correction: preserve continuity of the selected solution, constrain
the motion toward it and plan clearance before contact; ensure the final pose is
still checked. Merely adding another input smoother would leave this downstream
discontinuity intact.

## P1: The held sword severely limits forward reach

Sources: `PuppetBoneTest.cs:780` (outward fallback target) and
`PuppetBodyAvoidance.cs:33-42` (conservative sword proxy).

At the end of the same full-forward input:

| Character / arm | Avoidance off | Avoidance on |
| --- | ---: | ---: |
| Warrior left, holding sword | 0.9169 | 0.2258 |
| Warrior right | 0.9270 | 0.9270 |
| Musician left | 0.9091 | 0.9091 |

Values are the shoulder-to-wrist displacement projected onto character forward,
divided by arm length. They are not percentages of total hand travel.
The warrior's left-arm straight-line reach also drops from 0.9800 to 0.7629 of
arm length. The maximum correction blend reaches 0.8680.

The held sword follows the existing wrist orientation; the guard reacts by pulling
the entire arm toward its outward sleeve pose. The original forward pose places
the blade across the face/body protection volumes, so removing the guard alone
would restore that conflict. The mesh-bounds capsule may be conservative; its fit
needs measuring before attributing all interference to real blade contact.

Recommended correction: fit the sword proxy and define a forward-facing grip/wrist
orientation, then find a nearby reachable arm pose that retains the requested
forward direction. Avoid making the outward idle pose the only escape path.

Comparison: `reports/current-motion-audit/forward-original.png` and
`reports/current-motion-audit/forward-protected.png`. Panels run left to right,
top row then bottom row, at one-second simulation intervals.

## P2: ATTACK moves the gourd arm rather than the sword arm

Sources: `unity/UnityPuppetTest/Assets/IntegratedTheatre/Editor/HybridTheatreBuilder.cs:161`
and `unity/UnityPuppetTest/Assets/Editor/PuppetProjectBootstrap.cs:374`.

The hybrid ATTACK mask owns Torso and RightArm. The authored attack generator also
keys RightArm and RightForeArm. The current sword is attached to LeftHand and the
gourd to the other hand. In the rendered ATTACK sequence the gourd arm sweeps while
the sword arm remains in live mapping. For a sword-attack action, the clip and its
ownership mask must both be corrected; changing the mask alone is insufficient.

## Other observations

- Head-only motion while holding both arm inputs steady produced zero wrist
  displacement in both characters, with avoidance both enabled and disabled.
- DRINK intersected conservative protection volumes in 266 of 627 sampled warrior
  frames; the maximum proxy overlap was 0.0744 shoulder spans. The rendered sleeve
  obscures much of the face. This needs a separate animation/prop clearance review;
  proxy overlap alone does not establish exact mesh penetration.
- No release-phase proxy intersections were recorded in these five action cases.
  The code nevertheless composes arm animation after live IK, so coverage does not
  guarantee arbitrary release paths are collision-free.
- The audit completed with 22 result rows. The known Unity Editor Search database
  ArgumentOutOfRangeException is retained under `engineDiagnostics`; this is not a
  claim that the entire Editor run was error-free.

## Artifacts and limits

- `reports/current-motion-audit/Audit.json`: fresh current-version measurements.
- `reports/current-motion-audit/*.png`: six live-motion comparison sheets and five
  action sheets. Action sheets show the first five seconds and the final pose;
  they do not sample every frame or prove temporal smoothness by themselves.
- `Assets/IntegratedTheatre/Editor/LivePuppetMotionAudit.cs`: reproducible audit.

The findings concern the current Unity motion pipeline under synthetic input.
Real-camera landmark jitter, tracking delay and user comfort were not measured.
No runtime fix or WebGL rebuild was made as part of this review.
