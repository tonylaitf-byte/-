"""Two spatial control zones with persistent hand ownership, independent of handedness."""
import itertools
import math
import threading
import time
from types import SimpleNamespace

from gesture_recognizer import GestureActionRecognizer, GestureMovementTracker
from hand_identity import StableHandedness


def palm_center(points):
    return tuple(sum(getattr(points[i], axis, 0.0) for i in (0, 5, 9, 13, 17)) / 5
                 for axis in ('x', 'y'))


class DuetHandRouter:
    def __init__(self, lost_grace=1.0, recognizer_factory=GestureActionRecognizer):
        self.lost_grace = lost_grace
        self.recognizer_factory = recognizer_factory
        self.lock = threading.RLock()
        self.reset()

    def reset(self):
        with self.lock:
            self.tracks = {}
            self.visible = {}
            self.next_id = 1
            self.recognizers = {side: self.recognizer_factory() for side in ('left', 'right')}
            self.movement = {side: GestureMovementTracker() for side in ('left', 'right')}

    def update(self, hands, timestamp=None, gesture_enabled=None):
        with self.lock:
            return self._update(hands, time.monotonic() if timestamp is None else timestamp, gesture_enabled)

    def _update(self, hands, now, gesture_enabled=None):
        for track_id, track in list(self.tracks.items()):
            if now - track['seen'] > self.lost_grace:
                if track['owner']:
                    self.recognizers[track['owner']].reset()
                del self.tracks[track_id]
        observations = []
        for hand in hands[:2]:
            points = hand['landmarks']
            if points is None or len(points) < 21:
                continue
            center = palm_center(points)
            if not all(math.isfinite(value) for value in center):
                continue
            observations.append({**hand, 'center': center})

        # Global matching avoids depending on MediaPipe's per-frame result ordering.
        ids = list(self.tracks)
        best, best_cost = (), float('inf')
        for assignment in itertools.product([None] + ids, repeat=len(observations)):
            used = [item for item in assignment if item is not None]
            if len(set(used)) != len(used):
                continue
            cost = 0.0
            for observation, track_id in zip(observations, assignment):
                if track_id is None:
                    cost += .65
                    continue
                track = self.tracks[track_id]
                elapsed = max(0, now - track['seen'])
                predicted = tuple(c + v * min(elapsed, .1) for c, v in
                                  zip(track['center'], track.get('velocity', (0, 0))))
                distance = math.dist(observation['center'], predicted)
                mismatch = bool(observation.get('handedness') and track['handedness'] and
                                observation['handedness'] != track['handedness'])
                # During continuous tracking position outweighs label flicker.
                # After a gap, an opposite hand cannot steal a reserved zone.
                penalty = (1.0 if elapsed > .35 else .08) if mismatch else 0
                cost += distance + penalty if distance < .65 else 10
            if cost < best_cost:
                best, best_cost = assignment, cost
        visible = {}
        for observation, track_id in zip(observations, best):
            if track_id is None:
                track_id = self.next_id
                self.next_id += 1
                self.tracks[track_id] = {'owner': None, 'handedness': '', 'identity': StableHandedness()}
            track = self.tracks[track_id]
            elapsed = now - track.get('seen', now)
            track['velocity'] = (tuple(max(-3, min(3, (c-p)/elapsed)) for c, p in
                                       zip(observation['center'], track['center']))
                                 if 0 < elapsed <= .35 else (0, 0))
            track.update(center=observation['center'], seen=now)
            track['identity'].update(observation.get('handedness', ''), now,
                                     observation.get('confidence', 1.0))
            track['handedness'] = track['identity'].label
            visible[track_id] = observation

        self.visible = visible
        for track_id, track in self.tracks.items():
            if track_id not in visible:
                track['identity'].interrupt()
        occupied = {track['owner'] for track in self.tracks.values() if track['owner']}
        for track_id, observation in visible.items():
            track = self.tracks[track_id]
            if track['owner']:
                continue
            x = observation['center'][0]
            side = 'left' if x < .45 else 'right' if x > .55 else None
            if side and side not in occupied:
                track['owner'] = side
                occupied.add(side)

        result = {'version': 1, 'controlMode': 'duet', 'deadZoneMin': .45, 'deadZoneMax': .55}
        for side in ('left', 'right'):
            owner = next(((key, track) for key, track in self.tracks.items() if track['owner'] == side), None)
            observation = visible.get(owner[0]) if owner else None
            points = observation['landmarks'] if observation else None
            if gesture_enabled is None:
                gesture = self.recognizers[side].update(points, now)
            else:
                gesture = self.recognizers[side].update(points, now, enabled=gesture_enabled[side])
            if points:
                lo, hi = (0, .45) if side == 'left' else (.55, 1)
                normalized = [SimpleNamespace(x=(p.x-lo)/(hi-lo), y=p.y, z=getattr(p, 'z', 0)) for p in points]
                movement = self.movement[side].update(normalized, now)
            elif owner:
                # Preserve movement and ownership through a short occlusion, but don't trigger stale poses.
                movement = self.movement[side].value
            else:
                movement = self.movement[side].update(None, now)
            result[side] = {
                'version': 2, 'controlMode': 'gesture', 'handDetected': bool(observation),
                'ownerHeld': bool(owner), 'ownerId': owner[0] if owner else 0,
                'ownerRevision': owner[1]['identity'].revision if owner else 0,
                'handedness': owner[1]['handedness'] if owner else '',
                'palmX': owner[1]['center'][0] if owner else .225 if side == 'left' else .775,
                'gesture': gesture['gesture'], 'gestureHold': gesture['gestureHold'],
                'action': gesture['triggeredAction'] or gesture['detectedAction'],
                'actionSequence': gesture['actionSequence'], 'bodyX': round(movement, 4),
                'moveX': round(movement, 4),
            }
            if gesture_enabled is not None:
                result[side].update({key: gesture[key] for key in
                                    ('gestureArmed', 'inputEpoch', 'action')})
        return result
