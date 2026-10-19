"""Two spatial control zones with persistent hand ownership, independent of handedness."""
import itertools
import math
import threading
import time
from types import SimpleNamespace

from gesture_recognizer import GestureActionRecognizer, GestureMovementTracker


def palm_center(points):
    return tuple(sum(getattr(points[i], axis, 0.0) for i in (0, 5, 9, 13, 17)) / 5
                 for axis in ('x', 'y'))


class DuetHandRouter:
    def __init__(self, lost_grace=1.0):
        self.lost_grace = lost_grace
        self.lock = threading.RLock()
        self.reset()

    def reset(self):
        with self.lock:
            self.tracks = {}
            self.visible = {}
            self.next_id = 1
            self.recognizers = {side: GestureActionRecognizer() for side in ('left', 'right')}
            self.movement = {side: GestureMovementTracker() for side in ('left', 'right')}

    def update(self, hands, timestamp=None):
        with self.lock:
            return self._update(hands, time.monotonic() if timestamp is None else timestamp)

    def _update(self, hands, now):
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
                distance = math.dist(observation['center'], track['center'])
                mismatch = bool(observation.get('handedness') and track['handedness'] and
                                observation['handedness'] != track['handedness'])
                cost += distance + (1.0 if mismatch else 0) if distance < .65 else 10
            if cost < best_cost:
                best, best_cost = assignment, cost
        visible = {}
        for observation, track_id in zip(observations, best):
            if track_id is None:
                track_id = self.next_id
                self.next_id += 1
                self.tracks[track_id] = {'owner': None, 'handedness': observation.get('handedness', '')}
            track = self.tracks[track_id]
            track.update(center=observation['center'], seen=now)
            if observation.get('handedness'):
                track['handedness'] = observation['handedness']
            visible[track_id] = observation

        self.visible = visible
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
            gesture = self.recognizers[side].update(points, now)
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
                'handedness': owner[1]['handedness'] if owner else '',
                'palmX': owner[1]['center'][0] if owner else .225 if side == 'left' else .775,
                'gesture': gesture['gesture'], 'gestureHold': gesture['gestureHold'],
                'action': gesture['triggeredAction'] or gesture['detectedAction'],
                'actionSequence': gesture['actionSequence'], 'bodyX': round(movement, 4),
                'moveX': round(movement, 4),
            }
        return result
