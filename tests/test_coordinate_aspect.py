"""Aspect-ratio regression tests with synthetic geometry, without a camera."""
import copy
import math
import unittest
from types import SimpleNamespace
from unittest.mock import Mock, patch

import cv2
import numpy as np
import docker_app as service
from duet_mapping_tracking import DuetMappingRouter
from puppet_controls import CONTROL_DEFAULTS, PuppetControlPipeline, controls_from_landmarks
import test_puppet_controls as samples


SIZES = ((480, 480), (640, 480), (1280, 720), (480, 640))
SCREEN_KEYS = {'moveX', 'bodyX', 'bodyY'}


def poses():
    neutral = samples.PuppetControlsTests().make_landmarks()
    moved = copy.deepcopy(neutral)
    moved[4].y -= .04
    moved[4].z -= .03
    moved[12].y -= .04
    moved[12].z += .02
    moved[20].y += .04
    moved[20].z -= .03
    return neutral, moved


def encode(points, size, center_x=.5, scale=1.0):
    # Model the same physical hand at a fixed image height, with wider/narrower
    # camera frames. MediaPipe encodes x/z in width units and y in height units.
    aspect = size[0] / size[1]
    return [SimpleNamespace(x=center_x + (p.x-.5)*scale/aspect,
                            y=.5 + (p.y-.5)*scale, z=p.z*scale/aspect)
            for p in points]


class CoordinateAspectTests(unittest.TestCase):
    def assert_controls_equal(self, actual, expected, excluded=()):
        for key in CONTROL_DEFAULTS.keys() - set(excluded):
            self.assertAlmostEqual(actual[key], expected[key], places=4, msg=key)

    def test_raw_finger_geometry_and_palm_rotation_are_aspect_invariant(self):
        neutral, moved = poses()
        folded = copy.deepcopy(neutral)
        folded[4] = SimpleNamespace(x=.52, y=.65, z=-.01)
        folded[20] = SimpleNamespace(x=.55, y=.64, z=.01)
        folded[12] = SimpleNamespace(x=.68, y=.4, z=-.01)
        rotated = copy.deepcopy(moved)
        for p in rotated:
            x, y, z = p.x-.5, p.y-.6, p.z
            x, z = x*math.cos(.35)+z*math.sin(.35), -x*math.sin(.35)+z*math.cos(.35)
            p.x = .5 + x*math.cos(.2)-y*math.sin(.2)
            p.y = .6 + x*math.sin(.2)+y*math.cos(.2)
            p.z = z
        for pose in (moved, folded, rotated):
            expected = controls_from_landmarks(encode(pose, SIZES[0]), SIZES[0])
            for size in SIZES:
                with self.subTest(size=size, pose=pose is folded):
                    actual = controls_from_landmarks(encode(pose, size), size)
                    self.assert_controls_equal(actual, expected, SCREEN_KEYS | {'bodyDepth'})
                    self.assertAlmostEqual(actual['bodyDepth'] + math.log(size[0]/size[1]),
                                           expected['bodyDepth'], places=10)

    def test_calibrated_controls_match_across_aspects(self):
        neutral, moved = poses()
        outputs = []
        for size in SIZES:
            pipe = PuppetControlPipeline(alpha=1, calibration_samples=1)
            pipe.update(encode(neutral, size), timestamp=0, frame_size=size)
            outputs.append(pipe.update(encode(moved, size), timestamp=.1, frame_size=size))
        for output in outputs[1:]:
            self.assert_controls_equal(output, outputs[0])
        self.assertGreater(abs(outputs[0]['leftHandY']), .2)
        self.assertGreater(abs(outputs[0]['rightHandY']), .3)

    def test_screen_travel_and_original_overlay_coordinates_are_preserved(self):
        neutral, _ = poses()
        moved = copy.deepcopy(neutral)
        for point in moved:
            point.x += .08
            point.y -= .06
        original = copy.deepcopy(moved)
        outputs = []
        for size in SIZES:
            pipe = PuppetControlPipeline(alpha=1, calibration_samples=1)
            pipe.update(neutral, frame_size=size)
            outputs.append(pipe.update(moved, frame_size=size))
        for key in SCREEN_KEYS:
            self.assertGreater(abs(outputs[0][key]), .1)
            self.assertTrue(all(value[key] == outputs[0][key] for value in outputs))
        self.assertEqual(moved, original)

    def test_resolution_only_keeps_calibration_but_aspect_change_resets_it(self):
        neutral, _ = poses()
        pipe = PuppetControlPipeline(alpha=1, calibration_samples=2)
        for index in range(2):
            pipe.update(encode(neutral, (640, 480)), timestamp=index / 30, frame_size=(640, 480))
        pipe.update(encode(neutral, (1280, 960)), frame_size=(1280, 960))
        self.assertTrue(pipe.calibrated)
        controls = pipe.update(encode(neutral, (1280, 720)), frame_size=(1280, 720))
        self.assertFalse(pipe.calibrated)
        self.assertEqual(pipe.calibration_progress, .5)
        self.assertEqual(controls, CONTROL_DEFAULTS)

    def test_square_legacy_calls_and_invalid_dimensions(self):
        neutral, _ = poses()
        self.assertEqual(controls_from_landmarks(neutral), controls_from_landmarks(neutral, (640, 640)))
        for size in ((0, 480), (640, -1), (math.nan, 480), (640, math.inf)):
            with self.subTest(size=size), self.assertRaises(ValueError):
                controls_from_landmarks(neutral, size)


