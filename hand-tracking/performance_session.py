"""Session boundaries for the integrated browser stage. All writes use inference_lock."""
import copy
import secrets
import time
from flask import jsonify, request
from api_input import json_object


def install(service):
    app = service.app
    service.performance = None
    service.performance_paused = False

    def reset_input():
        service.performance_paused = False
        service.gesture_recognizer.reset()
        service.reset_solo_hybrid()
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
        if service.get_control_mode() == 'hybrid':
            service.publish_hybrid_state()
        with service.browser_lock:
            service.last_browser_frame_time = None
            service.browser_stream_active = False
        service.core.set_camera_status(False, False, None, 'Browser', 0.0, 'Waiting for browser camera frames.')

    def owns_session():
        payload = json_object(allow_empty=True)
        token = request.headers.get('X-Performance-Session') or payload.get('sessionId')
        return service.performance is not None and token == service.performance['sessionId']

    @app.post('/performance/start')
    def start():
        payload = json_object()
        players, mode = payload.get('players'), payload.get('mode')
        if type(players) is not int or players not in (1, 2) or mode not in ('gesture', 'mapping', 'hybrid'):
            return {'error': '請選擇 1–2 人及手勢／映射／混合模式。'}, 400
        backend_mode = mode if players == 1 else {'gesture':'duet', 'mapping':'duet_mapping', 'hybrid':'duet_hybrid'}[mode]
        with service.inference_lock:
            with service.control_mode_lock:
                service.control_mode = backend_mode
            service.duet_mapping_router.set_hybrid(backend_mode == 'duet_hybrid')
            reset_input()
            service.performance = {'sessionId': secrets.token_urlsafe(24), 'players': players,
                                   'mode': mode, 'controlMode': backend_mode, 'calibrationRevision': 0}
            return jsonify(service.performance)

    def snapshot():
        # Caller holds inference_lock: frame result and session must be atomic.
        mode = service.performance['controlMode']
        if mode == 'duet':
            with service.duet_state_lock:
                current = copy.deepcopy(service.duet_state)
        elif mode in ('duet_mapping', 'duet_hybrid'):
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

    @app.post('/performance/pause')
    def pause():
        payload = json_object()
        paused = payload.get('paused')
        if type(paused) is not bool:
            return {'error': 'paused must be a boolean'}, 400
        with service.inference_lock:
            if not owns_session():
                return {'error': '演出已結束。'}, 409
            service.performance_paused = paused
            if service.performance['mode'] == 'hybrid':
                if service.performance['players'] == 2:
                    service.duet_mapping_router.suspend()
                    if not paused:
                        # Pausing is not an ownership loss; restart the grace clock.
                        with service.duet_mapping_router.ownership.lock:
                            for track in service.duet_mapping_router.ownership.tracks.values():
                                track['seen'] = time.monotonic()
                    service.refresh_duet_mapping_state()
                else:
                    service.hybrid_recognizer.suspend()
                    if not paused and service.hybrid_last_seen is not None:
                        service.hybrid_last_seen = time.monotonic()
                    service.publish_hybrid_state()
                with service.browser_lock:
                    service.last_browser_frame_time = None if paused else time.monotonic()
                    service.browser_stream_active = False
            return {'status': 'paused' if paused else 'running', 'paused': paused}

    @app.post('/performance/calibrate', endpoint='performance_calibrate')
    def calibrate():
        payload = json_object(allow_empty=True)
        side = payload.get('side', 'both')
        if side not in ('left', 'right', 'both'):
            return {'error': '校正區域無效。'}, 400
        with service.inference_lock:
            if not owns_session():
                return {'error': '演出已結束。'}, 409
            if service.performance['mode'] not in ('mapping', 'hybrid'):
                return {'error': '手勢模式不需要校正。'}, 400
            if service.performance['players'] == 2:
                service.duet_mapping_router.recalibrate(side)
                service.refresh_duet_mapping_state()
            else:
                with service.core.pipeline_lock:
                    service.core.pipeline.reset_calibration()
                service.core.set_puppet_state(False, service.core.CONTROL_DEFAULTS)
                if service.performance['mode'] == 'hybrid':
                    service.reset_solo_hybrid()
                    service.publish_hybrid_state()
                else:
                    service.publish_gesture_state()
            service.performance['calibrationRevision'] += 1
            return {**snapshot(), 'status': 'calibrating', 'side': side}
