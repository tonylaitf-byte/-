const {chromium} = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const output = path.resolve(__dirname, '../reports');
const base = process.env.THEATRE_TEST_URL || 'http://127.0.0.1:5000';
fs.mkdirSync(output, {recursive:true});

(async () => {
  const browser = await chromium.launch({channel:'chrome', headless:true, args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
  const context = await browser.newContext({viewport:{width:1440,height:1000}, permissions:['camera']});
  await context.addInitScript(() => {
    let handler;
    Object.defineProperty(window, 'onTheatreStatus', {configurable:true,
      get() { return status => { window.__theatreStatus = status; if (handler) handler(status); }; },
      set(value) { handler = value; }
    });
    const getMedia = navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
    navigator.mediaDevices.getUserMedia = async options => {
      if (window.__denyCamera) throw Object.assign(new Error('denied'), {name:'NotAllowedError'});
      const stream = await getMedia(options);
      (window.__testStreams ||= []).push(stream);
      return stream;
    };
  });
  const page = await context.newPage();
  const errors = [], warnings = [], cases = [];
  page.on('pageerror', e => errors.push(e.message));
  page.on('console', msg => { if (msg.type() === 'error') { errors.push(msg.text()); console.log('BROWSER_ERROR',page.url(),msg.text()); } else if (msg.type() === 'warning') warnings.push(msg.text()); });
  try {
    await page.goto(base, {waitUntil:'domcontentloaded'});
    assert.equal(await page.locator('input[name=players]:checked').inputValue(), '1');
    assert.equal(await page.locator('input[name=mode]:checked').inputValue(), 'gesture');
    await page.screenshot({path:path.join(output,'setup-desktop.png'),fullPage:true});
    await page.setViewportSize({width:390,height:844});
    assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
    await page.screenshot({path:path.join(output,'setup-mobile.png'),fullPage:true});
    await page.setViewportSize({width:1440,height:1000});
    for (const players of [1,2]) for (const mode of ['gesture','mapping']) {
      let synthetic = null;
      const syntheticResponse = async route => {
        const response = await route.fetch();
        const result = await response.json();
        if (synthetic && result.state) result.state = synthetic;
        await route.fulfill({response, json:result});
      };
      await context.route('**/performance/state', syntheticResponse);
      await context.route('**/frame', syntheticResponse);
      await page.goto(base, {waitUntil:'domcontentloaded'});
      await page.locator(`input[name=players][value="${players}"]`).check({force:true});
      await page.locator(`input[name=mode][value="${mode}"]`).check({force:true});
      await page.getByRole('button',{name:'進入舞台'}).click();
      await page.waitForFunction(() => window.__theatreStatus?.ready, {timeout:120000});
      await page.waitForFunction(() => document.getElementById('camera-status').textContent === '鏡頭已開啟', {timeout:15000});
      await page.waitForFunction(() => document.getElementById('preview').videoWidth > 0, {timeout:15000});
      const initial = await page.evaluate(() => window.__theatreStatus);
      assert.equal(initial.actors, players); assert.equal(initial.mode,mode);
      assert.equal(initial.scene, (players === 1 ? 'Solo' : 'Duet') + (mode === 'gesture' ? 'Gesture' : 'Mapping'));
      assert.equal(initial.error,'');
      if (players === 2) { assert.ok(initial.leftX < 0); assert.ok(initial.rightX > 0); }
      await page.screenshot({path:path.join(output,`${players}-${mode}.png`),fullPage:true});
      await page.locator('#pause').click();
      await page.waitForFunction(() => window.__theatreStatus?.paused);
      await page.locator('#pause').click();
      await page.waitForFunction(() => !window.__theatreStatus?.paused);
      const valid = {version:2,controlMode:mode,handDetected:true,calibrated:true,calibrationProgress:1,sequenceId:1,sampleDt:.04,bodyX:.4,leftHandY:.5,headYaw:.3,gesture:1,gestureHold:1,action:'BOW',actionSequence:1};
      if (players === 1) synthetic = valid;
      else if (mode === 'gesture') synthetic = {version:1,controlMode:'duet',left:{ownerHeld:true,ownerId:1,handDetected:true,gesture:1,gestureHold:1,bodyX:.4},right:{ownerHeld:true,ownerId:2,handDetected:true,gesture:1,gestureHold:1,bodyX:-.4}};
      else synthetic = {version:1,controlMode:'duet_mapping',left:{ownerHeld:true,ownerId:1,handDetected:true,controls:valid},right:{ownerHeld:true,ownerId:2,handDetected:true,controls:{...valid,bodyX:-.4}}};
      await page.waitForFunction(() => window.__theatreStatus?.sequence > 3);
      if (mode === 'gesture') {
        await page.waitForFunction(() => window.__theatreStatus?.subtitle?.length > 0, {timeout:15000});
        const subtitle = await page.locator('#subtitle').innerText();
        assert.match(subtitle,/俠客/);
        await page.locator('#pause').click();
        await page.waitForFunction(() => window.__theatreStatus?.paused);
        await page.waitForTimeout(800);
        assert.equal(await page.locator('#subtitle').innerText(),subtitle,'Pausing must preserve the current line.');
        await page.locator('#pause').click();
        await page.screenshot({path:path.join(output,`${players}-${mode}-voice.png`),fullPage:true});
        if (players === 2) await page.waitForFunction(() => window.__theatreStatus?.subtitle?.includes('琴師'), {timeout:15000});
      } else {
        await page.waitForFunction(x => Math.abs(window.__theatreStatus.leftX - x) > .05, initial.leftX, {timeout:10000});
        assert.match(await page.locator('#left-detail').innerText(),/校正完成/);
        const responsePromise = page.waitForResponse(r => r.url().endsWith('/performance/calibrate'));
        await page.locator('.calibrate').first().click();
        assert.equal((await responsePromise).status(),200);
      }
      synthetic = null;
      // Repeat a complete reset ten times per combination, reusing the compiled runtime.
      let previous = initial.sessionId;
      for (let i=0;i<10;i++) {
        await page.locator('#reset').click();
        await page.waitForFunction(old => window.__theatreStatus?.ready && window.__theatreStatus.sessionId !== old, previous, {timeout:30000});
        const after = await page.evaluate(() => window.__theatreStatus);
        previous = after.sessionId;
        assert.equal(after.actors,players); assert.equal(after.subtitle,''); assert.equal(after.queued,0);
        await page.waitForFunction(() => document.getElementById('camera-status').textContent === '鏡頭已開啟', {timeout:10000});
      }
      await page.locator('#camera-button').click();
      await page.waitForFunction(() => (window.__testStreams || []).every(stream => stream.getTracks().every(track => track.readyState === 'ended')));
      await page.evaluate(() => window.__denyCamera = true);
      await page.locator('#camera-button').click();
      await page.waitForFunction(() => document.getElementById('camera-message').textContent.includes('請允許攝影機'));
      await page.evaluate(() => window.__denyCamera = false);
      await page.locator('#camera-button').click();
      await page.waitForFunction(() => document.getElementById('camera-status').textContent === '鏡頭已開啟');
      cases.push({players,mode,scene:initial.scene,actors:initial.actors,resets:10,input:true,pause:true,cameraRetry:true,mediaStopped:true});
      console.log(JSON.stringify(cases.at(-1)));
      await page.locator('#back-link').click();
      await page.waitForURL(base + '/');
      await context.unroute('**/performance/state');
      await context.unroute('**/frame');
    }
    const runtimeErrors = errors.filter(e => !e.includes('409 (CONFLICT)') && !e.includes('favicon.ico'));
    assert.deepEqual(runtimeErrors, []);
    fs.writeFileSync(path.join(output,'BrowserValidation.json'),JSON.stringify({passed:true,cases,errors,warnings},null,2));
  } catch(error) {
    await page.screenshot({path:path.join(output,'failure.png'),fullPage:true}).catch(()=>{});
    const status = await page.evaluate(() => window.__theatreStatus).catch(()=>null);
    fs.writeFileSync(path.join(output,'BrowserValidation.json'),JSON.stringify({passed:false,cases,error:String(error),status,errors,warnings},null,2));
    throw error;
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
