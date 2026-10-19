"""Session boundaries for the integrated browser stage. All writes use inference_lock."""
import copy
import secrets
import time
from flask import jsonify, request


def install(service):
    app = service.app
    service.performance = None

    def reset_input():
        service.gesture_recognizer.reset()
        service.gesture_movement.reset()
        service.duet_router.reset()
        service.duet_mapping_router.reset()
        with service.core.pipeline_lock:
            service.core.pipeline.reset_calibration()
        service.core.set_puppet_state(False, service.core.CONTROL_DEFAULTS)
        now = time.monotonic()
        service.publish_duet_state([], now)
        service.publish_duet_mapping_state([], now)
        service.publish_gesture_state()
        with service.browser_lock:
            service.last_browser_frame_time = None
            service.browser_stream_active = False
        service.core.set_camera_status(False, False, None, 'Browser', 0.0, 'Waiting for browser camera frames.')

    def owns_session():
        payload = request.get_json(silent=True) or {}
        token = request.headers.get('X-Performance-Session') or payload.get('sessionId')
        return service.performance is not None and token == service.performance['sessionId']

    @app.post('/performance/start')
    def start():
        payload = request.get_json(silent=True) or {}
        players, mode = payload.get('players'), payload.get('mode')
        if type(players) is not int or players not in (1, 2) or mode not in ('gesture', 'mapping'):
            return {'error': '請選擇 1–2 人及手勢／映射模式。'}, 400
        backend_mode = mode if players == 1 else ('duet' if mode == 'gesture' else 'duet_mapping')
        with service.inference_lock:
            with service.control_mode_lock:
                service.control_mode = backend_mode
            reset_input()
            service.performance = {'sessionId': secrets.token_urlsafe(24), 'players': players,
                                   'mode': mode, 'controlMode': backend_mode}
            return jsonify(service.performance)

    def snapshot():
        # Caller holds inference_lock: frame result and session must be atomic.
        mode = service.performance['controlMode']
        if mode == 'duet':
            with service.duet_state_lock:
                current = copy.deepcopy(service.duet_state)
        elif mode == 'duet_mapping':
            with service.duet_mapping_state_lock:
                current = copy.deepcopy(service.duet_mapping_state)
        else:
            with service.core.state_lock:
                current = copy.deepcopy(service.core.puppet_state)
        current['controlMode'] = mode
        return {**service.performance, 'state': current}

    service.performance_snapshot = snapshot

    @app.get('/performance/state')
    def state():
        with service.inference_lock:
            if not owns_session():
                return {'error': '本次演出已結束或已由另一個頁面接手。'}, 409
            return jsonify(snapshot())

    @app.post('/performance/stop')
    def stop():
        with service.inference_lock:
            if not owns_session():
                return {'error': '演出已結束。'}, 409
            reset_input()
            service.performance = None
            return {'status': 'stopped'}

    @app.post('/performance/input-stop')
    def input_stop():
        with service.inference_lock:
            if not owns_session():
                return {'error': '演出已結束。'}, 409
            reset_input()
            return {'status': 'camera-off'}

    @app.post('/performance/calibrate', endpoint='performance_calibrate')
    def calibrate():
        payload = request.get_json(silent=True) or {}
        side = payload.get('side', 'both')
        if side not in ('left', 'right', 'both'):
            return {'error': '校正區域無效。'}, 400
        with service.inference_lock:
            if not owns_session():
                return {'error': '演出已結束。'}, 409
            if service.performance['mode'] != 'mapping':
                return {'error': '手勢模式不需要校正。'}, 400
            if service.performance['players'] == 2:
                service.duet_mapping_router.recalibrate(side)
                service.publish_duet_mapping_state([], time.monotonic())
            else:
                with service.core.pipeline_lock:
                    service.core.pipeline.reset_calibration()
                service.core.set_puppet_state(False, service.core.CONTROL_DEFAULTS)
                service.publish_gesture_state()
            return {'status': 'calibrating', 'side': side}
