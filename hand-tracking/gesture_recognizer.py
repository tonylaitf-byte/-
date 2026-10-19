import math
import time


GESTURE_ACTIONS = {
    1: "BOW",
    2: "LANDSCAPE",
    3: "DRINK",
    4: "ALERT",
    5: "ATTACK",
}


def gesture_horizontal_target(landmarks, center=0.5, dead_zone=0.08, edge=0.38):
    """Map the palm center to -1..1 without using gesture-sensitive fingertips."""
    if landmarks is None or len(landmarks) < 18:
        return 0.0

    palm_x = sum(landmarks[index].x for index in (0, 5, 9, 13, 17)) / 5.0
    offset = palm_x - center
    magnitude = abs(offset)
    if magnitude <= dead_zone:
        return 0.0

    usable_range = max(0.01, edge - dead_zone)
    normalized = min(1.0, (magnitude - dead_zone) / usable_range)
    return math.copysign(normalized, offset)


class GestureMovementTracker:
    def __init__(self, response_speed=8.0):
        self.response_speed = max(0.1, float(response_speed))
        self.reset()

    def reset(self):
        self.value = 0.0
        self.last_time = None

    def update(self, landmarks, timestamp=None):
        timestamp = time.monotonic() if timestamp is None else float(timestamp)
        target = gesture_horizontal_target(landmarks)
        if self.last_time is None:
            delta_time = 1.0 / 30.0
        else:
            delta_time = min(0.25, max(1.0 / 240.0, timestamp - self.last_time))
        alpha = 1.0 - math.exp(-self.response_speed * delta_time)
        self.value += (target - self.value) * alpha
        if abs(self.value) < 0.002 and target == 0.0:
            self.value = 0.0
        self.last_time = timestamp
        return round(max(-1.0, min(1.0, self.value)), 4)


def _distance(first, second):
    return math.sqrt(
        (first.x - second.x) ** 2
        + (first.y - second.y) ** 2
        + (getattr(first, "z", 0.0) - getattr(second, "z", 0.0)) ** 2
    )


def _finger_extended(landmarks, tip, pip, mcp):
    return (
        landmarks[tip].y < landmarks[pip].y - 0.018
        and landmarks[pip].y < landmarks[mcp].y - 0.012
    )


def _thumb_state(landmarks):
    """Return (extended, clearly_tucked) using palm-relative distances.

    Four-vs-five is intentionally biased toward five unless the thumb tip is
    visibly inside the palm. This avoids an open palm becoming a four when the
    camera foreshortens the thumb.
    """
    palm_width = max(0.001, _distance(landmarks[5], landmarks[17]))
    palm_center = type("PalmPoint", (), {
        "x": sum(landmarks[index].x for index in (0, 5, 9, 13, 17)) / 5.0,
        "y": sum(landmarks[index].y for index in (0, 5, 9, 13, 17)) / 5.0,
        "z": sum(getattr(landmarks[index], "z", 0.0) for index in (0, 5, 9, 13, 17)) / 5.0,
    })()
    tip_from_index = _distance(landmarks[4], landmarks[5]) / palm_width
    tip_from_palm = _distance(landmarks[4], palm_center) / palm_width
    ip_from_palm = _distance(landmarks[3], palm_center) / palm_width

    clearly_tucked = tip_from_palm <= 0.78
    extended = (
        not clearly_tucked
        and tip_from_index >= 0.36
        and tip_from_palm >= ip_from_palm + 0.08
    )
    return extended, clearly_tucked


def classify_counting_gesture(landmarks):
    """Recognize the five deliberate finger-count gestures used by the show."""
    if landmarks is None or len(landmarks) < 21:
        return 0

    index = _finger_extended(landmarks, 8, 6, 5)
    middle = _finger_extended(landmarks, 12, 10, 9)
    ring = _finger_extended(landmarks, 16, 14, 13)
    pinky = _finger_extended(landmarks, 20, 18, 17)
    thumb, thumb_tucked = _thumb_state(landmarks)

    if index and middle and ring and pinky:
        return 4 if thumb_tucked else 5
    if index and middle and thumb and not ring and not pinky:
        return 3
    if index and middle and not thumb and not ring and not pinky:
        return 2
    if index and not middle and not ring and not pinky and not thumb:
        return 1
    return 0


class GestureActionRecognizer:
    def __init__(self, hold_seconds=0.5, cooldown_seconds=3.0):
        self.hold_seconds = max(0.1, float(hold_seconds))
        self.cooldown_seconds = max(0.0, float(cooldown_seconds))
        self.action_sequence = 0
        self.last_action = ""
        self.reset()

    def reset(self):
        self.candidate = 0
        self.candidate_since = None
        self.latched_gesture = 0
        self.last_trigger_time = {}
        self.last_action = ""

    def update(self, landmarks, timestamp=None):
        timestamp = time.monotonic() if timestamp is None else float(timestamp)
        gesture = classify_counting_gesture(landmarks)

        if gesture != self.candidate:
            self.candidate = gesture
            self.candidate_since = timestamp if gesture else None

        if gesture == 0:
            self.latched_gesture = 0
            hold_progress = 0.0
        else:
            held_for = max(0.0, timestamp - self.candidate_since)
            hold_progress = min(1.0, held_for / self.hold_seconds)

        triggered_action = ""
        if gesture and hold_progress >= 1.0 and gesture != self.latched_gesture:
            action = GESTURE_ACTIONS.get(gesture, "")
            last_time = self.last_trigger_time.get(action, float("-inf"))
            if action and timestamp - last_time >= self.cooldown_seconds:
                self.action_sequence += 1
                self.last_action = action
                self.last_trigger_time[action] = timestamp
                triggered_action = action
            self.latched_gesture = gesture

        return {
            "gesture": gesture,
            "gestureHold": round(hold_progress, 4),
            "detectedAction": self.last_action,
            "triggeredAction": triggered_action,
            "actionSequence": self.action_sequence,
        }
