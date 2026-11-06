"""Use asymmetric frames to check actual detector input in all six modes."""
from types import SimpleNamespace
from unittest.mock import Mock, patch
import unittest
import cv2
import numpy as np
import docker_app as service


class CameraDirectionTests(unittest.TestCase):
    def test_solo_is_original_and_duet_is_mirrored_in_every_mode(self):
        client = service.app.test_client()
        image = np.zeros((160, 240, 3), np.uint8)
        image[:, :80] = (30, 90, 230)
        image[:, 80:] = (220, 180, 50)
        jpeg = cv2.imencode('.jpg', image)[1].tobytes()
        decoded = cv2.imdecode(np.frombuffer(jpeg, np.uint8), cv2.IMREAD_COLOR)
        try:
            for players in (1, 2):
                for mode in ('gesture', 'mapping', 'hybrid'):
                    with self.subTest(players=players, mode=mode):
                        detector = Mock()
                        detector.process.return_value = SimpleNamespace(multi_hand_landmarks=[], multi_handedness=[])
                        with patch.object(service.core, 'hands', detector), patch.object(service, 'duet_hands', detector):
                            session = client.post('/performance/start', json={'players': players, 'mode': mode}).get_json()
                            response = client.post('/frame', data=jpeg, content_type='image/jpeg', headers={
                                'X-Performance-Session': session['sessionId'], 'Accept': 'application/json'})
                            self.assertEqual(response.status_code, 200)
                            expected = cv2.flip(decoded, 1) if players == 2 else decoded
                            np.testing.assert_array_equal(detector.process.call_args.args[0],
                                                          cv2.cvtColor(expected, cv2.COLOR_BGR2RGB))
        finally:
            if service.performance:
                client.post('/performance/stop', json={'sessionId': service.performance['sessionId']})
