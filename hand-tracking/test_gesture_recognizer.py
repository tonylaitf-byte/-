import unittest
from types import SimpleNamespace

from gesture_recognizer import (
    GestureActionRecognizer,
    GestureMovementTracker,
    classify_counting_gesture,
    gesture_horizontal_target,
)


def point(x, y, z=0.0):
    return SimpleNamespace(x=x, y=y, z=z)


def hand(extended):
    points = [point(0.5, 0.82) for _ in range(21)]
    points[0] = point(0.5, 0.9)
    finger_data = {
        "index": (5, 6, 8, 0.42),
        "middle": (9, 10, 12, 0.50),
        "ring": (13, 14, 16, 0.58),
        "pinky": (17, 18, 20, 0.66),
    }
    for name, (mcp, pip, tip, x) in finger_data.items():
        points[mcp] = point(x, 0.66)
        points[pip] = point(x, 0.58)
        points[tip] = point(x, 0.38 if name in extended else 0.68)

    points[5] = point(0.42, 0.66)
    points[3] = point(0.40, 0.66)
    points[4] = point(0.18, 0.62) if "thumb" in extended else point(0.42, 0.69)
    return points


class GestureRecognizerTests(unittest.TestCase):
    def test_exact_finger_counts_map_to_five_gestures(self):
        self.assertEqual(classify_counting_gesture(hand({"index"})), 1)
        self.assertEqual(classify_counting_gesture(hand({"index", "middle"})), 2)
        self.assertEqual(
            classify_counting_gesture(hand({"thumb", "index", "middle"})),
            3,
        )
        self.assertEqual(
            classify_counting_gesture(hand({"index", "middle", "ring", "pinky"})),
            4,
        )
        self.assertEqual(
            classify_counting_gesture(
                hand({"thumb", "index", "middle", "ring", "pinky"})
            ),
            5,
        )

    def test_four_and_five_trigger_remapped_actions(self):
        recognizer = GestureActionRecognizer(hold_seconds=0.5, cooldown_seconds=0.0)
        four = hand({"index", "middle", "ring", "pinky"})
        five = hand({"thumb", "index", "middle", "ring", "pinky"})
        recognizer.update(four, 1.0)
        self.assertEqual(recognizer.update(four, 1.5)["triggeredAction"], "ALERT")
        recognizer.update(five, 2.0)
        self.assertEqual(recognizer.update(five, 2.5)["triggeredAction"], "ATTACK")

    def test_all_five_gestures_trigger_requested_actions(self):
        cases = (
            ({"index"}, "BOW"),
            ({"index", "middle"}, "LANDSCAPE"),
            ({"thumb", "index", "middle"}, "DRINK"),
            ({"index", "middle", "ring", "pinky"}, "ALERT"),
            ({"thumb", "index", "middle", "ring", "pinky"}, "ATTACK"),
        )
        for extended, expected_action in cases:
            with self.subTest(expected_action=expected_action):
                recognizer = GestureActionRecognizer(
                    hold_seconds=0.5,
                    cooldown_seconds=0.0,
                )
                pose = hand(extended)
                recognizer.update(pose, 1.0)
                self.assertEqual(
                    recognizer.update(pose, 1.5)["triggeredAction"],
                    expected_action,
                )

    def test_open_palm_tolerates_foreshortened_thumb(self):
        open_palm = hand({"thumb", "index", "middle", "ring", "pinky"})
        open_palm[3] = point(0.39, 0.64)
        open_palm[4] = point(0.33, 0.60)
        self.assertEqual(classify_counting_gesture(open_palm), 5)

    def test_four_requires_thumb_tip_clearly_inside_palm(self):
        four = hand({"index", "middle", "ring", "pinky"})
        four[4] = point(0.50, 0.69)
        self.assertEqual(classify_counting_gesture(four), 4)

    def test_ring_or_pinky_rejects_supported_gesture(self):
        self.assertEqual(
            classify_counting_gesture(hand({"index", "middle", "ring"})),
            0,
        )

    def test_requires_half_second_hold_and_latches(self):
        recognizer = GestureActionRecognizer(hold_seconds=0.5, cooldown_seconds=0.0)
        pose = hand({"index"})
        self.assertEqual(recognizer.update(pose, 1.0)["triggeredAction"], "")
        self.assertEqual(recognizer.update(pose, 1.49)["triggeredAction"], "")
        self.assertEqual(recognizer.update(pose, 1.50)["triggeredAction"], "BOW")
        self.assertEqual(recognizer.update(pose, 2.00)["triggeredAction"], "")

    def test_changing_gesture_can_queue_next_action(self):
        recognizer = GestureActionRecognizer(hold_seconds=0.5, cooldown_seconds=0.0)
        one = hand({"index"})
        two = hand({"index", "middle"})
        recognizer.update(one, 1.0)
        self.assertEqual(recognizer.update(one, 1.5)["triggeredAction"], "BOW")
        recognizer.update(two, 1.6)
        self.assertEqual(recognizer.update(two, 2.1)["triggeredAction"], "LANDSCAPE")

    def test_palm_center_controls_horizontal_movement(self):
        centered = hand({"index"})
        self.assertAlmostEqual(gesture_horizontal_target(centered), 0.0, places=3)

        left = hand({"index"})
        right = hand({"index"})
        for point_ in left:
            point_.x -= 0.30
        for point_ in right:
            point_.x += 0.30
        self.assertLess(gesture_horizontal_target(left), -0.5)
        self.assertGreater(gesture_horizontal_target(right), 0.5)

    def test_movement_is_smoothed_and_returns_toward_center_when_lost(self):
        tracker = GestureMovementTracker(response_speed=8.0)
        right = hand({"index"})
        for point_ in right:
            point_.x += 0.30
        first = tracker.update(right, timestamp=1.0)
        second = tracker.update(right, timestamp=1.033)
        returning = tracker.update(None, timestamp=1.066)
        self.assertGreater(first, 0.0)
        self.assertGreater(second, first)
        self.assertLess(returning, second)


if __name__ == "__main__":
    unittest.main()
