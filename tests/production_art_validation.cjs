const {chromium} = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const assert = require('node:assert/strict');
const base = process.env.THEATRE_TEST_URL || 'http://127.0.0.1:5089';
const out = path.resolve(__dirname, '../reports/production-art');
fs.mkdirSync(out, {recursive:true});
(async()=>{
 const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
 const cases=[],errors=[];
 try {
  for(const players of [1,2]) for(const mode of ['gesture','mapping','hybrid']) {
   const context=await browser.newContext({viewport:{width:1600,height:1100},permissions:['camera']});
   await context.addInitScript(()=>{
    let handler;
    Object.defineProperty(window,'onTheatreStatus',{get:()=>status=>{window.__status=status;if(handler)handler(status);},set:v=>handler=v});
   });
   const page=await context.newPage();
   let inject=false, sample=0;
   await context.route('**/frame',async route=>{
    const response=await route.fetch();const result=await response.json();
    if(inject && mode!=='gesture') {
     const control={version:2,controlMode:mode,handDetected:true,calibrated:true,calibrationProgress:1,
      sequenceId:++sample,sampleDt:.04,bodyX:.35,leftHandY:.4,headYaw:.2,inputEpoch:99000,actionSequence:0};
     result.state=players===1?control:{version:1,controlMode:'duet_'+mode,
      left:{ownerHeld:true,ownerId:1,handDetected:true,controls:control},
      right:{ownerHeld:true,ownerId:2,handDetected:true,controls:{...control,bodyX:-.35}}};
    }
    await route.fulfill({response,json:result});
   });
   page.on('pageerror',e=>errors.push(e.message));
   try {
    await page.goto(`${base}/control?players=${players}&mode=${mode}`,{waitUntil:'domcontentloaded'});
    await page.waitForFunction(()=>window.__status?.ready,null,{timeout:120000});
    await page.waitForTimeout(1500);
    const initial=await page.evaluate(()=>window.__status);
    assert.equal(initial.actors,players);assert.equal(initial.mode,mode);assert.equal(initial.error,'');
    const file=`${players}-${mode}.png`;
    const png=await page.locator('#unity-canvas').screenshot({path:path.join(out,file)});
    assert.ok(png.length>20000,'Rendered scene must not be blank.');
    await page.locator('#pause').click();
    await page.waitForFunction(()=>window.__status?.paused);
    await page.locator('#pause').click();
    await page.waitForFunction(()=>!window.__status?.paused);
    await page.setViewportSize({width:390,height:844});
    await page.waitForTimeout(500);
    assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));
    await page.locator('#unity-canvas').screenshot({path:path.join(out,`${players}-${mode}-mobile.png`)});
    if(mode!=='gesture') {
     inject=true;
     await page.waitForFunction(x=>Math.abs(window.__status.leftX-x)>.03,initial.leftX,{timeout:15000});
    }
    cases.push({players,mode,scene:initial.scene,ready:true,pauseResume:true,desktop:file,mobile:true,
     syntheticMovement:mode!=='gesture'});
    await page.locator('#back').click();
   } finally {await context.close();}
  }
  assert.deepEqual(errors,[]);
  fs.writeFileSync(path.join(out,'Browser.json'),JSON.stringify({passed:true,cases,errors,scope:'Actual WebGL and local Flask, fake camera. No real hand or animation naturalness acceptance.'},null,2));
  console.log(JSON.stringify({passed:true,cases}));
 } finally {await browser.close();}
})().catch(e=>{console.error(e);process.exitCode=1;});
