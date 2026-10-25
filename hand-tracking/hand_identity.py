"""Confirm anatomical hand labels over time instead of trusting one frame."""
import math


class StableHandedness:
    def __init__(self):
        self.label = ''
        self.revision = 0
        self.interrupt()

    def interrupt(self):
        self.candidate = ''
        self.count = 0
        self.since = None
        self.last = None

    def update(self, label, now, confidence=1.0):
        confidence = float(confidence)
        if label not in ('Left', 'Right') or not math.isfinite(confidence) or confidence < .8:
            self.interrupt()
            return False
        if not self.label:
            self.label = label
        if label == self.label:
            self.interrupt()
            return False
        if (label != self.candidate or self.last is None
                or not 0 <= now - self.last <= .35):
            self.candidate, self.count, self.since = label, 0, now
        self.count += 1
        self.last = now
        if self.count >= 5 and now - self.since >= .25:
            self.label = label
            self.revision += 1
            self.interrupt()
            return True
        return False
