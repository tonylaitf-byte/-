import json
import sys
from pathlib import Path

import cv2


def main():
    args = sys.argv[1:]
    if len(args) != 2:
        raise SystemExit("Usage: verify_hand_tracking.py IMAGE REPORT")

    image_path = Path(args[0]).resolve()
    report_path = Path(args[1]).resolve()
    project_root = Path(__file__).resolve().parents[1]
    sys.path.insert(0, str(project_root / "hand-tracking"))

    import docker_app

    frame = cv2.imread(str(image_path))
    if frame is None:
        raise SystemExit(f"Could not read test image: {image_path}")

    with docker_app.core.pipeline_lock:
        docker_app.core.pipeline.reset_calibration()
    for _ in range(50):
        with docker_app.inference_lock:
            docker_app.process_browser_frame(frame.copy())

    with docker_app.core.state_lock:
        state = docker_app.core.puppet_state.copy()

    required_keys = {
        "version",
        "handDetected",
        "calibrated",
        "headNod",
        "headPitch",
        "headYaw",
        "headRoll",
        "leftArm",
        "leftHandX",
        "leftHandY",
        "leftHandZ",
        "rightArm",
        "rightHandX",
        "rightHandY",
        "rightHandZ",
        "bodyX",
        "bodyY",
        "bodyDepth",
        "bodyTurn",
        "bodyLean",
    }
    missing_keys = sorted(required_keys - state.keys())
    report = {
        "success": (
            state.get("version") == 2
            and state.get("handDetected") is True
            and state.get("calibrated") is True
            and not missing_keys
        ),
        "version": state.get("version"),
        "handDetected": state.get("handDetected"),
        "calibrated": state.get("calibrated"),
        "calibrationProgress": state.get("calibrationProgress"),
        "sequenceId": state.get("sequenceId"),
        "missingUnityKeys": missing_keys,
    }
    report_path.write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report))
    raise SystemExit(0 if report["success"] else 2)


if __name__ == "__main__":
    main()
