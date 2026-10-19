import copy
import math
import unittest
from types import SimpleNamespace

from puppet_controls import (
    AdaptiveControlSmoother,
    CONTROL_DEFAULTS,
    PAYLOAD_VERSION,
    ControlSmoother,
    PuppetControlPipeline,
    controls_from_landmarks,
    make_puppet_payload,
)


def point(x=0.0, y=0.0, z=0.0):
    return SimpleNamespace(x=x, y=y, z=z)


class PuppetControlsTests(unittest.TestCase):
    def make_landmarks(self):
        return [
            point(0.50, 0.82, 0.00),
            point(0.43, 0.72, -0.01),
            point(0.36, 0.65, -0.02),
            point(0.29, 0.59, -0.02),
            point(0.22, 0.53, -0.02),
            point(0.40, 0.58, 0.00),
            point(0.39, 0.45, -0.01),
            point(0.39, 0.32, -0.01),
            point(0.39, 0.20, -0.01),
            point(0.50, 0.55, 0.00),
            point(0.50, 0.40, -0.01),
            point(0.50, 0.27, -0.01),
            point(0.50, 0.14, -0.01),
            point(0.59, 0.58, 0.00),
            point(0.60, 0.45, -0.01),
            point(0.61, 0.33, -0.01),
            point(0.62, 0.22, -0.01),
            point(0.68, 0.62, 0.00),
            point(0.72, 0.52, -0.01),
            point(0.75, 0.42, -0.01),
            point(0.78, 0.32, -0.01),
        ]

    def calibrated_pipeline(self, landmarks=None, samples=3):
        landmarks = landmarks or self.make_landmarks()
        pipeline = PuppetControlPipeline(alpha=1.0, calibration_samples=samples)
        for _ in range(samples):
            pipeline.update(landmarks)
        self.assertTrue(pipeline.calibrated)
        return pipeline

    def test_straight_fingers_produce_neutral_legacy_controls(self):
        controls = controls_from_landmarks(self.make_landmarks())
        self.assertEqual(controls["headNod"], 0.0)
        self.assertEqual(controls["leftArm"], 0.0)
        self.assertEqual(controls["rightArm"], 0.0)

    def test_middle_finger_curl_controls_head_nod(self):
        landmarks = self.make_landmarks()
        landmarks[12] = point(0.68, 0.40, -0.01)
        controls = controls_from_landmarks(landmarks)
        self.assertGreater(controls["headNod"], 0.8)
        self.assertEqual(controls["leftArm"], 0.0)
        self.assertEqual(controls["rightArm"], 0.0)

    def test_thumb_tip_drives_only_left_hand_ik(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[4].x -= 0.08
        moved[4].y -= 0.06
        moved[4].z -= 0.08

        controls = pipeline.update(moved)

        self.assertGreater(abs(controls["leftHandX"]), 0.2)
        self.assertGreater(abs(controls["leftHandY"]), 0.2)
        self.assertGreater(abs(controls["leftHandZ"]), 0.2)
        self.assertAlmostEqual(controls["rightHandX"], 0.0, places=3)
        self.assertAlmostEqual(controls["headYaw"], 0.0, places=3)

    def test_pinky_tip_drives_only_right_hand_ik(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[20].x += 0.08
        moved[20].y -= 0.06
        moved[20].z -= 0.08

        controls = pipeline.update(moved)

        self.assertGreater(abs(controls["rightHandX"]), 0.2)
        self.assertGreater(abs(controls["rightHandY"]), 0.2)
        self.assertGreater(abs(controls["rightHandZ"]), 0.2)
        self.assertAlmostEqual(controls["leftHandX"], 0.0, places=3)
        self.assertAlmostEqual(controls["headYaw"], 0.0, places=3)

    def test_small_forward_motion_has_strong_depth_response(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[4].z -= 0.025
        moved[20].z -= 0.025

        controls = pipeline.update(moved)

        self.assertGreater(controls["leftHandZ"], 0.2)
        self.assertGreater(controls["rightHandZ"], 0.3)

    def test_pinky_is_more_sensitive_than_thumb_for_equal_motion(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[4].x -= 0.04
        moved[20].x += 0.04

        controls = pipeline.update(moved)

        self.assertGreater(
            abs(controls["rightHandX"]),
            abs(controls["leftHandX"]) * 1.2,
        )

    def test_small_landmark_tremor_stays_inside_dead_zone(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[4].x -= 0.001
        moved[20].x += 0.001
        moved[12].y += 0.001

        controls = pipeline.update(moved)

        self.assertEqual(controls["leftHandX"], 0.0)
        self.assertEqual(controls["rightHandX"], 0.0)
        self.assertEqual(controls["headPitch"], 0.0)

    def test_finger_base_shift_does_not_move_arm_target(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        for index in (2, 3, 4):
            moved[index].x -= 0.06
            moved[index].y += 0.04

        controls = pipeline.update(moved)

        self.assertAlmostEqual(controls["leftHandX"], 0.0, places=3)
        self.assertAlmostEqual(controls["leftHandY"], 0.0, places=3)

    def test_middle_tip_drives_three_axis_head_control(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[12].x -= 0.08
        moved[12].y += 0.08
        moved[12].z -= 0.08

        controls = pipeline.update(moved)

        self.assertGreater(abs(controls["headPitch"]), 0.2)
        self.assertGreater(abs(controls["headYaw"]), 0.2)
        self.assertGreater(abs(controls["headRoll"]), 0.2)
        self.assertAlmostEqual(controls["leftHandX"], 0.0, places=3)
        self.assertAlmostEqual(controls["rightHandX"], 0.0, places=3)

    def test_palm_translation_produces_shared_driver_without_changing_finger_directions(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        for landmark in moved:
            landmark.x += 0.15
            landmark.y -= 0.10

        controls = pipeline.update(moved)

        self.assertGreater(controls["bodyX"], 0.5)
        self.assertGreater(controls["bodyY"], 0.3)
        self.assertAlmostEqual(controls["leftHandX"], 0.0, places=3)
        self.assertAlmostEqual(controls["rightHandY"], 0.0, places=3)
        self.assertAlmostEqual(controls["headYaw"], 0.0, places=3)

    def test_palm_roll_controls_body_lean(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        center_x, center_y = 0.5, 0.58
        angle = math.radians(20.0)
        cosine, sine = math.cos(angle), math.sin(angle)
        for landmark in moved:
            x = landmark.x - center_x
            y = landmark.y - center_y
            landmark.x = center_x + x * cosine - y * sine
            landmark.y = center_y + x * sine + y * cosine

        controls = pipeline.update(moved)

        self.assertGreater(abs(controls["bodyLean"]), 0.3)

    def test_calibration_blocks_motion_until_neutral_is_ready(self):
        pipeline = PuppetControlPipeline(alpha=1.0, calibration_samples=3)
        first = pipeline.update(self.make_landmarks())
        self.assertFalse(pipeline.calibrated)
        self.assertAlmostEqual(pipeline.calibration_progress, 1.0 / 3.0)
        self.assertEqual(first, CONTROL_DEFAULTS)

    def test_calibration_uses_natural_thumb_and_pinky_position_as_neutral(self):
        neutral = self.make_landmarks()
        neutral[4] = point(0.25, 0.57, -0.02)
        neutral[20] = point(0.75, 0.37, -0.01)
        pipeline = self.calibrated_pipeline(neutral)

        controls = pipeline.update(copy.deepcopy(neutral))

        self.assertEqual(controls["leftArm"], 0.0)
        self.assertEqual(controls["rightArm"], 0.0)

    def test_fingertips_entering_palm_center_activate_forward_arm_gestures(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        curled = copy.deepcopy(neutral)
        curled[4] = point(0.43, 0.62, -0.01)
        curled[20] = point(0.59, 0.54, -0.01)

        controls = pipeline.update(curled)

        self.assertGreater(controls["leftArm"], 0.2)
        self.assertGreater(controls["rightArm"], 0.2)

    def test_fingertips_outside_palm_center_do_not_activate_forward_gesture(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[4] = point(0.13, 0.66, -0.01)
        moved[20] = point(0.88, 0.66, -0.01)

        controls = pipeline.update(moved)

        self.assertEqual(controls["leftArm"], 0.0)
        self.assertEqual(controls["rightArm"], 0.0)

    def test_lost_hand_returns_all_controls_to_neutral(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        moved[4].x -= 0.1
        self.assertNotEqual(pipeline.update(moved)["leftHandX"], 0.0)
        self.assertEqual(pipeline.no_hand(), CONTROL_DEFAULTS)

    def test_all_transmitted_controls_are_unity_safe_ranges(self):
        neutral = self.make_landmarks()
        pipeline = self.calibrated_pipeline(neutral)
        moved = copy.deepcopy(neutral)
        for index, landmark in enumerate(moved):
            landmark.x += 10.0 if index % 2 else -10.0
            landmark.y += 8.0 if index % 3 else -8.0
            landmark.z += 6.0 if index % 4 else -6.0

        controls = pipeline.update(moved)
        for value in controls.values():
            self.assertGreaterEqual(value, -1.0)
            self.assertLessEqual(value, 1.0)

    def test_smoothing_moves_toward_target(self):
        smoother = ControlSmoother(alpha=0.25)
        result = smoother.update({**CONTROL_DEFAULTS, "headPitch": 1.0})
        self.assertEqual(result["headPitch"], 0.25)

    def test_adaptive_smoothing_suppresses_jitter_but_tracks_fast_motion(self):
        smoother = AdaptiveControlSmoother()
        neutral = smoother.update(CONTROL_DEFAULTS, timestamp=1.0)
        jitter = smoother.update(
            {**CONTROL_DEFAULTS, "leftHandX": 0.02},
            timestamp=1.033,
        )
        fast = smoother.update(
            {**CONTROL_DEFAULTS, "leftHandX": 1.0},
            timestamp=1.066,
        )

        self.assertEqual(neutral["leftHandX"], 0.0)
        self.assertLess(jitter["leftHandX"], 0.01)
        self.assertGreater(fast["leftHandX"], 0.35)

    def test_adaptive_smoothing_is_stable_across_frame_rates(self):
        def simulate(frame_rate):
            smoother = AdaptiveControlSmoother()
            smoother.update(CONTROL_DEFAULTS, timestamp=0.0)
            result = CONTROL_DEFAULTS
            for frame in range(1, frame_rate + 1):
                progress = frame / frame_rate
                result = smoother.update(
                    {**CONTROL_DEFAULTS, "headYaw": progress},
                    timestamp=progress,
                )
            return result["headYaw"]

        self.assertAlmostEqual(simulate(30), simulate(60), delta=0.04)

    def test_unity_payload_contains_v2_metadata_and_legacy_values(self):
        payload = make_puppet_payload(
            True,
            {**CONTROL_DEFAULTS, "headPitch": 0.75},
            calibrated=True,
            calibration_progress=1.0,
        )
        self.assertEqual(payload["version"], PAYLOAD_VERSION)
        self.assertTrue(payload["handDetected"])
        self.assertTrue(payload["calibrated"])
        self.assertEqual(payload["calibrationProgress"], 1.0)
        self.assertEqual(payload["sequenceId"], 0)
        self.assertAlmostEqual(payload["sampleDt"], 1.0 / 30.0, places=4)
        self.assertEqual(payload["leftArmVelocity"], 0.0)
        self.assertEqual(payload["headPitch"], 0.75)
        self.assertIn("headNod", payload)
        self.assertIn("leftHandX", payload)
        self.assertIn("rightHandZ", payload)

    def test_payload_transmits_gesture_timing_and_velocity(self):
        payload = make_puppet_payload(
            True,
            CONTROL_DEFAULTS,
            calibrated=True,
            sequence_id=42,
            sample_dt=1.0 / 60.0,
            gesture_velocities={"leftArm": 2.5, "rightArm": -1.25},
        )

        self.assertEqual(payload["sequenceId"], 42)
        self.assertAlmostEqual(payload["sampleDt"], 1.0 / 60.0, places=4)
        self.assertEqual(payload["leftArmVelocity"], 2.5)
        self.assertEqual(payload["rightArmVelocity"], -1.25)


if __name__ == "__main__":
    unittest.main()
