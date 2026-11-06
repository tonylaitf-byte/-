"""Reproduce label flicker, moving calibration and delayed pre-reset frames."""
import copy
import math
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

import cv2
import numpy as np
import docker_app as service
from hand_identity import StableHandedness
from puppet_controls import CONTROL_DEFAULTS, NeutralPoseCalibrator, PuppetControlPipeline
from duet_mapping_tracking import DuetMappingRouter
import test_puppet_controls as samples
from test_duet_mapping import observation


class StableCalibrationTests(unittest.TestCase):
    def test_moving_or_slowly_drifting_pose_cannot_calibrate(self):
        for drift in (False, True):
            calibrator = NeutralPoseCalibrator()
            for index in range(150):
                value = index * .005 if drift else (-.8 if index % 2 else .8)
                calibrator.observe({**CONTROL_DEFAULTS, 'leftHandY': value}, index / 30)
                self.assertFalse(calibrator.calibrated)
            # Holding still afterwards must allow progress to recover.
            for index in range(46):
                calibrator.observe(CONTROL_DEFAULTS, 5 + index / 30)
            self.assertTrue(calibrator.calibrated)

    def test_small_tracking_noise_is_accepted(self):
        calibrator = NeutralPoseCalibrator()
        for index in range(46):
            calibrator.observe({key: .001 * math.sin(index) for key in CONTROL_DEFAULTS}, index / 30)
        self.assertTrue(calibrator.calibrated)

    def test_high_fps_cannot_shorten_stable_duration(self):
        calibrator = NeutralPoseCalibrator()
        for index in range(45):
            calibrator.observe(CONTROL_DEFAULTS, index / 60)
        self.assertFalse(calibrator.calibrated)
        for index in range(45, 90):
            calibrator.observe(CONTROL_DEFAULTS, index / 60)
        self.assertTrue(calibrator.calibrated)

    def test_loss_gap_and_invalid_data_restart_incomplete_window(self):
        for interruption in ('missing', 'gap', 'nan'):
            pipe = PuppetControlPipeline(calibration_samples=3)
            points = samples.PuppetControlsTests().make_landmarks()
            pipe.update(points, 0)
            pipe.update(points, .04)
            if interruption == 'missing':
                pipe.no_hand(.05)
                self.assertEqual(pipe.calibration_progress, 0)
            elif interruption == 'nan':
                pipe.calibrator.observe({**CONTROL_DEFAULTS, 'bodyX': math.nan}, .05)
            now = 1 if interruption == 'gap' else .08
            pipe.update(points, now)
            self.assertFalse(pipe.calibrated)
            self.assertAlmostEqual(pipe.calibration_progress, 1/3)
            pipe.update(points, now+.04)
            pipe.update(points, now+.08)
            self.assertTrue(pipe.calibrated)
            pipe.no_hand(now+.1)
            self.assertTrue(pipe.calibrated, 'Brief loss must retain an already-established neutral.')

    def test_duet_loss_resets_only_unfinished_side(self):
        router = DuetMappingRouter(calibration_samples=3)
        hands = [observation(.2), observation(.8)]
        router.update(hands, 0)
        router.update(hands, .04)
        state = router.update([hands[1]], .08)
        self.assertEqual(state['left']['controls']['calibrationProgress'], 0)
        self.assertTrue(state['right']['controls']['calibrated'])


class HandIdentityTests(unittest.TestCase):
    def test_label_flicker_and_low_confidence_do_not_switch(self):
        identity = StableHandedness()
        identity.update('Left', 0)
        for index in range(1, 30):
            self.assertFalse(identity.update('Right' if index % 2 else 'Left', index/30))
        for index in range(30, 60):
            self.assertFalse(identity.update('Right', index/30, .6))
        self.assertEqual(identity.label, 'Left')
        self.assertTrue(any(identity.update('Right', 2+index*.07) for index in range(5)))
        self.assertEqual(identity.label, 'Right')

    def test_short_duet_flicker_keeps_owner_and_calibration(self):
        for hybrid in (False, True):
            router = DuetMappingRouter(calibration_samples=3, hybrid=hybrid)
            hands = [observation(.2, 'Left'), observation(.8, 'Right')]
            for index in range(3):
                first = router.update(hands, index*.04)
            for index in range(3, 20):
                current = copy.deepcopy(hands)
                if index % 2:
                    current[0]['handedness'] = 'Right'
                    current[1]['handedness'] = 'Left'
                state = router.update(list(reversed(current)), index*.04)
                for side in ('left', 'right'):
                    self.assertEqual(state[side]['ownerId'], first[side]['ownerId'])
                    self.assertTrue(state[side]['controls']['calibrated'])
                    if hybrid:
                        self.assertEqual(state[side]['controls']['inputEpoch'], first[side]['controls']['inputEpoch'])


