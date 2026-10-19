const {chromium} = require('playwright');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.THEATRE_TEST_URL || 'http://127.0.0.1:5000';
(async () => {
  const browser = await chromium.launch({channel:'chrome',headless:true,args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
  const context = await browser.newContext({viewport:{width:1440,height:1000},permissions:['camera']});
  let active=0, maxActive=0, replies=0, feeds=0;
  const errors=[];
  await context.route('**/frame', async route => {
    active++; maxActive=Math.max(maxActive,active);
    try {
      const response=await route.fetch();
      const result=await response.json();
      // Model a slow recognizer/connection while retaining the real backend state.
      await new Promise(resolve=>setTimeout(resolve,350));
      result.landmarks=[Array.from({length:21},(_,i)=>[.2+i*.02,.4])];
      await route.fulfill({response,json:result}); replies++;
    } catch(error) {
      // Stopping the camera intentionally cancels its worker's pending request.
      if (!/closed|canceled|cancelled|disposed|aborted|Invalid InterceptionId/i.test(String(error))) errors.push(String(error));
    } finally {active--;}
  });
  const page=await context.newPage();
  page.on('pageerror',error=>errors.push(String(error)));
  page.on('request',request=>{if(new URL(request.url()).pathname==='/video_feed') feeds++;});
  try {
    await page.goto(`${base}/control?players=1&mode=gesture`,{waitUntil:'domcontentloaded'});
    await page.waitForFunction(()=>document.getElementById('camera-status').textContent==='鏡頭已開啟',null,{timeout:120000});
    const countBefore=replies;
    const stats=await page.evaluate(async()=>{
      const video=document.getElementById('preview'); let frames=0, running=true;
      const count=()=>{if(running){frames++;video.requestVideoFrameCallback(count);}};
      video.requestVideoFrameCallback(count);
      const start=performance.now(); await new Promise(resolve=>setTimeout(resolve,8000)); running=false;
      return {seconds:(performance.now()-start)/1000,frames,mirrored:getComputedStyle(video).transform};
    });
    const previewFps=stats.frames/stats.seconds;
    const inferenceFps=(replies-countBefore)/stats.seconds;
    assert.ok(previewFps>12,`Slow inference must not stall preview: ${previewFps}`);
    assert.ok(inferenceFps>0 && inferenceFps<4);
    assert.ok(previewFps>inferenceFps*3);
    assert.equal(maxActive,1,'Only one frame may be in flight; never queue old camera frames.');
    assert.equal(feeds,0,'The integrated preview must not download MJPEG.');
    assert.match(stats.mirrored,/-1/);
    await page.waitForFunction(()=>{
      const canvas=document.getElementById('hand-overlay');
      return canvas.getContext('2d').getImageData(Math.round(canvas.width*.2),Math.round(canvas.height*.4),1,1).data[3]>0;
    });
    await page.screenshot({path:path.resolve(__dirname,'../reports/camera-slow-inference.png'),fullPage:true});
    // Stop while a response is delayed. It must never redraw stale hands.
    const deadline=Date.now()+10000;
    while(!active && Date.now()<deadline) await new Promise(resolve=>setTimeout(resolve,20));
    assert.ok(active,'A delayed frame must be in flight before stopping the camera.');
    await page.locator('#camera-button').click();
    await page.waitForFunction(()=>document.getElementById('camera-status').textContent==='鏡頭未開啟');
    await page.waitForTimeout(700);
    assert.ok(await page.evaluate(()=>{
      const canvas=document.getElementById('hand-overlay');
      return document.getElementById('preview').srcObject===null && !canvas.getContext('2d').getImageData(0,0,canvas.width,canvas.height).data.some((n,i)=>i%4===3 && n);
    }));
    await page.locator('#back').click();
    assert.deepEqual(errors,[]);
    const result={passed:true,addedResponseDelayMs:350,previewFps,inferenceFps,maxFramesInFlight:maxActive,mjpegRequests:feeds,overlay:true,staleFramesAfterStop:false};
    fs.writeFileSync(path.resolve(__dirname,'../reports/CameraLatencyValidation.json'),JSON.stringify(result,null,2));
    console.log(JSON.stringify(result,null,2));
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
