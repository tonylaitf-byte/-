'use strict';
const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.THEATRE_TEST_URL || 'http://127.0.0.1:5087';
const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
async function until(condition) {
  for (let i=0;i<200;i++) { if(condition()) return; await delay(50); }
  throw new Error('Timed out waiting for delayed response');
}
(async()=>{
  const browser = await chromium.launch({channel:'chrome',headless:true,
    args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
  const cases=[], errors=[];
  try {
    for(const mode of ['mapping','hybrid']) for(const players of [1,2]) {
      const context=await browser.newContext({permissions:['camera']});
      const page=await context.newPage();
      page.on('pageerror',e=>errors.push(e.message));
      // Exercise the actual page/Worker/HTTP code; observe messages at the Unity
      // boundary without paying for a new WebGL build or testing animation here.
      await context.route('**/theatre.loader.js',route=>route.fulfill({contentType:'application/javascript',body:`
        window.__received=[];
        window.createUnityInstance=async()=>({Quit:async()=>{},SendMessage:(_,method,data)=>{
          if(method==='Configure') {const s=JSON.parse(data); queueMicrotask(()=>window.onTheatreStatus({...s,ready:true}));}
          if(method==='ReceiveState') window.__received.push(JSON.parse(data));
        }});`}));
      let holdFrame=false,holdPoll=false,frameRelease=null,pollRelease=null,resetDone=false;
      const inject=result=>{
        if(!result.state) return result;
        const control={version:2,controlMode:mode,handDetected:true,calibrated:true,
          calibrationProgress:1,leftHandY:.987,gesture:0,inputEpoch:1,actionSequence:0};
        result.state=players===1?control:{controlMode:'duet_'+mode,
          left:{ownerHeld:true,handDetected:true,controls:{...control}},
          right:{ownerHeld:true,handDetected:true,controls:{...control}}};
        return result;
      };
      await context.route('**/frame',async route=>{
        const response=await route.fetch();
        const result=await response.json();
        if(!resetDone) inject(result);
        if(holdFrame) {holdFrame=false; await new Promise(resolve=>{frameRelease=resolve;});}
        await route.fulfill({response,json:result});
      });
      await context.route('**/performance/state',async route=>{
        const response=await route.fetch();
        const result=await response.json();
        if(holdPoll) {
          holdPoll=false; inject(result);
          await new Promise(resolve=>{pollRelease=resolve;});
        }
        await route.fulfill({response,json:result});
      });
      await page.goto(`${base}/control?players=${players}&mode=${mode}`);
      await page.waitForFunction(()=>document.getElementById('left-detail').textContent.includes('校正完成'));
      holdFrame=true; holdPoll=true;
      await until(()=>frameRelease && pollRelease);
      resetDone=true;
      const resetResponse=page.waitForResponse(r=>r.url().endsWith('/performance/calibrate') && r.status()===200);
      await page.locator('.calibrate').first().click();
      const reset=await (await resetResponse).json();
      assert.equal(reset.calibrationRevision,1);
      await page.waitForFunction(players=>{
        if(!window.__received.length) return false;
        const state=JSON.parse(window.__received.at(-1).state);
        return (players===1?state:state.left.controls).calibrated===false;
      },players);
      await page.evaluate(()=>{window.__received=[];});
      const nextFrame=page.waitForRequest(r=>r.url().endsWith('/frame') && r.headers()['x-calibration-revision']==='1');
      frameRelease();pollRelease();
      await nextFrame;
      await page.waitForFunction(()=>window.__received.length>=2);
      const received=await page.evaluate(()=>window.__received.map(x=>JSON.parse(x.state)));
      for(const state of received) {
        const left=players===1?state:state.left.controls;
        assert.notEqual(left.leftHandY,.987,'Stale pose reached Unity after calibration');
        assert.equal(left.calibrated,false,'Stale calibration reached Unity');
      }
      assert.ok(!(await page.locator('#left-detail').innerText()).includes('校正完成'));
      cases.push({players,mode,oldFrameDiscarded:true,oldPollDiscarded:true,newRevisionSent:true});
      await page.locator('#back').click();
      await context.unrouteAll({behavior:'wait'});
      await context.close();
    }
    assert.deepEqual(errors,[]);
    const report={passed:true,cases,errors,unityBoundary:'SendMessage capture stub; actual page and Worker, fake camera'};
    fs.writeFileSync(path.resolve(__dirname,'../reports/CalibrationBrowserValidation.json'),JSON.stringify(report,null,2));
    console.log(JSON.stringify(report,null,2));
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
