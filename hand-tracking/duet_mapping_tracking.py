"""Independent calibrated puppet mapping over the established two-zone ownership."""
import copy
import threading
import time
from duet_tracking import DuetHandRouter
from gesture_recognizer import GestureActionRecognizer, HybridGestureActionRecognizer
from puppet_controls import PuppetControlPipeline, CONTROL_DEFAULTS, make_puppet_payload, clamp


class DuetMappingRouter:
    def __init__(self, calibration_samples=45, lost_grace=1.0, hybrid=False):
        self.samples=calibration_samples
        self.lock=threading.RLock()
        self.hybrid=bool(hybrid)
        self.ownership=DuetHandRouter(lost_grace=lost_grace,
            recognizer_factory=HybridGestureActionRecognizer if self.hybrid else GestureActionRecognizer)
        self.reset()

    def set_hybrid(self, enabled):
        with self.lock:
            if self.hybrid != bool(enabled):
                self.hybrid=bool(enabled)
                self.ownership.recognizer_factory=(HybridGestureActionRecognizer if self.hybrid
                                                   else GestureActionRecognizer)
                self.reset()

    def reset(self):
        with self.lock:
            self.ownership.reset()
            self.pipelines={side:PuppetControlPipeline(calibration_samples=self.samples) for side in ('left','right')}
            self.owners={side:0 for side in ('left','right')}
            self.owner_revisions={side:0 for side in ('left','right')}
            self.last={side:CONTROL_DEFAULTS.copy() for side in ('left','right')}
            self.sequence={side:0 for side in ('left','right')}
            self.last_time={side:None for side in ('left','right')}
            self.state=None

    def snapshot(self):
        with self.lock:
            if self.state is None:
                return self.update([])
            return copy.deepcopy(self.state)

    @staticmethod
    def _event_fields(event):
        return {key:event[key] for key in ('gesture','gestureHold','gestureArmed',
                                           'action','actionSequence','inputEpoch')}

    def suspend(self):
        with self.lock, self.ownership.lock:
            if not self.hybrid:
                return
            for side in ('left','right'):
                recognizer=self.ownership.recognizers[side]
                recognizer.suspend()
                if self.state:
                    self.state[side]['controls'].update(self._event_fields(recognizer.snapshot()))

    def recalibrate(self, side='both'):
        if side not in ('left','right','both'):raise ValueError('side must be left, right or both')
        with self.lock:
            for item in ('left','right') if side=='both' else (side,):
                self.pipelines[item].reset_calibration()
                self.last[item]=CONTROL_DEFAULTS.copy()
                if self.hybrid:
                    recognizer=self.ownership.recognizers[item]
                    recognizer.reset()
                if self.state:
                    controls=make_puppet_payload(False,CONTROL_DEFAULTS)
                    controls['controlMode']='mapping'
                    if self.hybrid:
                        controls.update(controlMode='hybrid',**self._event_fields(recognizer.snapshot()))
                    self.state[item]['handDetected']=False
                    self.state[item]['controls']=controls

    def update(self, hands, timestamp=None, frame_size=None):
        now=time.monotonic() if timestamp is None else timestamp
        with self.lock, self.ownership.lock:
            for side in ('left','right'):
                if self.pipelines[side].set_frame_size(frame_size):
                    self.last[side]=CONTROL_DEFAULTS.copy()
                    if self.hybrid:
                        self.ownership.recognizers[side].reset()
            enabled={side:self.pipelines[side].calibrated for side in ('left','right')} if self.hybrid else None
            routed=self.ownership.update(hands, now, gesture_enabled=enabled)
            result={'version':1,'controlMode':'duet_hybrid' if self.hybrid else 'duet_mapping',
                    'deadZoneMin':.45,'deadZoneMax':.55}
            for side in ('left','right'):
                owner=routed[side]
                if (owner['ownerId']!=self.owners[side]
                        or owner['ownerRevision']!=self.owner_revisions[side]):
                    self.owners[side]=owner['ownerId']
                    self.owner_revisions[side]=owner['ownerRevision']
                    self.pipelines[side]=PuppetControlPipeline(calibration_samples=self.samples)
                    self.last[side]=CONTROL_DEFAULTS.copy()
                    if self.hybrid:
                        recognizer=self.ownership.recognizers[side]
                        recognizer.reset()
                        owner.update(self._event_fields(recognizer.snapshot()))
                pipe=self.pipelines[side]
                observation=self.ownership.visible.get(owner['ownerId'])
                if observation:
                    # Geometry uses the full detector frame, not a stretched half-frame.
                    # Only horizontal stage travel expands to the half-camera range.
                    controls=pipe.update(observation['landmarks'],now,frame_size=frame_size)
                    controls['bodyX']=clamp(controls['bodyX']/.45,-1,1)
                    controls['moveX']=controls['bodyX']
                    self.last[side]=copy.copy(controls)
                elif owner['ownerHeld']:
                    pipe.calibrator.interrupt()
                    controls=self.last[side].copy()
                else:
                    controls=pipe.no_hand(now)
                    self.last[side]=copy.copy(controls)
                self.sequence[side]+=1
                previous=self.last_time[side]
                payload=make_puppet_payload(bool(observation),controls,pipe.calibrated,
                    pipe.calibration_progress,self.sequence[side],now-previous if previous is not None else 1/30,
                    pipe.gesture_velocities if observation else {})
                self.last_time[side]=now
                payload['controlMode']='mapping'
                if self.hybrid:
                    payload['controlMode']='hybrid'
                    payload.update(self._event_fields(owner))
                result[side]={'ownerHeld':owner['ownerHeld'],'ownerId':owner['ownerId'],
                    'handDetected':bool(observation),'handedness':owner['handedness'],
                    'palmX':owner['palmX'],'controls':payload}
            self.state=copy.deepcopy(result)
            return result
