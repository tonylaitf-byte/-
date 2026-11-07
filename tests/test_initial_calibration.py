"""Initial neutral capture must tolerate isolated noise without learning motion."""
import math
import unittest
from unittest.mock import patch

from puppet_controls import CONTROL_DEFAULTS, CONTROL_GAINS, NeutralPoseCalibrator


class InitialCalibrationTests(unittest.TestCase):
    def partial(self):
        calibrator = NeutralPoseCalibrator()
        for index in range(33):
            calibrator.observe(CONTROL_DEFAULTS, index / 30)
        self.assertGreater(calibrator.progress, .7)
        self.assertLess(calibrator.progress, .8)
        return calibrator

    def complete(self, calibrator):
        for index in range(35, 55):
            calibrator.observe(CONTROL_DEFAULTS, index / 30)
        self.assertTrue(calibrator.calibrated)

    def test_one_missing_frame_at_seventy_percent_pauses_not_resets(self):
        calibrator = self.partial()
        progress = calibrator.progress
        calibrator.interrupt(33 / 30)
        self.assertEqual(calibrator.progress, progress)
        calibrator.observe(CONTROL_DEFAULTS, 34 / 30)
        self.assertEqual(calibrator.progress, progress, 'Missing time must not count as stable time.')
        self.complete(calibrator)

    def test_outlier_at_seventy_percent_is_not_added_to_baseline(self):
        calibrator = self.partial()
        progress = calibrator.progress
        count = len(calibrator._samples)
        calibrator.observe({**CONTROL_DEFAULTS, 'rightHandZ': .6}, 33 / 30)
        self.assertEqual(calibrator.progress, progress)
        self.assertEqual(len(calibrator._samples), count)
        calibrator.observe(CONTROL_DEFAULTS, 34 / 30)
        self.assertEqual(calibrator.progress, progress)
        self.complete(calibrator)
        self.assertEqual(calibrator._baseline['rightHandZ'], 0)

    def test_long_loss_invalid_input_and_backward_time_clear_window(self):
        for kind in ('loss', 'nan', 'backward', 'gap'):
            with self.subTest(kind=kind):
                calibrator = self.partial()
                if kind == 'loss':
                    calibrator.interrupt(1.1)
                    calibrator.interrupt(1.4)
                elif kind == 'nan':
                    calibrator.observe({**CONTROL_DEFAULTS, 'rightHandZ': math.nan}, 1.1)
                else:
                    calibrator.observe(CONTROL_DEFAULTS, .5 if kind == 'backward' else 2)
                self.assertFalse(calibrator.calibrated)
                self.assertLess(calibrator.progress, .1)

    def test_recovery_after_loss_checks_pose_continuity(self):
        calibrator = self.partial()
        calibrator.interrupt(1.1)
        moved = {**CONTROL_DEFAULTS, 'bodyX': .4}
        for index in range(34, 38):
            calibrator.observe(moved, index / 30)
        self.assertFalse(calibrator.calibrated)
        self.assertLess(calibrator.progress, .2)
        for index in range(38, 90):
            calibrator.observe(moved, index / 30)
        self.assertTrue(calibrator.calibrated)
        self.assertEqual(calibrator._baseline['bodyX'], .4)

    def test_low_fps_uses_stable_time_and_minimum_sample_count(self):
        for fps in (30, 15, 10, 5, 2):
            with self.subTest(fps=fps):
                calibrator = NeutralPoseCalibrator()
                for index in range(int(6 * fps) + 1):
                    calibrator.observe(CONTROL_DEFAULTS, index / fps)
                    if calibrator.calibrated:
                        break
                self.assertTrue(calibrator.calibrated)
                self.assertGreaterEqual(index / fps, calibrator.stable_duration - 1e-9)
                self.assertGreaterEqual(index + 1, calibrator.minimum_samples)

    def test_high_fps_still_requires_full_stable_duration(self):
        calibrator = NeutralPoseCalibrator()
        for index in range(80):
            calibrator.observe(CONTROL_DEFAULTS, index / 120)
        self.assertFalse(calibrator.calibrated)

    def test_small_depth_jitter_is_independent_of_control_gain(self):
        for gain in (4, 100):
            with self.subTest(gain=gain), patch.dict(CONTROL_GAINS, rightHandZ=gain):
                calibrator = NeutralPoseCalibrator()
                for index in range(46):
                    calibrator.observe({**CONTROL_DEFAULTS, 'rightHandZ': .02 * math.sin(index)}, index / 30)
                self.assertTrue(calibrator.calibrated)

    def test_sustained_motion_slow_drift_and_frequent_outliers_never_calibrate(self):
        for kind in ('alternating', 'drift', 'outliers'):
            with self.subTest(kind=kind):
                calibrator = NeutralPoseCalibrator()
                for index in range(300):
                    if kind == 'alternating':
                        value = .8 if index % 2 else -.8
                    elif kind == 'drift':
                        value = .005 * index
                    else:
                        value = .8 if index % 3 == 0 else 0
                    calibrator.observe({**CONTROL_DEFAULTS, 'leftHandY': value}, index / 30)
                    self.assertFalse(calibrator.calibrated)

    def test_regular_isolated_detection_loss_can_finish(self):
        calibrator = NeutralPoseCalibrator()
        for index in range(90):
            if index and index % 30 == 0:
                calibrator.interrupt(index / 30)
            else:
                calibrator.observe(CONTROL_DEFAULTS, index / 30)
            if calibrator.calibrated:
                break
        self.assertTrue(calibrator.calibrated)

    def test_manual_reset_does_not_reuse_held_samples(self):
        calibrator = self.partial()
        calibrator.interrupt(1.1)
        calibrator.reset()
        calibrator.observe(CONTROL_DEFAULTS, 1.2)
        self.assertFalse(calibrator.calibrated)
        self.assertEqual(len(calibrator._samples), 1)


if __name__ == '__main__':
    unittest.main()
