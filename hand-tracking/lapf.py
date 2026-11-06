import cv2
import os
import threading
import time

import numpy as np

os.environ.setdefault("MPLCONFIGDIR", os.path.join(os.path.dirname(__file__), ".matplotlib"))

import mediapipe as mp
from flask import Flask, Response, jsonify
from api_input import install_json_errors, json_object

from puppet_controls import (
    CONTROL_DEFAULTS,
    PuppetControlPipeline,
    make_puppet_payload,
)


app = Flask(__name__)
install_json_errors(app)

mp_hands = mp.solutions.hands
model_complexity = max(0, min(1, int(os.environ.get("HAND_MODEL_COMPLEXITY", "1"))))
hands = mp_hands.Hands(
    static_image_mode=False,
    max_num_hands=1,
    model_complexity=model_complexity,
    min_detection_confidence=0.7,
    min_tracking_confidence=0.7,
)
mp_draw = mp.solutions.drawing_utils

frame_lock = threading.Lock()
state_lock = threading.Lock()
pipeline_lock = threading.Lock()
camera_status_lock = threading.Lock()
latest_jpeg = None
camera_opened = False
camera_frame_visible = False
camera_index_in_use = None
camera_backend_in_use = "none"
camera_brightness = 0.0
camera_error = "Camera is starting."
pipeline = PuppetControlPipeline(calibration_samples=45)
puppet_state = make_puppet_payload(False, CONTROL_DEFAULTS)
puppet_sequence = 0
last_puppet_sample_time = None


def set_puppet_state(
    hand_detected,
    controls,
    calibrated=False,
    calibration_progress=0.0,
    sample_time=None,
    gesture_velocities=None,
):
    global puppet_state
    global puppet_sequence
    global last_puppet_sample_time
    sample_time = time.monotonic() if sample_time is None else float(sample_time)
    with state_lock:
        sample_dt = (
            sample_time - last_puppet_sample_time
            if last_puppet_sample_time is not None
            else 1.0 / 30.0
        )
        last_puppet_sample_time = sample_time
        puppet_sequence += 1
        puppet_state = make_puppet_payload(
            hand_detected,
            controls,
            calibrated=calibrated,
            calibration_progress=calibration_progress,
            sequence_id=puppet_sequence,
            sample_dt=sample_dt,
            gesture_velocities=gesture_velocities,
        )


def set_camera_status(opened, frame_visible, index, backend, brightness, error):
    global camera_opened
    global camera_frame_visible
    global camera_index_in_use
    global camera_backend_in_use
    global camera_brightness
    global camera_error

    with camera_status_lock:
        camera_opened = opened
        camera_frame_visible = frame_visible
        camera_index_in_use = index
        camera_backend_in_use = backend
        camera_brightness = brightness
        camera_error = error


def camera_frame_metrics(frame):
    if frame is None or frame.size == 0:
        return 0.0, 0.0, False

    gray = cv2.cvtColor(frame, cv2.COLOR_BGR2GRAY)
    mean, standard_deviation = cv2.meanStdDev(gray)
    brightness = float(mean[0][0])
    contrast = float(standard_deviation[0][0])
    lit_pixel_ratio = float(np.count_nonzero(gray >= 8)) / float(gray.size)
    visible = brightness >= 2.0 and lit_pixel_ratio >= 0.01
    return brightness, contrast, visible


def configure_camera(cap):
    cap.set(cv2.CAP_PROP_FRAME_WIDTH, int(os.environ.get("CAMERA_WIDTH", "640")))
    cap.set(cv2.CAP_PROP_FRAME_HEIGHT, int(os.environ.get("CAMERA_HEIGHT", "480")))
    cap.set(cv2.CAP_PROP_FPS, int(os.environ.get("CAMERA_FPS", "30")))


def warm_up_camera(cap):
    frame = None
    brightness = 0.0
    contrast = 0.0
    visible = False
    frame_count = max(4, int(os.environ.get("CAMERA_WARMUP_FRAMES", "8")))

    for _ in range(frame_count):
        success, candidate = cap.read()
        if not success or candidate is None:
            time.sleep(0.02)
            continue
        frame = candidate
        brightness, contrast, visible = camera_frame_metrics(frame)
        if visible:
            break

    return frame, brightness, contrast, visible