class TrackingTransportTests(unittest.TestCase):
    def setUp(self):
        self.client = service.app.test_client()

    def tearDown(self):
        if service.performance:
            self.client.post('/performance/stop', json={'sessionId': service.performance['sessionId']})

    def test_solo_label_flicker_keeps_calibration_but_confirmed_change_resets(self):
        points = samples.PuppetControlsTests().make_landmarks()
        frame = np.full((120,160,3), 120, np.uint8)
        for mode in ('mapping', 'hybrid'):
            self.client.post('/performance/start', json={'players':1, 'mode':mode})
            pipe = PuppetControlPipeline(calibration_samples=3)
            detector = Mock()
            def push(label, now):
                detector.process.return_value = SimpleNamespace(
                    multi_hand_landmarks=[SimpleNamespace(landmark=points)],
                    multi_handedness=[SimpleNamespace(classification=[SimpleNamespace(label=label, score=.99)])])
                with patch.object(service.time, 'monotonic', return_value=now):
                    service.process_browser_frame(frame.copy(), render_preview=False)
                return service.performance_snapshot()['state']
            with service.inference_lock, patch.object(service.core, 'pipeline', pipe), patch.object(service.core, 'hands', detector):
                for now in (1, 1.04, 1.08):
                    before = push('Left', now)
                self.assertTrue(before['calibrated'])
                for label, now in (('Right',1.12), ('Left',1.16), ('Right',1.2), ('Left',1.24)):
                    state = push(label, now)
                    self.assertTrue(state['calibrated'])
                    if mode == 'hybrid':
                        self.assertEqual(state['inputEpoch'], before['inputEpoch'])
                for index in range(5):
                    after = push('Right', 1.3+index*.07)
                self.assertFalse(after['calibrated'])
                if mode == 'hybrid':
                    self.assertGreater(after['inputEpoch'], before['inputEpoch'])

    def test_delayed_old_capture_never_enters_new_calibration(self):
        jpeg = cv2.imencode('.jpg', np.full((120,160,3),120,np.uint8))[1].tobytes()
        for players in (1, 2):
            for mode in ('mapping', 'hybrid'):
                with self.subTest(players=players, mode=mode):
                    session = self.client.post('/performance/start', json={'players':players, 'mode':mode}).get_json()
                    headers = {'X-Performance-Session':session['sessionId'], 'Accept':'application/json',
                               'X-Calibration-Revision':'0'}
                    reset = self.client.post('/performance/calibrate', json={'side':'left'}, headers=headers).get_json()
                    self.assertEqual(reset['calibrationRevision'], 1)
                    with patch.object(service, 'process_browser_frame', return_value=[]) as process:
                        stale = self.client.post('/frame', data=jpeg, content_type='image/jpeg', headers=headers).get_json()
                        self.assertTrue(stale['discardedFrame'])
                        process.assert_not_called()
                        headers['X-Calibration-Revision'] = '1'
                        fresh = self.client.post('/frame', data=jpeg, content_type='image/jpeg', headers=headers).get_json()
                        self.assertNotIn('discardedFrame', fresh)
                        process.assert_called_once()

    def test_duet_calibration_snapshot_preserves_other_actor(self):
        for hybrid in (False, True):
            mode = 'hybrid' if hybrid else 'mapping'
            self.client.post('/performance/start', json={'players':2, 'mode':mode})
            with service.inference_lock:
                for index in range(46):
                    before = service.publish_duet_mapping_state([observation(.2), observation(.8)], index/30)
            session = service.performance
            response = self.client.post('/performance/calibrate', json={'side':'left'},
                                        headers={'X-Performance-Session':session['sessionId']}).get_json()
            self.assertFalse(response['state']['left']['controls']['calibrated'])
            self.assertEqual(response['state']['right'], before['right'])


if __name__ == '__main__':
    unittest.main()
