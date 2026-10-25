import copy
import unittest
from unittest.mock import patch

import docker_app as service


class ApiInputTest(unittest.TestCase):
    routes = (
        '/performance/start', '/performance/stop', '/performance/input-stop',
        '/performance/pause', '/performance/calibrate', '/control_mode',
        '/duet_mapping/calibrate', '/calibrate',
    )

    def setUp(self):
        self.client = service.app.test_client()
        response = self.client.post('/performance/start', json={'players': 1, 'mode': 'mapping'})
        self.assertEqual(response.status_code, 200)
        self.session = response.get_json()
        self.headers = {'X-Performance-Session': self.session['sessionId']}

    def tearDown(self):
        if service.performance:
            self.client.post('/performance/stop', json={'sessionId': service.performance['sessionId']})

    def assert_rejected_without_mutation(self, path, **kwargs):
        before = self.client.get('/performance/state', headers=self.headers).get_json()
        paused = service.performance_paused
        with patch.object(service.core.pipeline, 'reset_calibration', wraps=service.core.pipeline.reset_calibration) as reset:
            response = self.client.post(path, headers=self.headers, **kwargs)
            self.assertEqual(response.status_code, 400, response.get_data(as_text=True))
            self.assertIsInstance(response.get_json().get('error'), str)
            self.assertTrue(response.get_json()['error'])
            reset.assert_not_called()
        after = self.client.get('/performance/state', headers=self.headers).get_json()
        self.assertEqual(before, after)
        self.assertEqual(paused, service.performance_paused)

    def test_nonobjects_and_malformed_json_do_not_change_the_show(self):
        bodies = ('[1]', '[]', 'null', 'true', 'false', '1', '"text"', '""',
                  '{', '{"players":1,}', b'{"bad":"\xff"}')
        for path in self.routes:
            for body in bodies:
                with self.subTest(path=path, body=str(body)[:40]):
                    self.assert_rejected_without_mutation(path, data=body, content_type='application/json')

    def test_wrong_media_type_and_required_empty_bodies(self):
        for path in self.routes:
            for content_type in ('text/plain', 'application/x-www-form-urlencoded', None):
                with self.subTest(path=path, content_type=content_type):
                    self.assert_rejected_without_mutation(path, data='{}', content_type=content_type)
        for path in ('/performance/start', '/performance/pause', '/control_mode'):
            with self.subTest(path=path):
                self.assert_rejected_without_mutation(path, data=b'')

    def test_parser_recursion_failure_returns_400_without_stopping_show(self):
        before = self.client.get('/performance/state', headers=self.headers).get_json()
        with patch.object(service.app.json, 'loads', side_effect=RecursionError):
            response = self.client.post('/performance/stop', headers=self.headers,
                                        data='{}', content_type='application/json')
        self.assertEqual(response.status_code, 400)
        self.assertEqual(response.get_json()['error'], 'The request body is not valid JSON.')
        self.assertEqual(before, self.client.get('/performance/state', headers=self.headers).get_json())

    def test_field_types_are_rejected_before_state_changes(self):
        cases = (
            ('/performance/start', {'players': True, 'mode': 'mapping'}),
            ('/performance/start', {'players': [], 'mode': 'mapping'}),
            ('/performance/start', {'players': 1, 'mode': {}}),
            ('/performance/pause', {'paused': 'false'}),
            ('/performance/pause', {'paused': 1}),
            ('/performance/calibrate', {'side': []}),
            ('/duet_mapping/calibrate', {'side': {}}),
            ('/control_mode', {'mode': ['mapping']}),
            ('/control_mode', {'mode': None}),
        )
        for path, payload in cases:
            with self.subTest(path=path, payload=payload):
                self.assert_rejected_without_mutation(path, json=payload)
        for path in self.routes:
            for token in (None, [], {}, 1, True, '', ' '):
                with self.subTest(path=path, token=token):
                    self.assert_rejected_without_mutation(path, json={'sessionId': token})

    def test_header_only_commands_and_beacon_stop_remain_compatible(self):
        self.assertEqual(self.client.get('/performance/state', headers=self.headers).status_code, 200)
        for path in ('/performance/input-stop', '/performance/calibrate'):
            self.assertEqual(self.client.post(path, headers=self.headers).status_code, 200)
        self.assertEqual(self.client.post('/performance/pause', headers=self.headers, json={'paused': True}).status_code, 200)
        self.assertEqual(self.client.post('/performance/pause', headers=self.headers, json={'paused': False}).status_code, 200)
        response = self.client.post('/performance/stop', json={'sessionId': self.session['sessionId']},
                                    content_type='application/json')
        self.assertEqual(response.status_code, 200)
        self.assertIsNone(service.performance)
        self.assertEqual(self.client.post('/calibrate').status_code, 200)
        self.assertEqual(self.client.post('/duet_mapping/calibrate').status_code, 200)
        self.assertEqual(self.client.post('/control_mode', json={'mode': ' MAPPING '}).status_code, 200)

    def test_invalid_tokens_remain_conflicts_and_size_limit_is_preserved(self):
        self.assertEqual(self.client.post('/performance/stop', json={'sessionId': 'old-session'}).status_code, 409)
        self.assertEqual(self.client.get('/performance/state').status_code, 409)
        with patch.dict(service.app.config, MAX_CONTENT_LENGTH=32):
            response = self.client.post('/performance/start', data='{"padding":"' + 'x' * 100 + '"}',
                                        content_type='application/json')
            self.assertEqual(response.status_code, 413)
        self.assertEqual(service.performance['sessionId'], self.session['sessionId'])

    def test_native_calibration_validates_before_reset(self):
        client = service.core.app.test_client()
        for body in ('null', '[]', '"text"', '{'):
            with self.subTest(body=body), patch.object(service.core.pipeline, 'reset_calibration') as reset:
                before = copy.deepcopy(service.core.puppet_state)
                response = client.post('/calibrate', data=body, content_type='application/json')
                self.assertEqual(response.status_code, 400)
                self.assertTrue(response.get_json()['error'])
                reset.assert_not_called()
                self.assertEqual(before, service.core.puppet_state)
        self.assertEqual(client.post('/calibrate').status_code, 200)


if __name__ == '__main__':
    unittest.main()
