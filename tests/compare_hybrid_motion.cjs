'use strict';
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const root = path.resolve(__dirname, '../reports');
const before = JSON.parse(fs.readFileSync(path.join(root, 'motion-before/MotionReview.json'), 'utf8').replace(/^\uFEFF/, ''));
const after = JSON.parse(fs.readFileSync(path.join(root, 'motion-after/MotionReview.json'), 'utf8').replace(/^\uFEFF/, ''));
assert.ok(before.completed && after.completed && after.timingChecks);
assert.equal(before.actions.length, 10);
assert.equal(after.actions.length, 10);
const drop = (old, value) => Math.round((1 - value / old) * 1000) / 10;
const actions = before.actions.map(old => {
  const value = after.actions.find(item => item.role === old.role && item.action === old.action);
  assert.ok(value, 'Missing actor/action result.');
  return {role: old.role, action: old.action,
    speedBefore: old.peakDegreesPerSecond, speedAfter: value.peakDegreesPerSecond,
    speedDropPercent: drop(old.peakDegreesPerSecond, value.peakDegreesPerSecond),
    accelerationBefore: old.peakAcceleration, accelerationAfter: value.peakAcceleration,
    accelerationDropPercent: drop(old.peakAcceleration, value.peakAcceleration),
    releaseDropPercent: drop(old.releasePeak, value.releasePeak),
    wristSpeedDropPercent: drop(old.peakWristSpeed, value.peakWristSpeed),
    durationDifference: value.seconds - old.seconds};
});
const passed = actions.every(item => item.speedDropPercent >= 1 && item.accelerationDropPercent >= 1 &&
  item.releaseDropPercent >= 1 && item.wristSpeedDropPercent >= 1 && Math.abs(item.durationDifference) < .35);
const report = {passed, scope:'Ten actions, fixed calibrated input, 60 Hz deterministic skeleton review; not a human naturalness score.',
  timingChecks: after.timingChecks, actions,
  engineDiagnosticsBefore: before.engineDiagnostics, engineDiagnosticsAfter: after.engineDiagnostics};
fs.writeFileSync(path.join(root, 'HybridMotionComparison.json'), JSON.stringify(report, null, 2));
console.table(actions.map(({role, action, speedDropPercent, accelerationDropPercent, releaseDropPercent}) =>
  ({role, action, speedDropPercent, accelerationDropPercent, releaseDropPercent})));
assert.ok(passed, 'Motion comparison has a regression; inspect HybridMotionComparison.json.');