def camera_candidates():
    configured_index = os.environ.get("CAMERA_INDEX")
    if configured_index is not None:
        indexes = [int(configured_index)]
    else:
        probe_count = max(1, int(os.environ.get("CAMERA_PROBE_COUNT", "1")))
        indexes = list(range(probe_count))

    if os.name != "nt":
        return [(index, "Default", cv2.CAP_ANY) for index in indexes]

    backends = {
        "msmf": ("MSMF", cv2.CAP_MSMF),
        "dshow": ("DirectShow", cv2.CAP_DSHOW),
        "default": ("Default", cv2.CAP_ANY),
    }
    configured_backend = os.environ.get("CAMERA_BACKEND", "dshow").strip().lower()
    if configured_backend == "auto":
        selected_backends = [backends["dshow"]]
    elif configured_backend in backends:
        selected_backends = [backends[configured_backend]]
    else:
        raise ValueError(
            "CAMERA_BACKEND must be auto, msmf, dshow, or default."
        )

    return [
        (index, backend_name, backend_id)
        for index in indexes
        for backend_name, backend_id in selected_backends
    ]


def open_working_camera():
    first_black_candidate = None

    for index, backend_name, backend_id in camera_candidates():
        cap = cv2.VideoCapture(index, backend_id)
        if not cap.isOpened():
            cap.release()
            continue

        configure_camera(cap)
        frame, brightness, contrast, visible = warm_up_camera(cap)
        if visible:
            print(
                f"[camera] Using index {index} via {backend_name} "
                f"(brightness {brightness:.1f}, contrast {contrast:.1f})."
            )
            return cap, frame, index, backend_name, brightness, ""

        if frame is not None and first_black_candidate is None:
            first_black_candidate = (index, backend_name, backend_id, frame, brightness)
        cap.release()
        time.sleep(0.15)

    if first_black_candidate is not None:
        index, backend_name, backend_id, frame, brightness = first_black_candidate
        cap = cv2.VideoCapture(index, backend_id)
        if cap.isOpened():
            configure_camera(cap)
            error = "Camera frames are black. Reconnect the USB camera and check privacy mode."
            print(f"[camera] {error}")
            return cap, frame, index, backend_name, brightness, error
        cap.release()

    return None, None, None, "none", 0.0, "No usable camera could be opened."


def publish_frame(frame):
    global latest_jpeg

    encoded, buffer = cv2.imencode(
        ".jpg",
        frame,
        [int(cv2.IMWRITE_JPEG_QUALITY), 82],
    )
    if encoded:
        with frame_lock:
            latest_jpeg = buffer.tobytes()


def publish_camera_error(message):
    frame = np.zeros((720, 1280, 3), dtype=np.uint8)
    cv2.putText(
        frame,
        "CAMERA NOT READY",
        (40, 80),
        cv2.FONT_HERSHEY_SIMPLEX,
        1.2,
        (0, 80, 255),
        3,
    )
    cv2.putText(
        frame,
        message,
        (40, 130),
        cv2.FONT_HERSHEY_SIMPLEX,
        0.65,
        (255, 255, 255),
        2,
    )
    publish_frame(frame)


