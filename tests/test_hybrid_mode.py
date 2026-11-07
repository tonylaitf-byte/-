"""Hybrid transport and event-boundary checks using real routing and fake detections."""
import sys
import unittest
from pathlib import Path
from types import SimpleNamespace
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'hand-tracking'))
sys.path.insert(0, str(Path(__file__).resolve().parent / 'legacy'))
import cv2
import numpy as np
import docker_app as service
from puppet_controls import CONTROL_DEFAULTS
from test_gesture_recognizer import hand
from duet_mapping_tracking import DuetMappingRouter
from duet_tracking import palm_center
from gesture_recognizer import HybridGestureActionRecognizer


class CalibratedPipeline:
    calibrated = True
    calibration_progress = 1.0
    gesture_velocities = {}

    def __init__(self, **unused):
        pass

    def set_frame_size(self, frame_size):
        return False

    def update(self, points, timestamp=None, frame_size=None):
        return {**CONTROL_DEFAULTS, 'bodyX': .3, 'moveX': .3}

    def no_hand(self, timestamp=None):
        return CONTROL_DEFAULTS.copy()

    def reset_calibration(self):
        self.calibrated = False


class HybridModeTests(unittest.TestCase):
    def setUp(self):
        self.client = service.app.test_client()
        self.frame = np.full((120, 160, 3), 120, np.uint8)

    def tearDown(self):
        if service.performance:
            self.client.post('/performance/stop', json={'sessionId': service.performance['sessionId']})

    def start(self, players=1):
        response = self.client.post('/performance/start', json={'players': players, 'mode': 'hybrid'})
        self.assertEqual(response.status_code, 200, response.get_data(as_text=True))
        session = response.get_json()
        return session, {'X-Performance-Session': session['sessionId']}

    def slots(self, snapshot):
        state = snapshot['state']
        return [state] if snapshot['players'] == 1 else [state[side]['controls'] for side in ('left', 'right')]

    def push(self, points, now):
        detector = Mock()
        detector.process.return_value = SimpleNamespace(
            multi_hand_landmarks=[SimpleNamespace(landmark=points)], multi_handedness=None)
        with patch.object(service.core, 'hands', detector), \
                patch.object(service.core, 'pipeline', CalibratedPipeline()), \
                patch.object(service.time, 'monotonic', return_value=now):
            service.process_browser_frame(self.frame.copy(), render_preview=False)
        detector.process.assert_called_once()
        return service.performance_snapshot()['state']

    def test_start_shapes_and_old_session_rejection(self):
        old, old_headers = self.start()
        for players in (1, 2):
            session, headers = self.start(players)
            snapshot = self.client.get('/performance/state', headers=headers).get_json()
            self.assertEqual(snapshot['mode'], 'hybrid')
            self.assertEqual(snapshot['state']['controlMode'], 'hybrid' if players == 1 else 'duet_hybrid')
            for slot in self.slots(snapshot):
                self.assertEqual(slot['version'], 2)
                self.assertEqual(slot['controlMode'], 'hybrid')
                self.assertIsInstance(slot['inputEpoch'], int)
                self.assertEqual(slot['actionSequence'], 0)
                self.assertEqual(slot['action'], '')
                self.assertFalse(slot['calibrated'])
            self.assertNotEqual(session['sessionId'], old['sessionId'])
        self.assertEqual(self.client.get('/performance/state', headers=old_headers).status_code, 409)

    def test_one_detection_produces_mapping_and_gesture_without_initial_attack(self):
        self.start()
        with service.inference_lock:
            for now in (1000, 1000.6, 1004):
                state = self.push(hand({'thumb', 'index', 'middle', 'ring', 'pinky'}), now)
                self.assertTrue(state['calibrated'])
                self.assertAlmostEqual(state['bodyX'], .3, places=3)
                self.assertEqual(state['gesture'], 5)
                self.assertEqual(state['actionSequence'], 0)
                self.assertEqual(state['action'], '')

    def test_release_hold_cooldown_and_stable_event_identity(self):
        self.start()
        with service.inference_lock:
            self.push(hand(set()), 1000)
            self.push(hand(set()), 1000.25)
            self.push(hand({'index'}), 1000.3)
            first = self.push(hand({'index'}), 1000.85)
            self.assertEqual((first['actionSequence'], first['action']), (1, 'BOW'))
            self.assertEqual(self.push(hand({'index'}), 1004)['actionSequence'], 1)
            # A different command without a release must not replace the last event.
            candidate = self.push(hand({'index', 'middle'}), 1004.1)
            held = self.push(hand({'index', 'middle'}), 1004.8)
            self.assertEqual((candidate['actionSequence'], candidate['action']), (1, 'BOW'))
            self.assertEqual((held['actionSequence'], held['action']), (1, 'BOW'))
            self.push(hand(set()), 1005)
            self.push(hand(set()), 1005.25)
            self.push(hand({'index', 'middle'}), 1005.3)
            second = self.push(hand({'index', 'middle'}), 1005.85)
            self.assertEqual((second['actionSequence'], second['action']), (2, 'LANDSCAPE'))

    def test_calibrate_one_side_advances_only_its_epoch(self):
        _, headers = self.start(2)
        before = self.client.get('/performance/state', headers=headers).get_json()['state']
        response = self.client.post('/performance/calibrate', headers=headers, json={'side': 'right'})
        self.assertEqual(response.status_code, 200)
        after = self.client.get('/performance/state', headers=headers).get_json()['state']
        self.assertGreater(after['right']['controls']['inputEpoch'], before['right']['controls']['inputEpoch'])
        self.assertEqual(after['left']['controls']['inputEpoch'], before['left']['controls']['inputEpoch'])
        self.assertEqual(after['right']['controls']['actionSequence'], 0)

    def test_brief_solo_occlusion_keeps_epoch_and_calibration_until_grace_expires(self):
        self.start()
        pipeline = CalibratedPipeline()
        with service.inference_lock, patch.object(service.core, 'pipeline', pipeline):
            self.push(hand(set()), 1000)
            state = self.push(hand(set()), 1000.25)
            epoch = state['inputEpoch']
            service.publish_no_hand(1000.5)
            brief = service.performance_snapshot()['state']
            self.assertEqual(brief['inputEpoch'], epoch)
            self.assertTrue(brief['calibrated'])
            self.assertFalse(brief['handDetected'])
            self.assertFalse(brief['gestureArmed'])
            service.publish_no_hand(1001.5)
            lost = service.performance_snapshot()['state']
            self.assertGreater(lost['inputEpoch'], epoch)
            self.assertFalse(lost['calibrated'])

    def test_camera_restart_advances_epochs_and_requires_new_calibration(self):
        _, headers = self.start(2)
        before = self.slots(self.client.get('/performance/state', headers=headers).get_json())
        self.assertEqual(self.client.post('/performance/input-stop', headers=headers, json={}).status_code, 200)
        after = self.slots(self.client.get('/performance/state', headers=headers).get_json())
        for old, new in zip(before, after):
            self.assertGreater(new['inputEpoch'], old['inputEpoch'])
            self.assertFalse(new['calibrated'])
            self.assertEqual(new['actionSequence'], 0)

    def test_pause_is_session_protected_and_validated(self):
        old, old_headers = self.start()
        _, headers = self.start()
        self.assertEqual(self.client.post('/performance/pause', headers=old_headers, json={'paused': True}).status_code, 409)
        self.assertEqual(self.client.post('/performance/pause', headers=headers, json={'paused': 'yes'}).status_code, 400)
        for paused in (True, False):
            self.assertEqual(self.client.post('/performance/pause', headers=headers, json={'paused': paused}).status_code, 200)


