# API JSON input validation

Completed 2026-10-24.

`hand-tracking/api_input.py` provides shared request validation and a JSON 400
response for input errors. Browser-stage command endpoints and the native
calibration endpoint validate before changing session or calibration state.

## Behavior

- Arrays, strings, numbers, booleans and null are rejected as command bodies.
- Malformed JSON, invalid encoded JSON and parser recursion failures return 400.
- Nonempty bodies must have a JSON content type.
- A supplied `sessionId` must be a nonempty string. Valid but expired session IDs
  still return 409; this does not change session ownership rules.
- Existing field checks reject invalid players, mode, pause and calibration-side
  values. Legacy mode strings retain whitespace and case normalization.
- Empty bodies remain accepted for commands that previously supported them,
  including header-authenticated stop/input-stop/calibration and native calibration.
- Required-body start, pause and mode commands reject empty requests.
- Browser beacon stop requests using a JSON object remain supported.
- The existing request-size limit still returns 413. Unrelated server exceptions
  are not converted to input errors.
- Valid objects may contain extra fields; nesting alone does not make JSON invalid.

## Verification

`tests/run_backend_validation.py`: 86 tests passed, including seven input-validation
test methods with parameterized endpoint/body combinations. Checks cover rejection
before mutation, calibration preservation, session preservation, normal commands,
beacon compatibility, stale ownership, request limits and native calibration.

The original deep-nesting negative fixture was corrected: its JSON was valid and
the installed parser could decode it. Parser recursion rejection is now tested
explicitly instead of assuming a particular parser depth limit.

The idle local Flask service on port 5086 was restarted with this code. Live HTTP
requests containing `[1]`, `null` and `{` each returned JSON errors with status 400;
health returned ok with no camera open. Unity and WebGL do not need rebuilding.

The Docker COPY list now includes `api_input.py` and the previously required
`hand_identity.py`. A Docker image build was not run in this change.
