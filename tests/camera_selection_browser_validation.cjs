const {chromium}=require('playwright');
const fs=require('node:fs'),path=require('node:path'),assert=require('node:assert/strict');
const base=process.env.THEATRE_TEST_URL||'http://127.0.0.1:5000',output=path.resolve(__dirname,'../reports');
(async()=>{
  const browser=await chromium.launch({channel:'chrome',headless:true,args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream','--autoplay-policy=no-user-gesture-required','--enable-unsafe-swiftshader']});
  const context=await browser.newContext({viewport:{width:1440,height:1000},permissions:['camera']});
  await context.addInitScript(()=>{
    let handler;Object.defineProperty(window,'onTheatreStatus',{configurable:true,get(){return status=>{window.__theatreStatus=status;if(handler)handler(status);};},set(value){handler=value;}});
    window.__devices=[{deviceId:'laptop',label:'Integrated Camera'},{deviceId:'droidcam',label:'DroidCam Video'},{deviceId:'camo',label:'Camo Camera'}];
    navigator.mediaDevices.enumerateDevices=async()=>window.__devices.map(device=>({...device,kind:'videoinput',groupId:device.deviceId}));
    const native=navigator.mediaDevices.getUserMedia.bind(navigator.mediaDevices);
    navigator.mediaDevices.getUserMedia=async options=>{
      const wanted=typeof options.video==='object'?options.video.deviceId?.exact:'';
      const device=window.__devices.find(device=>device.deviceId===wanted)||(!wanted?window.__devices[0]:null);
      if(!device)throw Object.assign(new Error('missing test camera'),{name:'OverconstrainedError'});
      if(window.__denyCamera)throw Object.assign(new Error('denied'),{name:'NotAllowedError'});
      const constraints=structuredClone(options);if(typeof constraints.video==='object')delete constraints.video.deviceId;
      const stream=await native(constraints);window.__lastCamera=device.deviceId;(window.__streams||=[]).push(stream);
      for(const track of stream.getVideoTracks()){const settings=track.getSettings.bind(track);track.getSettings=()=>({...settings(),deviceId:device.deviceId});Object.defineProperty(track,'label',{get:()=>device.label});}
      return stream;
    };
  });
  const page=await context.newPage(),errors=[],cases=[];
  page.on('pageerror',error=>errors.push(error.message));
  const waitCamera=id=>page.waitForFunction(id=>window.__lastCamera===id&&document.getElementById('camera-status').textContent==='鏡頭已開啟',id,{timeout:15000});
  try{
    await page.goto(base);await page.evaluate(()=>localStorage.clear());
    for(const players of [1,2])for(const mode of ['gesture','mapping','hybrid']){
      await page.goto(`${base}/control?players=${players}&mode=${mode}`);await page.waitForFunction(()=>window.__theatreStatus?.ready,null,{timeout:120000});await waitCamera('laptop');
      const session=(await page.evaluate(()=>window.__theatreStatus)).sessionId;
      assert.equal(await page.locator('#camera-source').inputValue(),'device');
      if(players===1&&mode==='gesture')await page.screenshot({path:path.join(output,'device-camera.png'),fullPage:true});
      const expectedPhone=await page.evaluate(()=>localStorage.getItem('puppet-camera-phone')||'droidcam');
      await page.locator('#camera-source').selectOption('phone');await waitCamera(expectedPhone);
      await page.locator('#camera-device').selectOption('droidcam');await waitCamera('droidcam');
      assert.ok(await page.locator('#phone-camera-help').isVisible());assert.match(await page.locator('#camera-device-name').textContent(),/DroidCam/);
      assert.equal((await page.evaluate(()=>window.__theatreStatus)).sessionId,session);
      await page.waitForFunction(()=>(window.__streams||[]).slice(0,-1).every(stream=>stream.getTracks().every(track=>track.readyState==='ended')));
      await page.locator('#camera-device').selectOption('camo');await waitCamera('camo');assert.match(await page.locator('#camera-device-name').textContent(),/Camo/);
      if(players===1&&mode==='gesture')await page.screenshot({path:path.join(output,'phone-webcam-selected.png'),fullPage:true});
      await page.locator('#camera-source').selectOption('device');await waitCamera('laptop');assert.equal((await page.evaluate(()=>window.__theatreStatus)).sessionId,session);
      cases.push({players,mode,localCamera:true,droidCamSelected:true,camoSelected:true,oldStreamsReleased:true,samePerformance:true});console.log('PASS',players,mode);
      await page.locator('#back-link').click();await page.waitForURL(base+'/');
    }
    await page.goto(`${base}/control?players=1&mode=gesture`);await waitCamera('laptop');
    await page.evaluate(()=>{window.__devices=[{deviceId:'laptop',label:'Integrated Camera'}];localStorage.removeItem('puppet-camera-phone');});
    await page.locator('#camera-source').selectOption('phone');await page.waitForFunction(()=>document.getElementById('camera-message').textContent.includes('尚未偵測到手機鏡頭'));
    assert.ok(await page.evaluate(()=>(window.__streams||[]).every(stream=>stream.getTracks().every(track=>track.readyState==='ended'))),'Missing phone camera silently used laptop camera');
    await page.evaluate(()=>{window.__devices.push({deviceId:'other-phone',label:'USB Video Bridge'});navigator.mediaDevices.dispatchEvent(new Event('devicechange'));});
    await page.locator('#camera-device option[value="other-phone"]').waitFor({state:'attached'});await page.locator('#camera-device').selectOption('other-phone');await waitCamera('other-phone');
    await page.locator('#camera-button').click();await page.waitForFunction(()=>(window.__streams||[]).every(stream=>stream.getTracks().every(track=>track.readyState==='ended')));
    await page.evaluate(()=>{window.__devices=[{deviceId:'laptop',label:'Integrated Camera'}];navigator.mediaDevices.dispatchEvent(new Event('devicechange'));});
    await page.locator('#refresh-cameras').click();await page.waitForFunction(()=>document.getElementById('camera-message').textContent.includes('尚未偵測到手機鏡頭'));
    await page.evaluate(()=>{window.__devices.push({deviceId:'droidcam',label:'DroidCam Video'});navigator.mediaDevices.dispatchEvent(new Event('devicechange'));});await waitCamera('droidcam');
    await page.locator('#camera-source').selectOption('device');await waitCamera('laptop');await page.locator('#camera-button').click();await page.evaluate(()=>window.__denyCamera=true);
    await page.locator('#camera-button').click();await page.waitForFunction(()=>document.getElementById('camera-message').textContent.includes('請允許攝影機'));
    await page.evaluate(()=>window.__denyCamera=false);await page.locator('#camera-button').click();await waitCamera('laptop');
    await page.setViewportSize({width:390,height:844});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await page.screenshot({path:path.join(output,'camera-selection-mobile.png'),fullPage:true});
    assert.deepEqual(errors,[]);const report={passed:true,method:'Installed phone webcam (DroidCam/Camo)',cases,missingPhoneDoesNotUseLaptop:true,unknownNameManualSelection:true,deviceReconnect:true,permissionRetry:true,mobileLayout:true,runtimeErrors:errors,physicalPhoneTested:false};
    fs.writeFileSync(path.join(output,'CameraSelectionValidation.json'),JSON.stringify(report,null,2));console.log('CAMERA_BROWSER_PASS');
  }catch(error){await page.screenshot({path:path.join(output,'BrowserFailureDesktop.png'),fullPage:true});console.error('Diagnostics:',await page.locator('#feedback').textContent());throw error;}
  finally{await context.close();await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
