'use strict';
const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.THEATRE_TEST_URL || 'http://127.0.0.1:5000';
const output = path.resolve(__dirname, '../reports');
fs.mkdirSync(output, {recursive:true});

(async () => {
  const browser = await chromium.launch({channel:'chrome',headless:true,
    args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
  const context = await browser.newContext({viewport:{width:1440,height:1000},permissions:['camera']});
  await context.addInitScript(() => {
    let handler;
    Object.defineProperty(window,'onTheatreStatus',{configurable:true,
      get:() => status => {window.__theatreStatus=status; if(handler) handler(status);},
      set:value => {handler=value;}});
  });
  const page = await context.newPage();
  const errors=[], cases=[];
  page.on('pageerror',error=>errors.push(error.message));
  let injected=null;
  await context.route('**/frame', async route => {
    const response=await route.fetch();
    const result=await response.json();
    if(injected && result.state) result.state=JSON.parse(JSON.stringify(injected));
    await route.fulfill({response,json:result});
  });
  const control=(extra={})=>({version:2,controlMode:'hybrid',inputEpoch:10000,sequenceId:1,sampleDt:.04,
    handDetected:true,calibrated:true,calibrationProgress:1,bodyX:.3,headYaw:.2,leftHandY:.2,
    gesture:0,gestureHold:0,gestureArmed:true,action:'',actionSequence:0,...extra});
  const envelope=(players,left,right)=>players===1?left:{version:1,controlMode:'duet_hybrid',
    left:{ownerHeld:true,ownerId:11,handDetected:true,controls:left},
    right:{ownerHeld:true,ownerId:22,handDetected:true,controls:right||control({bodyX:-.3})}};
  const state=()=>page.evaluate(()=>window.__theatreStatus);
  try {
    for(const players of [1,2]) {
      injected=null;
      await page.goto(base,{waitUntil:'domcontentloaded'});
      await page.locator(`input[name=players][value="${players}"]`).check({force:true});
      await page.locator('input[name=mode][value="hybrid"]').check({force:true});
      await page.getByRole('button',{name:'進入舞台'}).click();
      await page.waitForFunction(()=>window.__theatreStatus?.ready,null,{timeout:120000});
      await page.waitForFunction(()=>document.getElementById('camera-status').textContent==='鏡頭已開啟');
      const initial=await state();
      assert.equal(initial.mode,'hybrid');
      assert.equal(initial.scene,players===1?'SoloHybrid':'DuetHybrid');
      assert.equal(initial.actors,players);
      assert.equal(initial.error,'');
      assert.equal(await page.locator('.calibrate').count(),players);
      injected=envelope(players,control(),control({bodyX:-.3}));
      await page.waitForFunction(()=>window.__theatreStatus?.leftHybrid?.calibrated && window.__theatreStatus.leftHybrid.inputEpoch===10000);
      await page.waitForFunction(x=>Math.abs(window.__theatreStatus.leftX-x)>.05,initial.leftX);

      // The same input stream carries a continuous pose and one discrete action.
      injected=envelope(players,control({action:'ATTACK',actionSequence:1,gesture:5,gestureHold:1,gestureArmed:false}));
      await page.waitForFunction(()=>window.__theatreStatus?.leftHybrid?.actionActive && window.__theatreStatus.leftHybrid.startedCount===1);
      const during=await state();
      const handBefore=during.leftHybrid.mappingPose.leftHand;
      injected=envelope(players,control({leftHandY:.9,action:'ATTACK',actionSequence:1,gesture:5,gestureHold:1,gestureArmed:false}));
      await page.waitForFunction(()=>window.__theatreStatus?.leftHybrid?.mappingPose?.inputLeftHandY>.85);
      await page.waitForFunction(before=>{
        const s=window.__theatreStatus?.leftHybrid, p=s?.mappingPose?.leftHand;
        return s?.actionActive && p && Math.hypot(p.x-before.x,p.y-before.y,p.z-before.z)>.02;
      },handBefore);
      assert.equal((await state()).leftHybrid.startedCount,1,'Repeated event sequence must not restart an action.');

      // Busy gestures are consumed once; they must never play later from a stale queue.
      injected=envelope(players,control({bodyX:-.3,leftHandY:.9,action:'BOW',actionSequence:2}));
      await page.waitForFunction(()=>window.__theatreStatus?.leftHybrid?.droppedCount===1);
      await page.waitForFunction(()=>window.__theatreStatus?.leftHybrid && !window.__theatreStatus.leftHybrid.actionActive,null,{timeout:45000});
      assert.equal((await state()).leftHybrid.startedCount,1);
      injected=envelope(players,control({bodyX:.45,leftHandY:-.3,action:'BOW',actionSequence:2}));
      await page.waitForFunction(()=>window.__theatreStatus.leftHybrid.mappingPose.inputLeftHandY<-.25);
      assert.equal((await state()).leftHybrid.startedCount,1);

      if(players===2) {
        injected=envelope(players,control({action:'BOW',actionSequence:3}),control({bodyX:-.3,action:'BOW',actionSequence:1}));
        await page.waitForFunction(()=>window.__theatreStatus?.leftHybrid?.startedCount===2 && window.__theatreStatus?.rightHybrid?.startedCount===1);
        assert.equal((await state()).leftHybrid.ownerId,11);
        assert.equal((await state()).rightHybrid.ownerId,22);
        assert.notEqual((await state()).leftHybrid.actionSequence,(await state()).rightHybrid.actionSequence);
      } else {
        injected=envelope(players,control({action:'BOW',actionSequence:3}));
        await page.waitForFunction(()=>window.__theatreStatus?.leftHybrid?.startedCount===2);
      }
      await page.waitForFunction(()=>window.__theatreStatus?.subtitle?.length>0,null,{timeout:15000});
      const subtitle=(await state()).subtitle;
      await page.locator('#pause').click();
      await page.waitForFunction(()=>window.__theatreStatus?.paused);
      await page.waitForTimeout(500);
      assert.equal((await state()).subtitle,subtitle);
      assert.equal(await page.locator('#subtitle').innerText(),subtitle);
      await page.locator('#pause').click();
      await page.waitForFunction(()=>!window.__theatreStatus?.paused);
      await page.screenshot({path:path.join(output,`${players}-hybrid.png`),fullPage:true});
      const canvas=page.locator('#unity-canvas');
      const desktopCanvas=await canvas.screenshot({path:path.join(output,`${players}-hybrid-canvas.png`)});
      assert.ok(desktopCanvas.length>10000,'Desktop stage must contain rendered content.');
      await page.setViewportSize({width:390,height:844});
      assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),'Mobile layout must not overflow.');
      await page.waitForTimeout(300);
      const mobileCanvas=await canvas.screenshot({path:path.join(output,`${players}-hybrid-mobile-canvas.png`)});
      assert.ok(mobileCanvas.length>10000,'Mobile stage must contain rendered content.');
      await page.screenshot({path:path.join(output,`${players}-hybrid-mobile.png`),fullPage:true});
      await page.setViewportSize({width:1440,height:1000});
      const beforeReset=(await state()).sessionId;
      injected=null;
      await page.locator('#reset').click();
      await page.waitForFunction(old=>window.__theatreStatus?.ready && window.__theatreStatus.sessionId!==old,beforeReset,{timeout:45000});
      const reset=await state();
      assert.equal(reset.leftHybrid.startedCount,0);
      assert.equal(reset.actionActive,false);
      assert.equal(reset.subtitle,'');
      cases.push({players,scene:initial.scene,mappingDuringAction:true,eventDedup:true,busyDrop:true,
        newestTargetAfterAction:true,ownerIsolation:true,pause:true,reset:true});
      await page.locator('#back-link').click();
      await page.waitForURL(base+'/');
    }
    assert.deepEqual(errors,[]);
    fs.writeFileSync(path.join(output,'HybridBrowserValidation.json'),JSON.stringify({passed:true,cases,errors},null,2));
    console.log(JSON.stringify({passed:true,cases}));
  } catch(error) {
    await page.screenshot({path:path.join(output,'hybrid-failure.png'),fullPage:true}).catch(()=>{});
    fs.writeFileSync(path.join(output,'HybridBrowserValidation.json'),JSON.stringify({passed:false,cases,
      error:String(error),status:await state().catch(()=>null),errors},null,2));
    throw error;
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
