"""Independent calibrated puppet mapping over the established two-zone ownership."""
import copy
import threading
import time
from duet_tracking import DuetHandRouter
from puppet_controls import PuppetControlPipeline, CONTROL_DEFAULTS, make_puppet_payload, clamp


class DuetMappingRouter:
    def __init__(self, calibration_samples=45, lost_grace=1.0):
        self.samples=calibration_samples
        self.lock=threading.RLock()
        self.ownership=DuetHandRouter(lost_grace=lost_grace)
        self.reset()

    def reset(self):
        with self.lock:
            self.ownership.reset()
            self.pipelines={side:PuppetControlPipeline(calibration_samples=self.samples) for side in ('left','right')}
            self.owners={side:0 for side in ('left','right')}
            self.last={side:CONTROL_DEFAULTS.copy() for side in ('left','right')}
            self.sequence={side:0 for side in ('left','right')}
            self.last_time={side:None for side in ('left','right')}

    def recalibrate(self, side='both'):
        if side not in ('left','right','both'):raise ValueError('side must be left, right or both')
        with self.lock:
            for item in ('left','right') if side=='both' else (side,):
                self.pipelines[item].reset_calibration()
                self.last[item]=CONTROL_DEFAULTS.copy()

    def update(self, hands, timestamp=None):
        now=time.monotonic() if timestamp is None else timestamp
        with self.lock, self.ownership.lock:
            routed=self.ownership.update(hands, now)
            result={'version':1,'controlMode':'duet_mapping','deadZoneMin':.45,'deadZoneMax':.55}
            for side in ('left','right'):
                owner=routed[side]
                if owner['ownerId']!=self.owners[side]:
                    self.owners[side]=owner['ownerId']
                    self.pipelines[side]=PuppetControlPipeline(calibration_samples=self.samples)
                    self.last[side]=CONTROL_DEFAULTS.copy()
                pipe=self.pipelines[side]
                observation=self.ownership.visible.get(owner['ownerId'])
                if observation:
                    # Keep image coordinates isotropic for finger/palm geometry.
                    # Only horizontal stage travel is expanded to the half-camera range.
                    controls=pipe.update(observation['landmarks'],now)
                    controls['bodyX']=clamp(controls['bodyX']/.45,-1,1)
                    controls['moveX']=controls['bodyX']
                    self.last[side]=copy.copy(controls)
                elif owner['ownerHeld']:
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
                result[side]={'ownerHeld':owner['ownerHeld'],'ownerId':owner['ownerId'],
                    'handDetected':bool(observation),'handedness':owner['handedness'],
                    'palmX':owner['palmX'],'controls':payload}
            return result
