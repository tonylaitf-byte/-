"""Compare first-frame landmark transport with fake detector output, no camera."""
import json
import sys
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch

ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT / "hand-tracking"), str(ROOT / "tests" / "legacy")]
import cv2
import numpy as np
import docker_app as service
from test_gesture_recognizer import hand


def main():
    client = service.app.test_client()
    jpeg = cv2.imencode(".jpg", np.full((120, 160, 3), 120, np.uint8))[1].tobytes()
    cases = []
    for players in (1, 2):
        for mode in ("gesture", "mapping", "hybrid"):
            session = client.post("/performance/start", json={"players": players, "mode": mode}).get_json()
            detections = []
            for x in ((.2,) if players == 1 else (.2, .8)):
                points = hand({"thumb", "index", "middle", "ring", "pinky"})
                center = sum(points[i].x for i in (0, 5, 9, 13, 17)) / 5
                detections.append(SimpleNamespace(landmark=[
                    SimpleNamespace(x=p.x + x - center, y=p.y, z=p.z) for p in points]))
            detector = Mock()
            detector.process.return_value = SimpleNamespace(multi_hand_landmarks=detections, multi_handedness=None)
            headers = {"X-Performance-Session": session["sessionId"],
                       "X-Calibration-Revision": "0", "Accept": "application/json"}
            try:
                with patch.object(service.core, "hands", detector), patch.object(service, "duet_hands", detector):
                    response = client.post("/frame", data=jpeg, content_type="image/jpeg", headers=headers)
                payload = response.get_json()
                state = payload["state"]
                slots = [state] if players == 1 else [state[side] for side in ("left", "right")]
                controls = [slot.get("controls", slot) for slot in slots]
                result = {"players": players, "mode": mode, "status": response.status_code,
                          "detectorCalls": detector.process.call_count,
                          "landmarkHandsOnFirstFrame": len(payload["landmarks"]),
                          "landmarksPerHand": [len(points) for points in payload["landmarks"]],
                          "handDetected": [slot["handDetected"] for slot in slots],
                          "calibrated": [control.get("calibrated") for control in controls]}
                assert result["status"] == 200 and result["detectorCalls"] == 1
                assert result["landmarksPerHand"] == [21] * players
                assert all(result["handDetected"])
                if mode != "gesture":
                    assert not any(result["calibrated"])
                cases.append(result)
            finally:
                client.post("/performance/stop", json={}, headers=headers)
    report = {"passed": True,
              "scope": "Real Flask first-frame route with mocked detector output. No actual image recognition, browser, Unity or camera validation.",
              "cases": cases}
    (ROOT / "reports" / "InitialModeDetectionDiagnosis.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))


if __name__ == "__main__":
    main()
