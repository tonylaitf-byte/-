"""Reproduce initial calibration delays without accessing a physical camera."""
import json
import argparse
import math
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "hand-tracking"))
from puppet_controls import CONTROL_DEFAULTS, NeutralPoseCalibrator


def simulate(name, fps, noise_key=None, amplitude=0.0, miss_every=None):
    calibrator = NeutralPoseCalibrator()
    resets = 0
    completed_at = None
    for index in range(int(30 * fps) + 1):
        before = len(calibrator._samples)
        now = index / fps
        if miss_every and index and index % miss_every == 0:
            calibrator.interrupt(now)
        else:
            controls = CONTROL_DEFAULTS.copy()
            if noise_key:
                controls[noise_key] = amplitude * math.sin(index)
            calibrator.observe(controls, now)
        if calibrator.calibrated:
            completed_at = round(now, 4)
            break
        if before and len(calibrator._samples) <= 1:
            resets += 1
    return {
        "scenario": name,
        "inferenceFps": fps,
        "calibratedWithin30Seconds": calibrator.calibrated,
        "completedAtSeconds": completed_at,
        "incompleteWindowResets": resets,
        "finalProgress": round(calibrator.progress, 4),
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', type=Path, default=ROOT / 'reports' / 'InitialTrackingDiagnosis.json')
    args = parser.parse_args()
    scenarios = [simulate("perfectly_still", fps) for fps in (30, 15, 10, 5, 3, 2.5)]
    scenarios.extend([
        simulate("one_missing_detection_per_second", 30, miss_every=30),
        simulate("small_control_noise", 30, "rightHandZ", .001),
        simulate("pinky_depth_control_noise", 30, "rightHandZ", .02),
        simulate("palm_position_control_noise", 30, "bodyX", .02),
    ])
    report = {
        "scope": "Deterministic calibration logic only; no detector, camera, server or Unity input.",
        "diagnosticWritesProductionCode": False,
        "observationWindowSeconds": 30,
        "scenarios": scenarios,
    }
    target = args.output
    target.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
