import os
import sys
import copy
import threading
import time

import cv2
import numpy as np
from flask import Flask, Response, jsonify, render_template, request

import lapf as core
from gesture_recognizer import GestureActionRecognizer, GestureMovementTracker
from duet_tracking import DuetHandRouter
from duet_mapping_tracking import DuetMappingRouter


app = Flask(__name__)
app.config["MAX_CONTENT_LENGTH"] = int(
    os.environ.get("MAX_CAMERA_FRAME_BYTES", str(2 * 1024 * 1024))
)

browser_lock = threading.Lock()
inference_lock = threading.Lock()
last_browser_frame_time = None
browser_stream_active = False
browser_timeout_seconds = float(os.environ.get("BROWSER_FRAME_TIMEOUT", "0.8"))
control_mode_lock = threading.Lock()
control_mode = "mapping"
gesture_recognizer = GestureActionRecognizer(hold_seconds=0.5, cooldown_seconds=3.0)
gesture_movement = GestureMovementTracker(response_speed=8.0)
duet_router = DuetHandRouter(lost_grace=1.0)
duet_state_lock = threading.Lock()
duet_state = duet_router.update([])
duet_hands = None
duet_mapping_router = DuetMappingRouter()
duet_mapping_state_lock = threading.Lock()
duet_mapping_state = duet_mapping_router.update([])


def publish_duet_mapping_state(observations, timestamp):
    global duet_mapping_state
    state = duet_mapping_router.update(observations, timestamp)
    with duet_mapping_state_lock:
        duet_mapping_state = state
    return state


def publish_duet_state(observations, timestamp):
    global duet_state
    state = duet_router.update(observations, timestamp)
    with duet_state_lock:
        duet_state = state
    return state


def get_control_mode():
    with control_mode_lock:
        return control_mode


def publish_gesture_state(result=None, movement_x=0.0):
    result = result or gesture_recognizer.update(None)
    with core.state_lock:
        core.puppet_state.update(
            {
                "controlMode": get_control_mode(),
                "gesture": result["gesture"],
                "gestureHold": result["gestureHold"],
                "detectedAction": result["detectedAction"],
                "action": result["triggeredAction"] or result["detectedAction"],
                "actionSequence": result["actionSequence"],
                "gestureMoveX": round(float(movement_x), 4),
            }
        )


def annotate_frame(frame, state):
    cv2.putText(
        frame,
        "Unity Puppet Controls",
        (10, 30),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.75,
        (0, 255, 0),
        2,
    )
    cv2.putText(
        frame,
        (
            f"Head P {state['headPitch']:+.2f} Y {state['headYaw']:+.2f}  "
            f"L {state['leftHandX']:+.2f},{state['leftHandY']:+.2f}  "
            f"R {state['rightHandX']:+.2f},{state['rightHandY']:+.2f}"
        ),
        (10, 62),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.5,
        (255, 255, 0),
        1,
    )
    calibration_text = (
        "CALIBRATED"
        if state["calibrated"]
        else f"HOLD NEUTRAL {state['calibrationProgress'] * 100:.0f}%"
    )
    cv2.putText(
        frame,
        calibration_text,
        (10, 90),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.55,
        (0, 255, 0) if state["calibrated"] else (0, 200, 255),
        2,
    )


