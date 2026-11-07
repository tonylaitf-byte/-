'use strict';
const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(__dirname, '..');
const base = 'http://127.0.0.1:5093';

(async () => {
  const browser = await chromium.launch({channel:'chrome', headless:true,
    args:['--use-fake-device-for-media-stream', '--use-fake-ui-for-media-stream']});
  const cases = [], errors = [];
  try {
    for (const players of [1, 2]) for (const mode of ['gesture', 'mapping', 'hybrid']) {
      const context = await browser.newContext({permissions:['camera']});
      const session = {sessionId:`overlay-${players}-${mode}`, players, mode, calibrationRevision:0};
      const control = {version:2, controlMode:mode, handDetected:true, calibrated:false,
        calibrationProgress:0, gesture:0, gestureHold:0, gestureArmed:false, inputEpoch:1, actionSequence:0};
      const state = players === 1 ? control : {controlMode:'duet_' + mode,
        left:{handDetected:true, ownerHeld:true, controls:control},
        right:{handDetected:true, ownerHeld:true, controls:control}};
      const envelope = {...session, state};
      let frameResponses = 0;
      await context.route(base + '/**', async route => {
        const url = new URL(route.request().url());
        if (url.pathname === '/control') return route.fulfill({contentType:'text/html',
          body:fs.readFileSync(path.join(root, 'hand-tracking/templates/control.html'), 'utf8')});
        if (url.pathname.startsWith('/static/theatre-ui/')) {
          const filename = path.basename(url.pathname);
          if (!['performance.js', 'camera-worker.js', 'theatre.css'].includes(filename)) return route.abort();
          return route.fulfill({contentType:filename.endsWith('.css') ? 'text/css' : 'application/javascript',
            body:fs.readFileSync(path.join(root, 'hand-tracking/static/theatre-ui', filename), 'utf8')});
        }
        if (url.pathname.endsWith('theatre.loader.js')) return route.fulfill({contentType:'application/javascript', body:`
          window.createUnityInstance=async()=>({Quit:async()=>{},SendMessage:(_,method,data)=>{
            if(method==='Configure') queueMicrotask(()=>window.onTheatreStatus({...JSON.parse(data),ready:true}));
          }});`});
        let payload;
        if (url.pathname === '/performance/start') payload = session;
        else if (url.pathname === '/performance/state') payload = envelope;
        else if (url.pathname === '/performance/input-stop' || url.pathname === '/performance/stop') payload = {status:'stopped'};
        else if (url.pathname === '/frame') {
          // Keep calibration false: drawing must not wait for neutral or arming.
          await new Promise(resolve => setTimeout(resolve, mode === 'hybrid' ? 500 : 40));
          frameResponses++;
          payload = {...envelope, landmarks:Array.from({length:players}, (_, hand) =>
            Array.from({length:21}, (_, i) => [.15 + hand*.5 + i*.004, .35 + i*.004]))};
        } else return route.fulfill({status:404, body:'Not found'});
        return route.fulfill({contentType:'application/json', body:JSON.stringify(payload)});
      });
      const page = await context.newPage();
      page.on('pageerror', error => errors.push({mode, players, error:error.message}));
      try {
        await page.goto(`${base}/control?players=${players}&mode=${mode}`);
        await page.waitForFunction(() => {
          const canvas = document.getElementById('hand-overlay');
          return canvas && canvas.getContext('2d').getImageData(0, 0, canvas.width, canvas.height)
            .data.some((value, i) => i % 4 === 3 && value);
        }, null, {timeout:15000});
        assert.ok(frameResponses > 0);
        assert.match(await page.locator('#left-detail').innerText(), mode === 'gesture' ? /等待穩定手勢/ : /校正 0%/);
        assert.equal(await page.locator('#hand-overlay').isVisible(), true);
        cases.push({players, mode, responseDelayMs:mode === 'hybrid' ? 500 : 40,
          visibleOverlayBeforeCalibration:true, frameResponses});
        await page.locator('#camera-button').click();
      } finally {
        await context.unrouteAll({behavior:'ignoreErrors'});
        await context.close();
      }
    }
    assert.deepEqual(errors, []);
    const report = {passed:true, cases, errors,
      scope:'Actual page/CSS/Worker with fake camera, mocked API landmarks and Unity loader. Not actual MediaPipe, camera or Unity performance validation.'};
    fs.writeFileSync(path.join(root, 'reports/InitialOverlayValidation.json'), JSON.stringify(report, null, 2) + '\n');
    console.log(JSON.stringify(report, null, 2));
  } finally { await browser.close(); }
})().catch(error => {console.error(error); process.exitCode = 1;});
