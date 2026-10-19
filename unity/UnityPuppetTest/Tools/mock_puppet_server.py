import json
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer


def puppet_state(**overrides):
    state = {
        "version": 2,
        "handDetected": True,
        "calibrated": True,
        "calibrationProgress": 1.0,
        "sequenceId": 0,
        "sampleDt": 0.033,
        "leftArmVelocity": 0.0,
        "rightArmVelocity": 0.0,
        "headNod": 0.0,
        "leftArm": 0.0,
        "rightArm": 0.0,
        "bodyTurn": 0.0,
        "moveX": 0.0,
        "leftHandX": 0.0,
        "leftHandY": 0.0,
        "leftHandZ": 0.0,
        "rightHandX": 0.0,
        "rightHandY": 0.0,
        "rightHandZ": 0.0,
        "headPitch": 0.0,
        "headYaw": 0.0,
        "headRoll": 0.0,
        "bodyX": 0.0,
        "bodyY": 0.0,
        "bodyDepth": 0.0,
        "bodyLean": 0.0,
    }
    state.update(overrides)
    return state


PRESETS = {
    "neutral": puppet_state(),
    "head-direction": puppet_state(headPitch=0.8, headYaw=-0.65, headRoll=0.4),
    "left-hand": puppet_state(leftHandX=0.55, leftHandY=0.8, leftHandZ=-0.45),
    "right-hand": puppet_state(rightHandX=-0.55, rightHandY=0.8, rightHandZ=-0.45),
    "center-below-threshold": puppet_state(leftArm=0.4, rightArm=0.4),
    "left-curl": puppet_state(leftArm=1.0, leftArmVelocity=6.0),
    "right-curl": puppet_state(rightArm=1.0, rightArmVelocity=6.0),
    "left-center-palm-shift": puppet_state(
        leftArm=1.0,
        bodyX=0.45,
        bodyY=0.35,
        bodyDepth=0.2,
    ),
    "right-center-palm-shift": puppet_state(
        rightArm=1.0,
        bodyX=-0.45,
        bodyY=0.35,
        bodyDepth=0.2,
    ),
    "forward-hands": puppet_state(leftHandZ=-0.8, rightHandZ=0.8),
    "flexible-left-forward": puppet_state(
        leftHandX=0.35,
        leftHandY=0.6,
        leftHandZ=-0.85,
    ),
    "synergy-left": puppet_state(leftHandX=0.5, leftHandY=0.7, leftHandZ=-0.55),
    "elbow-sweep-left": puppet_state(leftHandX=-0.45, leftHandY=0.35, leftHandZ=-0.2),
    "elbow-sweep-forward": puppet_state(leftHandX=0.1, leftHandY=0.85, leftHandZ=-0.85),
    "body": puppet_state(bodyX=0.6, bodyY=0.5, bodyDepth=0.35, bodyTurn=0.8, bodyLean=-0.6),
    "palm-arms": puppet_state(bodyX=0.65, bodyY=0.55, bodyDepth=0.45),
    "uncalibrated": puppet_state(
        calibrated=False,
        calibrationProgress=0.5,
        leftHandX=1.0,
        rightHandY=1.0,
        headPitch=1.0,
        bodyX=1.0,
    ),
    "hand-lost": puppet_state(handDetected=False),
}

state_lock = threading.Lock()
current_state = PRESETS["neutral"].copy()


class PuppetStateHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        global current_state

        if self.path == "/health":
            self.send_json({"status": "ok"})
            return

        if self.path == "/puppet_state":
            with state_lock:
                self.send_json(current_state.copy())
            return

        if self.path.startswith("/preset/"):
            preset_name = self.path.removeprefix("/preset/")
            if preset_name not in PRESETS:
                self.send_error(404, "Unknown preset")
                return
            with state_lock:
                current_state = PRESETS[preset_name].copy()
                payload = current_state.copy()
            self.send_json(payload)
            return

        self.send_error(404)

    def send_json(self, payload):
        body = json.dumps(payload).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, format, *args):
        return


if __name__ == "__main__":
    server = ThreadingHTTPServer(("127.0.0.1", 5000), PuppetStateHandler)
    print("Mock puppet channel: http://127.0.0.1:5000/puppet_state", flush=True)
    server.serve_forever()