def process_browser_frame(frame, render_preview=True):
    global browser_stream_active
    global last_browser_frame_time

    timestamp = time.monotonic()
    brightness, _, visible = core.camera_frame_metrics(frame)
    error = "" if visible else "The browser camera frame is black."
    core.set_camera_status(
        True,
        visible,
        None,
        "Browser",
        brightness,
        error,
    )

    # Match the mirrored controller view used by the original native-camera app.
    frame = cv2.flip(frame, 1)
    mode = get_control_mode()
    global duet_hands
    if mode in ("duet", "duet_mapping") and duet_hands is None:
        duet_hands = core.mp_hands.Hands(
            static_image_mode=False, max_num_hands=2, model_complexity=core.model_complexity,
            min_detection_confidence=0.7, min_tracking_confidence=0.7,
        )
    detector = duet_hands if mode in ("duet", "duet_mapping") else core.hands
    results = (
        detector.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
        if visible
        else None
    )

    if mode in ("duet", "duet_mapping"):
        observations = []
        if results is not None and results.multi_hand_landmarks:
            for index, landmarks in enumerate(results.multi_hand_landmarks):
                handedness = results.multi_handedness[index].classification[0].label if results.multi_handedness else ""
                observations.append({'landmarks': landmarks.landmark, 'handedness': handedness})
                if render_preview:
                    core.mp_draw.draw_landmarks(frame, landmarks, core.mp_hands.HAND_CONNECTIONS)
        routed = (publish_duet_mapping_state(observations, timestamp) if mode == "duet_mapping"
                  else publish_duet_state(observations, timestamp))
        core.set_puppet_state(bool(observations), core.CONTROL_DEFAULTS.copy(), sample_time=timestamp)
        publish_gesture_state()
        if render_preview:
            height, width = frame.shape[:2]
            cv2.rectangle(frame, (int(width*.45), 0), (int(width*.55), height), (45, 45, 45), -1)
            for edge in (.45, .55):
                cv2.line(frame, (int(width*edge), 0), (int(width*edge), height), (190, 190, 190), 2)
            for side, x in (('left', 12), ('right', int(width*.57))):
                role = "WARRIOR" if side == 'left' else "MUSICIAN"
                item = routed[side]
                detail = (f"MAP {item['controls']['calibrationProgress']:.0%}" if mode == "duet_mapping" else f"G{item['gesture']}")
                cv2.putText(frame, f"{role}  {detail}  #{item['ownerId']}", (x, height-20), cv2.FONT_HERSHEY_SIMPLEX, .5, (100, 230, 190), 1)
    elif results is not None and results.multi_hand_landmarks:
        hand_landmarks = results.multi_hand_landmarks[0]
        mode = get_control_mode()
        if mode == "gesture":
            controls = core.CONTROL_DEFAULTS.copy()
            movement_x = gesture_movement.update(
                hand_landmarks.landmark,
                timestamp=timestamp,
            )
            controls["bodyX"] = movement_x
            controls["moveX"] = movement_x
            calibrated = True
            calibration_progress = 1.0
            gesture_velocities = None
            gesture_result = gesture_recognizer.update(
                hand_landmarks.landmark,
                timestamp=timestamp,
            )
        else:
            with core.pipeline_lock:
                controls = core.pipeline.update(
                    hand_landmarks.landmark,
                    timestamp=timestamp,
                )
                calibrated = core.pipeline.calibrated
                calibration_progress = core.pipeline.calibration_progress
                gesture_velocities = core.pipeline.gesture_velocities
            gesture_result = gesture_recognizer.update(None, timestamp=timestamp)
        core.set_puppet_state(
            True,
            controls,
            calibrated=calibrated,
            calibration_progress=calibration_progress,
            sample_time=timestamp,
            gesture_velocities=gesture_velocities,
        )
        publish_gesture_state(
            gesture_result,
            movement_x if mode == "gesture" else 0.0,
        )
        if render_preview:
            core.mp_draw.draw_landmarks(frame, hand_landmarks, core.mp_hands.HAND_CONNECTIONS)
    else:
        publish_no_hand(timestamp)

    if render_preview:
        with core.state_lock:
            state = core.puppet_state.copy()
        annotate_frame(frame, state)
        core.publish_frame(frame)

    with browser_lock:
        last_browser_frame_time = time.monotonic()
        browser_stream_active = True

    return [[[round(point.x, 5), round(point.y, 5)] for point in hand.landmark]
            for hand in (results.multi_hand_landmarks or [])] if results else []


def publish_no_hand(timestamp=None):
    timestamp = time.monotonic() if timestamp is None else timestamp
    mode = get_control_mode()
    if mode == "duet":
        publish_duet_state([], timestamp)
    elif mode == "duet_mapping":
        publish_duet_mapping_state([], timestamp)
    if mode == "gesture":
        movement_x = gesture_movement.update(None, timestamp=timestamp)
        controls = core.CONTROL_DEFAULTS.copy()
        controls["bodyX"] = movement_x
        controls["moveX"] = movement_x
        calibrated = True
        calibration_progress = 1.0
        gesture_velocities = None
    else:
        movement_x = 0.0
        with core.pipeline_lock:
            controls = core.pipeline.no_hand(timestamp=timestamp)
            calibrated = core.pipeline.calibrated
            calibration_progress = core.pipeline.calibration_progress
            gesture_velocities = core.pipeline.gesture_velocities
    core.set_puppet_state(
        False,
        controls,
        calibrated=calibrated,
        calibration_progress=calibration_progress,
        sample_time=timestamp,
        gesture_velocities=gesture_velocities,
    )
    publish_gesture_state(
        gesture_recognizer.update(None, timestamp=timestamp),
        movement_x,
    )


def check_browser_timeout(now):
    global browser_stream_active
    with browser_lock:
        silent = (last_browser_frame_time is not None
                  and now - last_browser_frame_time > browser_timeout_seconds)
        timed_out = browser_stream_active and silent
        if timed_out:
            browser_stream_active = False
    if timed_out:
        publish_no_hand(now)
        core.set_camera_status(False, False, None, "Browser", 0.0,
                               "Waiting for browser camera frames.")
    elif silent and get_control_mode() == "duet":
        # Continue aging ownership after the camera stops sending frames. The
        # 0.8 s browser timeout precedes the 1 s hand ownership release.
        publish_duet_state([], now)
    elif silent and get_control_mode() == "duet_mapping":
        publish_duet_mapping_state([], now)


def browser_watchdog():
    while True:
        with inference_lock:
            check_browser_timeout(time.monotonic())
        time.sleep(0.1)


@app.get("/")
def index():
    return render_template("index.html")


@app.get("/gesture")
@app.get("/gesture.html")
def gesture():
    return render_template("gesture.html")


@app.get("/duet")
@app.get("/duet.html")
def duet():
    return render_template("duet.html")


