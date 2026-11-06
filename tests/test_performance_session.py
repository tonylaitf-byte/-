import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1] / 'hand-tracking'))
import docker_app as service
import cv2
import numpy as np
from unittest.mock import patch
from types import SimpleNamespace


class PerformanceTest(unittest.TestCase):
    def setUp(self):
        self.client = service.app.test_client()

    def start(self, players=1, mode='gesture'):
        response = self.client.post('/performance/start', json={'players': players, 'mode': mode})
        self.assertEqual(response.status_code, 200)
        session = response.get_json()
        return session, {'X-Performance-Session': session['sessionId']}

    def tearDown(self):
        if service.performance:
            self.client.post('/performance/stop', json={'sessionId': service.performance['sessionId']})

    def test_all_four_modes_reset_on_every_entry(self):
        tokens = set()
        for _ in range(10):
            for players in (1, 2):
                for mode in ('gesture', 'mapping'):
                    session, headers = self.start(players, mode)
                    self.assertNotIn(session['sessionId'], tokens)
                    tokens.add(session['sessionId'])
                    response = self.client.get('/performance/state', headers=headers)
                    self.assertEqual(response.status_code, 200)
                    result = response.get_json()
                    self.assertEqual(result['sessionId'], session['sessionId'])
                    state = result['state']
                    self.assertEqual(state['controlMode'], mode if players == 1 else 'duet' if mode == 'gesture' else 'duet_mapping')
                    if players == 1:
                        self.assertFalse(state['handDetected'])
                        self.assertEqual(state['actionSequence'], 0)
                        self.assertFalse(state['calibrated'])
                        # Simulate a previous show with a pending event and calibration.
                        with service.core.state_lock:
                            service.core.puppet_state.update(actionSequence=9, handDetected=True, calibrated=True)
                    else:
                        self.assertFalse(state['left']['ownerHeld'])
                        self.assertFalse(state['right']['ownerHeld'])

    def test_old_frames_and_old_stop_cannot_mutate_new_show(self):
        old, old_headers = self.start()
        new, headers = self.start(2, 'mapping')
        image = cv2.imencode('.jpg', np.full((120, 160, 3), 120, np.uint8))[1].tobytes()
        self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg', headers=old_headers).status_code, 409)
        self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg').status_code, 409)
        self.assertEqual(self.client.post('/performance/stop', json={'sessionId': old['sessionId']}).status_code, 409)
        self.assertEqual(self.client.get('/performance/state', headers=old_headers).status_code, 409)
        self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg', headers=headers).status_code, 204)
        self.assertEqual(service.performance['sessionId'], new['sessionId'])
        self.assertEqual(self.client.post('/control_mode', json={'mode': 'gesture'}).status_code, 409)
        self.assertEqual(self.client.post('/calibrate').status_code, 409)
        self.assertEqual(self.client.post('/duet_mapping/calibrate', json={'side': 'left'}).status_code, 409)
        self.assertEqual(self.client.post('/performance/stop', headers=headers, json={}).status_code, 200)
        self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg', headers=headers).status_code, 409)
        self.assertEqual(self.client.post('/control_mode', json={'mode': 'gesture'}).status_code, 200)

    def test_calibration_and_invalid_settings(self):
        for players in (0, 3, '2', True, None):
            self.assertEqual(self.client.post('/performance/start', json={'players': players, 'mode': 'gesture'}).status_code, 400)
        self.assertEqual(self.client.post('/performance/start', json={'players': 1, 'mode': 'unknown'}).status_code, 400)
        _, headers = self.start(2, 'mapping')
        self.assertEqual(self.client.post('/performance/calibrate', headers=headers, json={'side': 'left'}).status_code, 200)
        self.assertEqual(self.client.post('/performance/calibrate', headers=headers, json={'side': 'right'}).status_code, 200)
        self.assertEqual(self.client.post('/performance/calibrate', headers=headers, json={'side': 'wrong'}).status_code, 400)
        _, gesture_headers = self.start(1, 'gesture')
        self.assertEqual(self.client.post('/performance/calibrate', headers=gesture_headers, json={}).status_code, 400)
        self.assertEqual(self.client.post('/performance/input-stop', headers=gesture_headers, json={}).status_code, 200)

    def test_compact_frames_return_atomic_state_without_jpeg_reencoding(self):
        image = cv2.imencode('.jpg', np.full((120, 160, 3), 120, np.uint8))[1].tobytes()
        for players in (1, 2):
            for mode in ('gesture', 'mapping'):
                session, headers = self.start(players, mode)
                headers['Accept'] = 'application/json'
                with patch.object(service.core, 'publish_frame') as encode:
                    response = self.client.post('/frame', data=image, content_type='image/jpeg', headers=headers)
                    self.assertEqual(response.status_code, 200)
                    encode.assert_not_called()
                result = response.get_json()
                self.assertEqual(result['sessionId'], session['sessionId'])
                self.assertEqual(result['state']['controlMode'], session['controlMode'])
                self.assertEqual(result['landmarks'], [])
                self.assertGreaterEqual(result['processingMs'], 0)
                self.assertEqual(result['state'], self.client.get('/performance/state', headers=headers).get_json()['state'])

    def test_compact_landmarks_use_mirrored_detector_coordinates(self):
        _, headers = self.start()
        headers['Accept'] = 'application/json'
        points = [SimpleNamespace(x=.23+i*.01, y=.4, z=0) for i in range(21)]
        detected = SimpleNamespace(multi_hand_landmarks=[SimpleNamespace(landmark=points)])
        image = cv2.imencode('.jpg', np.full((120,160,3),120,np.uint8))[1].tobytes()
        with patch.object(service.core.hands, 'process', return_value=detected), patch.object(service.core.mp_draw, 'draw_landmarks') as draw:
            result = self.client.post('/frame', data=image, content_type='image/jpeg', headers=headers).get_json()
            draw.assert_not_called()
        self.assertEqual(result['landmarks'][0][0], [.23,.4])
        self.assertEqual(len(result['landmarks'][0]),21)

    def test_pages_have_unified_entry_and_real_stage(self):
        home = self.client.get('/').get_data(as_text=True)
        self.assertIn('name="players"', home)
        self.assertIn('name="mode"', home)
        self.assertNotIn('name="character"', home)
        for players in (1, 2):
            for mode in ('gesture', 'mapping'):
                page = self.client.get(f'/control?players={players}&mode={mode}')
                self.assertEqual(page.status_code, 200)
                self.assertIn('id="unity-canvas"', page.get_data(as_text=True))
                self.assertIn('id="preview"', page.get_data(as_text=True))

    def test_old_camera_frames_cannot_resume_after_switch(self):
        image = cv2.imencode('.jpg', np.full((120, 160, 3), 120, np.uint8))[1].tobytes()
        for players in (1, 2):
            for mode in ('gesture', 'mapping', 'hybrid'):
                session, headers = self.start(players, mode)
                first = {**headers, 'X-Camera-Generation': '0', 'Accept': 'application/json'}
                self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg', headers=first).status_code, 200)
                stopped = self.client.post('/performance/input-stop', headers=headers, json={'cameraGeneration': 0})
                self.assertEqual(stopped.get_json()['cameraGeneration'], 1)
                self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg', headers=first).status_code, 410)
                self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg', headers=headers).status_code, 410)
                current = {**headers, 'X-Camera-Generation': '1', 'Accept': 'application/json'}
                self.assertEqual(self.client.post('/frame', data=image, content_type='image/jpeg', headers=current).status_code, 200)
                self.assertEqual(self.client.post('/performance/input-stop', headers=headers, json={'cameraGeneration': 0}).status_code, 410)
                self.assertEqual(self.client.get('/performance/state', headers=headers).get_json()['cameraGeneration'], 1)
                self.assertEqual(service.performance['sessionId'], session['sessionId'])


    def test_camera_switch_preserves_calibration_revision_and_discards_both_old_inputs(self):
        image = cv2.imencode('.jpg', np.full((120,160,3),120,np.uint8))[1].tobytes()
        for players in (1,2):
            for mode in ('mapping','hybrid'):
                _, headers = self.start(players,mode)
                self.client.post('/performance/calibrate',headers=headers,json={'side':'left'})
                stopped = self.client.post('/performance/input-stop',headers=headers,json={'cameraGeneration':0})
                self.assertEqual(stopped.get_json()['cameraGeneration'],1)
                snapshot = self.client.get('/performance/state',headers=headers).get_json()
                self.assertEqual(snapshot['calibrationRevision'],1)
                compact = {**headers,'Accept':'application/json','X-Camera-Generation':'1'}
                with patch.object(service,'process_browser_frame',return_value=[]) as process:
                    stale = self.client.post('/frame',headers={**compact,'X-Calibration-Revision':'0'},data=image).get_json()
                    self.assertTrue(stale['discardedFrame'])
                    process.assert_not_called()
                    self.assertEqual(self.client.post('/frame',headers={**compact,'X-Camera-Generation':'0','X-Calibration-Revision':'1'},data=image).status_code,410)
                    process.assert_not_called()
                    self.assertEqual(self.client.post('/frame',headers={**compact,'X-Calibration-Revision':'1'},data=image).status_code,200)
                    process.assert_called_once()

    def test_invalid_camera_generation_does_not_reset_input(self):
        _, headers = self.start(1,'hybrid')
        for value in (True,-1,'0',None,[]):
            self.assertEqual(self.client.post('/performance/input-stop',headers=headers,json={'cameraGeneration':value}).status_code,400)
            self.assertEqual(service.performance['cameraGeneration'],0)

if __name__ == '__main__':
    unittest.main(verbosity=2)
