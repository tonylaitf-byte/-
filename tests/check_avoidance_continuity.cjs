'use strict';
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const read = folder => JSON.parse(fs.readFileSync(path.join(root, 'reports', folder, 'Audit.json'), 'utf8'));
const before = read('current-motion-audit');
const after = read(process.argv[2] || 'avoidance-continuity');
assert.equal(after.failure, '', 'Unity audit must finish successfully');
assert.equal(after.results.length, 22);
const key = r => `${r.role}/${r.phase}/${r.avoidance}`;
const baseline = new Map(before.results.map(r => [key(r), r]));
const close = (a, b, label) => assert.ok(Math.abs(a - b) < 0.0001, label);
for (const row of after.results) {
  const old = baseline.get(key(row));
  assert.ok(old, `Missing baseline: ${key(row)}`);
  assert.equal(row.frames, old.frames);
  assert.ok(row.intersectionFrames <= old.intersectionFrames, `New intersections: ${key(row)}`);
  for (const side of ['Left', 'Right']) {
    for (const axis of ['x', 'y', 'z'])
      close(row[`final${side}Local`][axis], old[`final${side}Local`][axis], `Endpoint: ${key(row)}/${side}/${axis}`);
  }
  if (!row.avoidance || row.role === '\u7434\u5e2b') {
    close(row.maxBoneStepDegrees, old.maxBoneStepDegrees, `Unchanged control: ${key(row)}`);
    close(row.maxWristStepInShoulderSpans, old.maxWristStepInShoulderSpans, `Unchanged wrist: ${key(row)}`);
  }
  if (row.phase === 'head-only') assert.equal(row.maxWristStepInShoulderSpans, 0);
  if (row.avoidance && ['forward', 'sweep', 'head-only'].includes(row.phase))
    assert.equal(row.intersectionFrames, 0, `Protected live pose: ${key(row)}`);
}
const sweep = after.results.find(r => r.phase === 'sweep' && r.avoidance && r.role === '\u4fe0\u5ba2');
const oldSweep = baseline.get(key(sweep));
assert.ok(sweep.maxBoneStepDegrees <= 3.05, '60 Hz protected sweep must respect 180 degrees/second');
assert.ok(sweep.maxBoneStepDegrees < oldSweep.maxBoneStepDegrees * 0.1);
assert.ok(sweep.maxWristStepInShoulderSpans < 0.05);
console.log(JSON.stringify({passed:true, rows:after.results.length,
  sweepBeforeDegrees:oldSweep.maxBoneStepDegrees, sweepAfterDegrees:sweep.maxBoneStepDegrees,
  engineDiagnostics:after.engineDiagnostics.length,
  note:'Checks saved runtime audit output; rerun LivePuppetMotionAudit after runtime changes.'}, null, 2));