@app.get("/duet_state")
def read_duet_state():
    with duet_state_lock:
        state = copy.deepcopy(duet_state)
    state['controlMode'] = get_control_mode()
    return jsonify(state)


@app.get("/duet-mapping")
@app.get("/duet_mapping")
def duet_mapping():
    return render_template("duet_mapping.html")


@app.get("/duet_mapping_state")
def read_duet_mapping_state():
    with duet_mapping_state_lock:
        state = copy.deepcopy(duet_mapping_state)
    state['controlMode'] = get_control_mode()
    return jsonify(state)


@app.post("/duet_mapping/calibrate")
def calibrate_duet_mapping():
    payload = request.get_json(silent=True) or {}
    side = payload.get('side', 'both')
    if side not in ('left', 'right', 'both'):
        return {'error':'side must be left, right or both'}, 400
    with inference_lock:
        if performance is not None:
            return {'error': '請使用整合舞台的校正按鈕。'}, 409
        duet_mapping_router.recalibrate(side)
        publish_duet_mapping_state([], time.monotonic())
    return {'status':'calibrating','side':side}


@app.get("/mapping")
@app.get("/mapping.html")
def mapping():
    return render_template("mapping.html")


@app.get("/control")
@app.get("/control.html")
def control():
    return render_template("control.html")


@app.post("/frame")
def receive_frame():
    if not request.data:
        return {"error": "The JPEG request body is empty."}, 400

    encoded = np.frombuffer(request.data, dtype=np.uint8)
    frame = cv2.imdecode(encoded, cv2.IMREAD_COLOR)
    if frame is None or frame.size == 0:
        return {"error": "The request body is not a valid image."}, 400

    with inference_lock:
        if performance is not None and request.headers.get('X-Performance-Session') != performance['sessionId']:
            return {'error': '這個辨識頁面已不屬於目前演出。'}, 409
        if performance is None and request.headers.get('X-Performance-Session'):
            return {'error': '本次演出已結束。'}, 409
        compact = performance is not None and request.headers.get('Accept') == 'application/json'
        started = time.monotonic()
        landmarks = process_browser_frame(frame, render_preview=not compact)
        if compact:
            return jsonify({**performance_snapshot(), 'landmarks': landmarks,
                            'processingMs': round((time.monotonic() - started) * 1000, 1)})
    return Response(status=204)


@app.get("/video_feed")
def video_feed():
    return Response(
        core.generate_frames(),
        mimetype="multipart/x-mixed-replace; boundary=frame",
    )


@app.get("/puppet_state")
def puppet_state():
    with core.state_lock:
        return jsonify(core.puppet_state.copy())


@app.get("/get_command")
def get_command_compatibility():
    return puppet_state()


@app.get("/health")
def health():
    with core.pipeline_lock:
        calibrated = core.pipeline.calibrated
        calibration_progress = core.pipeline.calibration_progress
    with core.camera_status_lock:
        camera_status = {
            "cameraOpened": core.camera_opened,
            "cameraFrameVisible": core.camera_frame_visible,
            "cameraIndex": core.camera_index_in_use,
            "cameraBackend": core.camera_backend_in_use,
            "cameraBrightness": round(core.camera_brightness, 2),
            "cameraError": core.camera_error,
        }
    return {
        "status": "ok",
        "inputMode": "browser",
        "calibrated": calibrated,
        "calibrationProgress": round(calibration_progress, 4),
        **camera_status,
    }


@app.post("/calibrate")
def calibrate():
    with inference_lock:
        if performance is not None:
            return {'error': '請使用整合舞台的校正按鈕。'}, 409
        with core.pipeline_lock:
            core.pipeline.reset_calibration()
        core.set_puppet_state(False, core.CONTROL_DEFAULTS)
    return {
        "status": "calibrating",
        "message": "Hold the controller hand in a comfortable neutral pose.",
    }


@app.post("/control_mode")
def set_control_mode():
    global control_mode
    payload = request.get_json(silent=True) or {}
    mode = str(payload.get("mode", "")).strip().lower()
    if mode not in {"gesture", "mapping", "duet", "duet_mapping"}:
        return {"error": "mode must be gesture, mapping, duet or duet_mapping"}, 400

    with inference_lock:
        if performance is not None:
            return {'error': '整合舞台演出中，請先返回設定。'}, 409
        with control_mode_lock:
            if control_mode == mode:
                return {"status": "ok", "mode": mode}
            control_mode = mode
        gesture_recognizer.reset()
        gesture_movement.reset()
        duet_router.reset()
        publish_duet_state([], time.monotonic())
        duet_mapping_router.reset()
        publish_duet_mapping_state([], time.monotonic())
        publish_gesture_state()
    return {"status": "ok", "mode": mode}


from performance_session import install
install(sys.modules[__name__])

core.set_camera_status(
    False,
    False,
    None,
    "Browser",
    0.0,
    "Waiting for browser camera frames.",
)
threading.Thread(target=browser_watchdog, daemon=True).start()


if __name__ == "__main__":
    app.run(host="0.0.0.0", port=5000, threaded=True, debug=False)
