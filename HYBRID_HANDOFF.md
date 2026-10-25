# Hybrid animation handoff

Updated 2026-10-25. Only the third (hybrid) mode is changed. Gesture-only mode,
mapping-only mode, tracking, calibration, clips and ownership masks are unchanged.

## Behavior

`HybridPuppetActor` already crossfaded from the last visible pose into animation
and back toward live mapping. Previously the release ended at a fixed duration,
even if fresh hand input was still moving the destination at the boundary.

- Entry retains the authored easing and animation smoothing. Owned joints have a
  180 degrees/second handoff limit until the entry target is caught; ordinary clip
  playback is not globally retimed or slowed by this addition.
- Release starts at the actual last visible pose, retains the existing bounded
  velocity carry, and continuously follows the latest mapping pose, not an old
  coordinate captured when the action started.
- The same speed limit applies to owned joints throughout release. The nominal
  blend-out time is now a minimum fade duration, not a hard ownership cutoff.
- Ownership returns after all owned joints track within 0.05 degrees for 0.15
  seconds while moving below 75% of the handoff speed. This guards against the
  mapping spring still accelerating at the end of the fade.
- Animation-unowned joints remain live. Cancellation, tracking loss and identity
  changes use the same release path. Reset/StopImmediately remains immediate.

`handoffAngularSpeed` is editable on the hybrid actor, default 180 degrees/second.
Large differences can prolong the handoff; persistently fast input can keep it in
Release. Existing busy-event behavior remains in force until it settles. There is
no arbitrary timeout that teleports the rig to the live pose. This does not add
body/prop collision handling to authored animations.

## Verification

`HybridHandoffValidation.Run` performs real Unity Play Mode evaluations using
synthetic controls, without physical-camera or screen interaction. The 60 cases
cover two actors, five actions, three frame rates and natural/cancelled releases.
Each case reverses the hand target just before the nominal release timeout.

| FPS | Cases | Maximum first mapping-frame rotation | Longest measured release |
| --- | ---: | ---: | ---: |
| 30 | 20 | 0.86 degrees | 1.20 seconds |
| 60 | 20 | 0.52 degrees | 1.22 seconds |
| 120 | 20 | 0.25 degrees | 1.25 seconds |

All cases completed, with entry/release joint speed within 180 degrees/second plus
quaternion measurement tolerance. Results: `reports/hybrid-handoff/Validation.json`.
These are joint-local measurements, not proof of subjective naturalness or limits
on world-space wrist speed under arbitrary parent movement.

The existing `HybridTheatreValidation` functional assertions also completed:
solo/duet, unmasked-arm mapping, latest target return, busy-event dropping, pause,
epoch release, musician completion and masks. Its overall `passed` remains false
because the known Editor Search database exception is still counted; see
`unity/UnityPuppetTest/IntegrationReports/HybridValidation.json` and
`HybridTheatreValidation.log`. The focused handoff report preserves the same engine
diagnostic separately. No clean-Editor claim is made.

WebGL rebuilt successfully. `tests/hybrid_browser_validation.cjs` passed with the
bundled Node 24.22.1 runtime on an isolated port 5087 service: solo and duet hybrid,
live unmasked input during animation, latest-target return, event deduplication,
busy-event dropping, owner isolation, pause, reset and desktop/mobile rendering.
Results are in `reports/HybridBrowserValidation.json`. Fake camera frames were
used; subjective real-hand responsiveness is still not measured. The test service
was stopped afterward. Port 5086 was no longer running, so the local service was
started there for manual testing; no physical camera is opened by this startup.