class HybridRouterTests(unittest.TestCase):
    def test_cooldowns_are_per_action_not_global(self):
        recognizer = HybridGestureActionRecognizer()
        def neutral(now):
            recognizer.update(hand(set()), now)
            recognizer.update(hand(set()), now + .25)
        neutral(1)
        recognizer.update(hand({'index'}), 1.3)
        self.assertEqual(recognizer.update(hand({'index'}), 1.85)['actionSequence'], 1)
        neutral(2)
        recognizer.update(hand({'index', 'middle'}), 2.3)
        state = recognizer.update(hand({'index', 'middle'}), 2.85)
        self.assertEqual((state['actionSequence'], state['action']), (2, 'LANDSCAPE'))
        neutral(3)
        recognizer.update(hand({'index'}), 3.3)
        self.assertEqual(recognizer.update(hand({'index'}), 3.85)['actionSequence'], 2)
        neutral(5)
        recognizer.update(hand({'index'}), 5.3)
        self.assertEqual(recognizer.update(hand({'index'}), 5.85)['actionSequence'], 3)

    def test_disabled_calibration_cannot_accumulate_neutral_or_command_hold(self):
        recognizer = HybridGestureActionRecognizer()
        recognizer.update(hand(set()), 1, enabled=False)
        recognizer.update(hand(set()), 2, enabled=False)
        recognizer.update(hand({'index'}), 3, enabled=False)
        state = recognizer.update(hand({'index'}), 4, enabled=True)
        self.assertFalse(state['gestureArmed'])
        self.assertEqual(state['actionSequence'], 0)
        self.assertEqual(recognizer.update(hand({'index'}), 5)['actionSequence'], 0)

    @staticmethod
    def observation(x, extended, handedness):
        points = hand(extended)
        offset = x - palm_center(points)[0]
        return {'landmarks': [SimpleNamespace(x=p.x + offset, y=p.y, z=p.z) for p in points],
                'handedness': handedness}

    def test_no_hand_and_pause_never_arm_or_complete_old_hold(self):
        recognizer = HybridGestureActionRecognizer()
        recognizer.update(hand(set()), 1)
        self.assertTrue(recognizer.update(hand(set()), 1.25)['gestureArmed'])
        recognizer.update(hand({'index'}), 1.3)
        recognizer.suspend()
        self.assertEqual(recognizer.update(hand({'index'}), 2)['actionSequence'], 0)
        recognizer.update(None, 3)
        self.assertFalse(recognizer.update(None, 4)['gestureArmed'])
        self.assertEqual(recognizer.update(hand({'index'}), 5)['actionSequence'], 0)
        recognizer.update(hand(set()), 6)
        recognizer.update(hand(set()), 6.25)
        recognizer.update(hand({'index'}), 6.3)
        self.assertEqual(recognizer.update(hand({'index'}), 6.85)['action'], 'BOW')

    def test_duet_events_follow_mapping_owner_and_recalibration_is_independent(self):
        with patch('duet_mapping_tracking.PuppetControlPipeline', CalibratedPipeline):
            router = DuetMappingRouter(hybrid=True)
            neutral = [self.observation(.2, set(), 'Left'), self.observation(.8, set(), 'Right')]
            for now in (1, 1.1, 1.35):
                state = router.update(neutral, now)
            owners = [state[side]['ownerId'] for side in ('left', 'right')]
            commands = [self.observation(.2, {'index'}, 'Left'),
                        self.observation(.8, {'index', 'middle'}, 'Right')]
            router.update(commands, 1.4)
            state = router.update(list(reversed(commands)), 1.95)
            self.assertEqual([state[side]['ownerId'] for side in ('left', 'right')], owners)
            self.assertEqual(state['left']['controls']['action'], 'BOW')
            self.assertEqual(state['right']['controls']['action'], 'LANDSCAPE')
            self.assertEqual(state['left']['controls']['actionSequence'], 1)
            self.assertEqual(state['right']['controls']['actionSequence'], 1)
            self.assertNotEqual(state['left']['controls']['inputEpoch'], state['right']['controls']['inputEpoch'])
            before = state
            router.recalibrate('right')
            after = router.snapshot()
            self.assertGreater(after['right']['controls']['inputEpoch'], before['right']['controls']['inputEpoch'])
            self.assertEqual(after['right']['controls']['actionSequence'], 0)
            self.assertEqual(after['right']['controls']['action'], '')
            self.assertEqual(after['left']['controls'], before['left']['controls'])

    def test_duet_loss_releases_event_identity_and_new_owner_must_rearm(self):
        with patch('duet_mapping_tracking.PuppetControlPipeline', CalibratedPipeline):
            router = DuetMappingRouter(hybrid=True)
            pose = [self.observation(.2, {'index'}, 'Left')]
            first = router.update(pose, 1)['left']
            router.update([], 2.2)
            replacement = router.update(pose, 2.3)['left']
            self.assertNotEqual(replacement['ownerId'], first['ownerId'])
            self.assertGreater(replacement['controls']['inputEpoch'], first['controls']['inputEpoch'])
            self.assertEqual(router.update(pose, 3)['left']['controls']['actionSequence'], 0)


if __name__ == '__main__':
    unittest.main(verbosity=2)
