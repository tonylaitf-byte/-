"""Hybrid-specific pose remapping and measured stable-start latency."""
import json
import unittest
from pathlib import Path
from types import SimpleNamespace

import docker_app as service
from duet_mapping_tracking import DuetMappingRouter
from duet_tracking import palm_center
from gesture_recognizer import (HybridGestureActionRecognizer,
                                classify_counting_gesture, classify_hybrid_gesture)
from puppet_controls import (CONTROL_DEFAULTS, NeutralPoseCalibrator,
                             FAST_CALIBRATION_SAMPLES, FAST_CALIBRATION_SECONDS)
from test_gesture_recognizer import hand

OPEN = {'thumb', 'index', 'middle', 'ring', 'pinky'}
SWORD = {'thumb', 'index'}


class HybridGestureTests(unittest.TestCase):
    def test_new_sword_pose_is_hybrid_only_for_both_hands(self):
        for mirror in (False, True):
            points = hand(SWORD)
            if mirror:
                points = [SimpleNamespace(x=1-p.x, y=p.y, z=p.z) for p in points]
            self.assertEqual(classify_hybrid_gesture(points), 5)
            self.assertEqual(classify_counting_gesture(points), 0)
        self.assertEqual(classify_counting_gesture(hand(OPEN)), 5)
        self.assertEqual(classify_hybrid_gesture(hand(OPEN)), 0)

    def test_first_four_commands_keep_their_existing_meaning(self):
        for fingers, expected in (({'index'},1), ({'index','middle'},2),
                ({'thumb','index','middle'},3), ({'index','middle','ring','pinky'},4)):
            self.assertEqual(classify_hybrid_gesture(hand(fingers)), expected)

    def test_sword_still_requires_neutral_hold_and_rearm(self):
        recognizer = HybridGestureActionRecognizer()
        for now in (0, .5, 2):
            self.assertEqual(recognizer.update(hand(SWORD), now)['actionSequence'], 0)
        recognizer.update(hand(OPEN), 3)
        self.assertTrue(recognizer.update(hand(OPEN), 3.2)['gestureArmed'])
        recognizer.update(hand(SWORD), 3.3)
        self.assertEqual(recognizer.update(hand(SWORD), 3.79)['actionSequence'], 0)
        state = recognizer.update(hand(SWORD), 3.8)
        self.assertEqual((state['action'],state['actionSequence']), ('ATTACK',1))
        self.assertEqual(recognizer.update(hand(SWORD), 8)['actionSequence'], 1)
        recognizer.update(hand(OPEN), 9)
        self.assertTrue(recognizer.update(hand(OPEN), 9.2)['gestureArmed'])
        self.assertEqual(recognizer.update(hand(OPEN), 11)['actionSequence'], 1)


class HybridCalibrationTests(unittest.TestCase):
    def test_stable_window_is_shorter_at_normal_and_high_frame_rates(self):
        rows=[]
        for fps in (15,16,20,30,60,120,240):
            durations=[]
            for calibrator in (NeutralPoseCalibrator(),
                    NeutralPoseCalibrator(FAST_CALIBRATION_SAMPLES, FAST_CALIBRATION_SECONDS)):
                for frame in range(fps*4):
                    now=frame/fps
                    calibrator.observe(CONTROL_DEFAULTS, now)
                    if calibrator.calibrated:
                        durations.append(now)
                        break
                else: self.fail('Stable calibration did not finish')
            self.assertGreaterEqual(durations[1]+1e-9, .6)
            self.assertLessEqual(durations[1], .8)
            self.assertLess(durations[1],durations[0])
            rows.append({'fps':fps,'previousSeconds':durations[0],'hybridSeconds':durations[1]})
        output=Path(__file__).resolve().parents[1]/'reports/HybridQuickStartTiming.json'
        output.write_text(json.dumps({'passed':True,'cases':rows},indent=2),encoding='utf-8')

    def test_quick_calibration_still_rejects_motion_and_loss(self):
        calibrator=NeutralPoseCalibrator(12,.6)
        for frame in range(100):
            calibrator.observe({**CONTROL_DEFAULTS,'leftHandY': .8 if frame%2 else -.8},frame/20)
            self.assertFalse(calibrator.calibrated)
        for frame in range(6):calibrator.observe(CONTROL_DEFAULTS,5+frame/20)
        calibrator.interrupt()
        self.assertEqual(calibrator.progress,0)
        for frame in range(12):calibrator.observe(CONTROL_DEFAULTS,6+frame/20)
        self.assertFalse(calibrator.calibrated)
        calibrator.observe(CONTROL_DEFAULTS,6.6)
        self.assertTrue(calibrator.calibrated)

    def test_solo_and_duet_mapping_and_hybrid_profiles_survive_resets(self):
        client=service.app.test_client()
        try:
            for mode in ('mapping','hybrid'):
                for players in (1,2):
                    session=client.post('/performance/start',json={'players':players,'mode':mode}).get_json()
                    headers={'X-Performance-Session':session['sessionId']}
                    def pipelines():
                        return [service.core.pipeline] if players==1 else list(service.duet_mapping_router.pipelines.values())
                    for operation in ('start','calibrate','camera'):
                        if operation=='calibrate':client.post('/performance/calibrate',headers=headers,json={})
                        if operation=='camera':client.post('/performance/input-stop',headers=headers,json={})
                        for pipe in pipelines():
                            self.assertEqual(pipe.calibrator.samples_required,12)
                            self.assertAlmostEqual(pipe.calibrator.minimum_seconds,.6)
            session=client.post('/performance/start',json={'players':1,'mode':'hybrid'}).get_json()
            client.post('/performance/stop',json={'sessionId':session['sessionId']})
            client.post('/control_mode',json={'mode':'mapping'})
            self.assertEqual(service.core.pipeline.calibrator.samples_required,12)
        finally:
            if service.performance:client.post('/performance/stop',json={'sessionId':service.performance['sessionId']})

    def test_both_real_duet_pipelines_can_start_sword_in_first_second_and_half(self):
        router=DuetMappingRouter(hybrid=True)
        def observation(x,fingers,label):
            points=hand(fingers);offset=x-palm_center(points)[0]
            return {'landmarks':[SimpleNamespace(x=p.x+offset,y=p.y,z=p.z) for p in points], 'handedness':label}
        for frame in range(31):
            fingers=OPEN if frame<20 else SWORD
            state=router.update([observation(.2,fingers,'Left'),observation(.8,fingers,'Right')],frame/20,frame_size=(640,480))
            if frame==16:
                self.assertTrue(state['left']['controls']['calibrated'])
                self.assertTrue(state['right']['controls']['calibrated'])
            if frame<30:
                self.assertEqual(state['left']['controls']['actionSequence'],0)
        for side in ('left','right'):
            self.assertEqual((state[side]['controls']['action'],state[side]['controls']['actionSequence']),('ATTACK',1))

    def test_mapping_owners_use_fast_window_after_initial_assignment(self):
        from test_duet_mapping import observation
        router = DuetMappingRouter()
        for frame in range(17):
            state = router.update([observation(.2), observation(.8)], frame / 20)
        for side in ('left', 'right'):
            self.assertTrue(state[side]['controls']['calibrated'])
            self.assertEqual(router.pipelines[side].calibrator.samples_required, 12)
        router.reset()
        self.assertTrue(all(p.calibrator.minimum_seconds == .6 for p in router.pipelines.values()))


if __name__=='__main__':unittest.main()
