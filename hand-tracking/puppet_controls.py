import math
import statistics
import time


PAYLOAD_VERSION = 2

LEGACY_CONTROL_DEFAULTS = {
    "headNod": 0.0,
    "leftArm": 0.0,
    "rightArm": 0.0,
    "bodyTurn": 0.0,
    "moveX": 0.0,
}

IK_CONTROL_DEFAULTS = {
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

CONTROL_DEFAULTS = {**LEGACY_CONTROL_DEFAULTS, **IK_CONTROL_DEFAULTS}

CENTER_GESTURE_KEYS = ("leftArm", "rightArm")
CALIBRATED_KEYS = tuple(IK_CONTROL_DEFAULTS) + ("bodyTurn",) + CENTER_GESTURE_KEYS

CONTROL_GAINS = {
    "leftHandX": 1.5,
    "leftHandY": 1.5,
    "leftHandZ": 3.0,
    # The pinky has a smaller comfortable travel range than the thumb.
    "rightHandX": 2.0,
    "rightHandY": 2.0,
    "rightHandZ": 4.0,
    "leftArm": 1.25,
    "rightArm": 1.55,
    "headPitch": 1.25,
    "headYaw": 1.25,
    "headRoll": 1.8,
    "bodyX": 4.0,
    "bodyY": 4.0,
    "bodyDepth": 2.5,
    "bodyTurn": 1.0,
    "bodyLean": 1.0,
}

# Small dead zones remove landmark tremor. Exponents below 1 amplify comfortable
# finger motion and palm-center gestures, with a stronger curve for the pinky.
CONTROL_RESPONSE = {
    "leftHandX": (0.025, 0.82),
    "leftHandY": (0.025, 0.82),
    "leftHandZ": (0.035, 0.82),
    "rightHandX": (0.018, 0.68),
    "rightHandY": (0.018, 0.68),
    "rightHandZ": (0.025, 0.68),
    "leftArm": (0.035, 0.78),
    "rightArm": (0.025, 0.68),
    "headPitch": (0.020, 0.88),
    "headYaw": (0.020, 0.88),
    "headRoll": (0.025, 0.90),
    "bodyX": (0.020, 0.92),
    "bodyY": (0.020, 0.92),
    "bodyDepth": (0.025, 0.92),
    "bodyTurn": (0.020, 0.92),
    "bodyLean": (0.020, 0.92),
}

ADAPTIVE_FILTER_DEFAULTS = {
    "min_cutoff": 1.15,
    "beta": 0.70,
    "derivative_cutoff": 1.0,
}

ADAPTIVE_FILTER_OVERRIDES = {
    # Depth is noisier in a monocular camera, so it receives stronger
    # stationary smoothing while still opening up during fast movement.
    "leftHandZ": (0.85, 0.85),
    "rightHandZ": (0.85, 0.95),
    "bodyDepth": (0.65, 0.45),
    # Palm-center gestures stay stable at rest but expose deliberate speed to
    # Unity's motion servo instead of being turned into a fixed animation.
    "leftArm": (1.55, 1.00),
    "rightArm": (1.55, 1.10),
    # Body motion should feel weightier than the hands.
    "bodyX": (0.80, 0.45),
    "bodyY": (0.80, 0.45),
    "bodyTurn": (0.75, 0.40),
    "bodyLean": (0.75, 0.40),
}


def make_puppet_payload(
    hand_detected,
    controls,
    calibrated=False,
    calibration_progress=0.0,
    sequence_id=0,
    sample_dt=1.0 / 30.0,
    gesture_velocities=None,
):
    gesture_velocities = gesture_velocities or {}
    payload = {
        "version": PAYLOAD_VERSION,
        "handDetected": bool(hand_detected),
        "calibrated": bool(calibrated),
        "calibrationProgress": round(clamp(calibration_progress, 0.0, 1.0), 4),
        "sequenceId": max(0, int(sequence_id)),
        "sampleDt": round(clamp(float(sample_dt), 1.0 / 240.0, 0.25), 5),
        "leftArmVelocity": round(
            clamp(float(gesture_velocities.get("leftArm", 0.0)), -8.0, 8.0),
            4,
        ),
        "rightArmVelocity": round(
            clamp(float(gesture_velocities.get("rightArm", 0.0)), -8.0, 8.0),
            4,
        ),
    }
    payload.update(
        {
            key: round(float(controls.get(key, default)), 4)
            for key, default in CONTROL_DEFAULTS.items()
        }
    )
    return payload


def clamp(value, minimum, maximum):
    return max(minimum, min(maximum, value))


def _signed_response(value, dead_zone, exponent):
    magnitude = abs(float(value))
    if magnitude <= dead_zone:
        return 0.0

    normalized = (magnitude - dead_zone) / max(1.0 - dead_zone, 1e-6)
    shaped = math.pow(clamp(normalized, 0.0, 1.0), exponent)
    return math.copysign(shaped, value)


def _vector(point):
    return (
        float(point.x),
        float(point.y),
        float(getattr(point, "z", 0.0)),
    )


def _add(left, right):
    return tuple(a + b for a, b in zip(left, right))


def _subtract(left, right):
    return tuple(a - b for a, b in zip(left, right))


def _scale(vector, factor):
    return tuple(value * factor for value in vector)


def _dot(left, right):
    return sum(a * b for a, b in zip(left, right))


def _cross(left, right):
    return (
        left[1] * right[2] - left[2] * right[1],
        left[2] * right[0] - left[0] * right[2],
        left[0] * right[1] - left[1] * right[0],
    )


def _length(vector):
    return math.sqrt(_dot(vector, vector))


def _normalize(vector, fallback):
    length = _length(vector)
    if length < 1e-6:
        return fallback
    return _scale(vector, 1.0 / length)


def _average(points):
    total = (0.0, 0.0, 0.0)
    for point in points:
        total = _add(total, point)
    return _scale(total, 1.0 / len(points))


def joint_angle(a, b, c):
    ba = _subtract(_vector(a), _vector(b))
    bc = _subtract(_vector(c), _vector(b))
    ba_length = _length(ba)
    bc_length = _length(bc)
    if ba_length == 0.0 or bc_length == 0.0:
        return 180.0

    cosine = _dot(ba, bc) / (ba_length * bc_length)
    return math.degrees(math.acos(clamp(cosine, -1.0, 1.0)))


def finger_curl(landmarks, mcp, pip, tip):
    angle = joint_angle(landmarks[mcp], landmarks[pip], landmarks[tip])
    return clamp((165.0 - angle) / 85.0, 0.0, 1.0)


def _palm_frame(landmarks):
    wrist = _vector(landmarks[0])
    index_mcp = _vector(landmarks[5])
    middle_mcp = _vector(landmarks[9])
    ring_mcp = _vector(landmarks[13])
    pinky_mcp = _vector(landmarks[17])

    center = _average((wrist, index_mcp, middle_mcp, ring_mcp, pinky_mcp))
    x_axis = _normalize(_subtract(index_mcp, pinky_mcp), (-1.0, 0.0, 0.0))
    y_hint = _normalize(_subtract(middle_mcp, wrist), (0.0, -1.0, 0.0))
    z_axis = _normalize(_cross(x_axis, y_hint), (0.0, 0.0, 1.0))
    y_axis = _normalize(_cross(z_axis, x_axis), (0.0, -1.0, 0.0))
    palm_scale = max(_length(_subtract(index_mcp, pinky_mcp)), 1e-4)

    return center, x_axis, y_axis, z_axis, palm_scale


def _to_palm_local(point, frame):
    center, x_axis, y_axis, z_axis, palm_scale = frame
    relative = _subtract(_vector(point), center)
    return (
        _dot(relative, x_axis) / palm_scale,
        _dot(relative, y_axis) / palm_scale,
        _dot(relative, z_axis) / palm_scale,
    )


def _palm_local_vector(landmarks, start_index, end_index, frame):
    start = _to_palm_local(landmarks[start_index], frame)
    end = _to_palm_local(landmarks[end_index], frame)
    return _subtract(end, start)


def palm_center_reach(landmarks, tip_index, frame):
    """Return how deeply a fingertip has entered the central palm area."""
    x, y, z = _to_palm_local(landmarks[tip_index], frame)
    # MediaPipe depth is noisier than its image-plane position, so it contributes
    # less while still rejecting a fingertip hovering well above the palm.
    distance = math.sqrt(x * x + y * y + (z * 0.35) ** 2)
    return clamp((0.70 - distance) / (0.70 - 0.28), 0.0, 1.0)


def controls_from_landmarks(landmarks):
    frame = _palm_frame(landmarks)
    center, _, y_axis, z_axis, palm_scale = frame
    thumb_direction = _palm_local_vector(landmarks, 2, 4, frame)
    middle_direction = _palm_local_vector(landmarks, 9, 12, frame)
    pinky_direction = _palm_local_vector(landmarks, 17, 20, frame)

    thumb_center_reach = palm_center_reach(landmarks, 4, frame)
    middle_curl = finger_curl(landmarks, 9, 10, 12)
    pinky_center_reach = palm_center_reach(landmarks, 20, frame)

    palm_yaw = math.degrees(math.atan2(z_axis[0], max(abs(z_axis[2]), 1e-5)))
    palm_roll = math.degrees(math.atan2(y_axis[0], -y_axis[1]))

    return {
        "headNod": middle_curl,
        "leftArm": thumb_center_reach,
        "rightArm": pinky_center_reach,
        "bodyTurn": clamp(palm_yaw / 45.0, -2.0, 2.0),
        "moveX": clamp((center[0] - 0.5) * 2.0, -1.0, 1.0),
        "leftHandX": thumb_direction[0],
        "leftHandY": thumb_direction[1],
        "leftHandZ": -thumb_direction[2],
        "rightHandX": pinky_direction[0],
        "rightHandY": pinky_direction[1],
        "rightHandZ": -pinky_direction[2],
        "headPitch": -middle_direction[1],
        "headYaw": middle_direction[0],
        "headRoll": -middle_direction[2],
        "bodyX": center[0],
        "bodyY": -center[1],
        "bodyDepth": math.log(palm_scale),
        "bodyLean": clamp(palm_roll / 45.0, -2.0, 2.0),
    }


class NeutralPoseCalibrator:
    def __init__(self, samples_required=45):
        self.samples_required = max(1, int(samples_required))
        self.reset()

    @property
    def calibrated(self):
        return self._baseline is not None

    @property
    def progress(self):
        if self.calibrated:
            return 1.0
        return len(self._samples) / self.samples_required

    def reset(self):
        self._samples = []
        self._baseline = None

    def observe(self, raw_controls):
        if self.calibrated:
            return

        self._samples.append(
            {key: float(raw_controls.get(key, 0.0)) for key in CALIBRATED_KEYS}
        )
        if len(self._samples) >= self.samples_required:
            self._baseline = {
                key: statistics.median(sample[key] for sample in self._samples)
                for key in CALIBRATED_KEYS
            }
            self._samples = []

    def apply(self, raw_controls):
        if not self.calibrated:
            return CONTROL_DEFAULTS.copy()

        result = {
            "headNod": clamp(float(raw_controls.get("headNod", 0.0)), 0.0, 1.0)
        }
        for key in CALIBRATED_KEYS:
            delta = float(raw_controls.get(key, 0.0)) - self._baseline[key]
            scaled = clamp(
                delta * CONTROL_GAINS[key],
                0.0 if key in CENTER_GESTURE_KEYS else -1.0,
                1.0,
            )
            dead_zone, exponent = CONTROL_RESPONSE[key]
            result[key] = _signed_response(scaled, dead_zone, exponent)

        result["moveX"] = result["bodyX"]
        return {**CONTROL_DEFAULTS, **result}


class ControlSmoother:
    def __init__(self, alpha=0.25):
        self.alpha = clamp(alpha, 0.0, 1.0)
        self.values = CONTROL_DEFAULTS.copy()

    def reset(self):
        self.values = CONTROL_DEFAULTS.copy()

    def update(self, target):
        for key, default in CONTROL_DEFAULTS.items():
            next_value = float(target.get(key, default))
            self.values[key] += (next_value - self.values[key]) * self.alpha
        return {key: round(value, 4) for key, value in self.values.items()}


class OneEuroFilter:
    """Adaptive scalar low-pass filter for responsive teleoperation."""

    def __init__(
        self,
        min_cutoff=1.15,
        beta=0.70,
        derivative_cutoff=1.0,
    ):
        self.min_cutoff = max(float(min_cutoff), 1e-4)
        self.beta = max(float(beta), 0.0)
        self.derivative_cutoff = max(float(derivative_cutoff), 1e-4)
        self.reset()

    @staticmethod
    def _alpha(delta_time, cutoff):
        time_constant = 1.0 / (2.0 * math.pi * cutoff)
        return 1.0 / (1.0 + time_constant / delta_time)

    def reset(self):
        self.filtered_value = None
        self.filtered_derivative = 0.0
        self.previous_raw = None
        self.previous_time = None

    def update(self, value, timestamp=None):
        value = float(value)
        timestamp = time.monotonic() if timestamp is None else float(timestamp)
        if self.filtered_value is None:
            self.filtered_value = value
            self.previous_raw = value
            self.previous_time = timestamp
            return value

        delta_time = clamp(timestamp - self.previous_time, 1.0 / 240.0, 0.25)
        derivative = (value - self.previous_raw) / delta_time
        derivative_alpha = self._alpha(delta_time, self.derivative_cutoff)
        self.filtered_derivative += (
            derivative - self.filtered_derivative
        ) * derivative_alpha

        cutoff = self.min_cutoff + self.beta * abs(self.filtered_derivative)
        value_alpha = self._alpha(delta_time, cutoff)
        self.filtered_value += (value - self.filtered_value) * value_alpha
        self.previous_raw = value
        self.previous_time = timestamp
        return self.filtered_value


class AdaptiveControlSmoother:
    """One Euro filters tuned per control channel.

    This follows the adaptive smoothing strategy used by Wuji's teleoperation
    input: steady poses receive more filtering while deliberate fast movement
    stays responsive.
    """

    def __init__(self):
        self.filters = {}
        for key in CONTROL_DEFAULTS:
            min_cutoff, beta = ADAPTIVE_FILTER_OVERRIDES.get(
                key,
                (
                    ADAPTIVE_FILTER_DEFAULTS["min_cutoff"],
                    ADAPTIVE_FILTER_DEFAULTS["beta"],
                ),
            )
            self.filters[key] = OneEuroFilter(
                min_cutoff=min_cutoff,
                beta=beta,
                derivative_cutoff=ADAPTIVE_FILTER_DEFAULTS[
                    "derivative_cutoff"
                ],
            )
        self.values = CONTROL_DEFAULTS.copy()

    def reset(self):
        for filter_ in self.filters.values():
            filter_.reset()
        self.values = CONTROL_DEFAULTS.copy()

    def update(self, target, timestamp=None):
        timestamp = time.monotonic() if timestamp is None else float(timestamp)
        for key, default in CONTROL_DEFAULTS.items():
            self.values[key] = self.filters[key].update(
                target.get(key, default),
                timestamp=timestamp,
            )
        return {
            key: round(clamp(value, -1.0, 1.0), 4)
            for key, value in self.values.items()
        }


class PuppetControlPipeline:
    def __init__(self, alpha=None, calibration_samples=45):
        self.smoother = (
            AdaptiveControlSmoother()
            if alpha is None
            else ControlSmoother(alpha=alpha)
        )
        self.calibrator = NeutralPoseCalibrator(samples_required=calibration_samples)

    @property
    def calibrated(self):
        return self.calibrator.calibrated

    @property
    def calibration_progress(self):
        return self.calibrator.progress

    @property
    def gesture_velocities(self):
        if not isinstance(self.smoother, AdaptiveControlSmoother):
            return {key: 0.0 for key in CENTER_GESTURE_KEYS}
        return {
            key: clamp(self.smoother.filters[key].filtered_derivative, -8.0, 8.0)
            for key in CENTER_GESTURE_KEYS
        }

    def reset_calibration(self):
        self.calibrator.reset()
        self.smoother.reset()

    def update(self, landmarks, timestamp=None):
        raw_controls = controls_from_landmarks(landmarks)
        self.calibrator.observe(raw_controls)
        calibrated = self.calibrator.apply(raw_controls)
        if isinstance(self.smoother, AdaptiveControlSmoother):
            return self.smoother.update(calibrated, timestamp=timestamp)
        return self.smoother.update(calibrated)

    def no_hand(self, timestamp=None):
        if isinstance(self.smoother, AdaptiveControlSmoother):
            return self.smoother.update(CONTROL_DEFAULTS, timestamp=timestamp)
        return self.smoother.update(CONTROL_DEFAULTS)