def camera_thread():
    while True:
        cap, first_frame, camera_index, backend_name, brightness, error = (
            open_working_camera()
        )
        if cap is None:
            set_camera_status(False, False, None, "none", 0.0, error)
            publish_camera_error(error)
            time.sleep(2.0)
            continue

        set_camera_status(
            True,
            not error,
            camera_index,
            backend_name,
            brightness,
            error,
        )
        pending_frame = first_frame
        failed_reads = 0

        while cap.isOpened():
            if pending_frame is not None:
                success, frame = True, pending_frame
                pending_frame = None
            else:
                success, frame = cap.read()

            if not success:
                failed_reads += 1
                if failed_reads >= 30:
                    break
                time.sleep(0.02)
                continue

            failed_reads = 0
            frame_timestamp = time.monotonic()
            brightness, _, frame_visible = camera_frame_metrics(frame)
            error = (
                ""
                if frame_visible
                else "Camera frames are black. Reconnect the USB camera and check privacy mode."
            )
            set_camera_status(
                True,
                frame_visible,
                camera_index,
                backend_name,
                brightness,
                error,
            )
            frame = cv2.flip(frame, 1)
            results = (
                hands.process(cv2.cvtColor(frame, cv2.COLOR_BGR2RGB))
                if frame_visible
                else None
            )

            if results is not None and results.multi_hand_landmarks:
                hand_landmarks = results.multi_hand_landmarks[0]
                with pipeline_lock:
                    controls = pipeline.update(
                        hand_landmarks.landmark,
                        timestamp=frame_timestamp,
                        frame_size=(frame.shape[1], frame.shape[0]),
                    )
                    calibrated = pipeline.calibrated
                    calibration_progress = pipeline.calibration_progress
                    gesture_velocities = pipeline.gesture_velocities
                set_puppet_state(
                    True,
                    controls,
                    calibrated=calibrated,
                    calibration_progress=calibration_progress,
                    sample_time=frame_timestamp,
                    gesture_velocities=gesture_velocities,
                )
                mp_draw.draw_landmarks(
                    frame,
                    hand_landmarks,
                    mp_hands.HAND_CONNECTIONS,
                )
            else:
                with pipeline_lock:
                    controls = pipeline.no_hand(timestamp=frame_timestamp)
                    calibrated = pipeline.calibrated
                    calibration_progress = pipeline.calibration_progress
                    gesture_velocities = pipeline.gesture_velocities
                set_puppet_state(
                    False,
                    controls,
                    calibrated=calibrated,
                    calibration_progress=calibration_progress,
                    sample_time=frame_timestamp,
                    gesture_velocities=gesture_velocities,
                )

            with state_lock:
                display_state = puppet_state.copy()

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
                    f"Head P {display_state['headPitch']:+.2f} "
                    f"Y {display_state['headYaw']:+.2f}  "
                    f"L {display_state['leftHandX']:+.2f},"
                    f"{display_state['leftHandY']:+.2f}  "
                    f"R {display_state['rightHandX']:+.2f},"
                    f"{display_state['rightHandY']:+.2f}"
                ),
                (10, 65),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.5,
                (255, 255, 0),
                1,
            )
            cv2.putText(
                frame,
                (
                    "CALIBRATED"
                    if display_state["calibrated"]
                    else f"HOLD NEUTRAL {display_state['calibrationProgress'] * 100:.0f}%"
                ),
                (10, 92),
                cv2.FONT_HERSHEY_SIMPLEX,
                0.55,
                (0, 255, 0) if display_state["calibrated"] else (0, 200, 255),
                2,
            )

            if not frame_visible:
                cv2.putText(
                    frame,
                    "CAMERA IMAGE IS BLACK",
                    (10, 130),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.65,
                    (0, 80, 255),
                    2,
                )
                cv2.putText(
                    frame,
                    "Reconnect the USB camera and check camera privacy mode.",
                    (10, 160),
                    cv2.FONT_HERSHEY_SIMPLEX,
                    0.55,
                    (0, 160, 255),
                    2,
                )

            publish_frame(frame)

        set_camera_status(
            False,
            False,
            camera_index,
            backend_name,
            0.0,
            "Camera stopped returning frames. Reconnecting.",
        )
        cap.release()
        time.sleep(0.5)


def generate_frames():
    while True:
        with frame_lock:
            jpeg = latest_jpeg
        if jpeg is None:
            time.sleep(0.02)
            continue
        yield b"--frame\r\nContent-Type: image/jpeg\r\n\r\n" + jpeg + b"\r\n"
        time.sleep(0.01)


@app.route("/")
def index():
    return Response(
        generate_frames(),
        mimetype="multipart/x-mixed-replace; boundary=frame",
    )


@app.route("/puppet_state")
def get_puppet_state():
    with state_lock:
        return jsonify(puppet_state.copy())


@app.route("/get_command")
def get_command_compatibility():
    return get_puppet_state()


@app.route("/health")
def health():
    with pipeline_lock:
        calibrated = pipeline.calibrated
        calibration_progress = pipeline.calibration_progress
    with camera_status_lock:
        camera_status = {
            "cameraOpened": camera_opened,
            "cameraFrameVisible": camera_frame_visible,
            "cameraIndex": camera_index_in_use,
            "cameraBackend": camera_backend_in_use,
            "cameraBrightness": round(camera_brightness, 2),
            "cameraError": camera_error,
        }
    return {
        "status": "ok",
        "calibrated": calibrated,
        "calibrationProgress": round(calibration_progress, 4),
        **camera_status,
    }


@app.route("/calibrate", methods=["POST"])
def calibrate():
    json_object(allow_empty=True)
    with pipeline_lock:
        pipeline.reset_calibration()
    set_puppet_state(False, CONTROL_DEFAULTS)
    return {
        "status": "calibrating",
        "message": "Hold the controller hand in a comfortable neutral pose.",
    }


if __name__ == "__main__":
    threading.Thread(target=camera_thread, daemon=True).start()
    app.run(host="0.0.0.0", port=5000, threaded=True, debug=False)