class CoordinateTransportTests(unittest.TestCase):
    def tearDown(self):
        if service.performance:
            service.app.test_client().post('/performance/stop', json={'sessionId': service.performance['sessionId']})

    def test_real_frame_endpoint_compensates_solo_duet_mapping_and_hybrid(self):
        neutral, moved = poses()
        client = service.app.test_client()
        for mode in ('mapping', 'hybrid'):
            for players in (1, 2):
                baseline = None
                for size in SIZES:
                    with self.subTest(mode=mode, players=players, size=size):
                        detector = Mock()
                        pipe = PuppetControlPipeline(alpha=1, calibration_samples=1)
                        router = DuetMappingRouter(calibration_samples=1, hybrid=mode == 'hybrid')
                        with patch.object(service.core, 'pipeline', pipe), \
                                patch.object(service, 'duet_mapping_router', router), \
                                patch.object(service.core, 'hands', detector), \
                                patch.object(service, 'duet_hands', detector):
                            session = client.post('/performance/start', json={'players': players, 'mode': mode}).get_json()
                            headers = {'X-Performance-Session': session['sessionId'], 'Accept': 'application/json'}
                            jpeg = cv2.imencode('.jpg', np.full((size[1], size[0], 3), 120, np.uint8))[1].tobytes()
                            for index, pose in enumerate((neutral, moved)):
                                centers = (.5,) if players == 1 else (.2, .8)
                                hands = [encode(pose, size, center, scale=.4) for center in centers]
                                detector.process.return_value = SimpleNamespace(
                                    multi_hand_landmarks=[SimpleNamespace(landmark=p) for p in hands],
                                    multi_handedness=None)
                                with patch.object(service.time, 'monotonic', return_value=1000 + index*.1):
                                    response = client.post('/frame', data=jpeg, content_type='image/jpeg', headers=headers)
                                self.assertEqual(response.status_code, 200)
                                result = response.get_json()
                                self.assertEqual(result['landmarks'][0][0], [round(hands[0][0].x, 5), round(hands[0][0].y, 5)])
                            state = result['state']
                            slots = [state] if players == 1 else [state[s]['controls'] for s in ('left', 'right')]
                            for slot in slots:
                                self.assertTrue(slot['calibrated'])
                                self.assertGreater(abs(slot['leftHandY']), .1)
                                self.assertGreater(abs(slot['rightHandY']), .1)
                            if baseline is None:
                                baseline = slots
                            else:
                                for actual, expected in zip(slots, baseline):
                                    for key in CONTROL_DEFAULTS:
                                        self.assertAlmostEqual(actual[key], expected[key], places=4, msg=key)
                            detector.process.assert_called()
                            client.post('/performance/stop', headers=headers, json={})

    def test_duet_aspect_change_resets_both_calibrations_and_hybrid_events(self):
        neutral, _ = poses()
        router = DuetMappingRouter(calibration_samples=2, hybrid=True)
        def hands(size):
            return [{'landmarks': encode(neutral, size, center, .4)} for center in (.2, .8)]
        for now in (1, 1.1):
            before = router.update(hands((640, 480)), now, frame_size=(640, 480))
        after = router.update(hands((1280, 720)), 1.2, frame_size=(1280, 720))
        for side in ('left', 'right'):
            self.assertTrue(before[side]['controls']['calibrated'])
            self.assertFalse(after[side]['controls']['calibrated'])
            self.assertGreater(after[side]['controls']['inputEpoch'], before[side]['controls']['inputEpoch'])
            self.assertEqual(after[side]['ownerId'], before[side]['ownerId'])
            self.assertEqual(after[side]['controls']['actionSequence'], 0)


if __name__ == '__main__':
    unittest.main()
