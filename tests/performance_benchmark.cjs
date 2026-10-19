const {chromium} = require('playwright');
const fs = require('node:fs');
const path = require('node:path');
const base = process.env.THEATRE_TEST_URL || 'http://127.0.0.1:5000';
const label = process.env.BENCHMARK_LABEL || 'baseline';
(async () => {
  const browser = await chromium.launch({channel:'chrome', headless:true, args:['--use-fake-device-for-media-stream','--use-fake-ui-for-media-stream']});
  const page = await browser.newPage({viewport:{width:1440,height:1000},deviceScaleFactor:1.5,permissions:['camera']});
  const result = [];
  try {
    for (const players of [1,2]) {
      const requests = new Map(), latency = [];
      let measuring = false;
      const requested = request => { if (measuring && new URL(request.url()).pathname === '/frame') requests.set(request, Date.now()); };
      const received = response => { if (requests.has(response.request())) { latency.push(Date.now()-requests.get(response.request())); requests.delete(response.request()); } };
      page.on('request',requested); page.on('response',received);
      await page.goto(`${base}/control?players=${players}&mode=gesture`,{waitUntil:'domcontentloaded'});
      await page.waitForFunction(() => document.getElementById('camera-status').textContent === '鏡頭已開啟',null,{timeout:120000});
      await page.waitForTimeout(3000);
      measuring = true;
      const stats = await page.evaluate(async () => {
        const video = document.querySelector('#camera') || document.querySelector('#preview');
        const intervals = []; let frames = 0, last = 0, running = true;
        function raf(now) { if (!running) return; if(last) intervals.push(now-last); last=now; requestAnimationFrame(raf); }
        function vf() { if(!running) return; frames++; video.requestVideoFrameCallback(vf); }
        requestAnimationFrame(raf); if(video.requestVideoFrameCallback) video.requestVideoFrameCallback(vf);
        const start=performance.now(); await new Promise(resolve=>setTimeout(resolve,15000)); running=false;
        const seconds=(performance.now()-start)/1000; intervals.sort((a,b)=>a-b);
        return {seconds,renderFps:intervals.length/seconds,cameraCallbackFps:frames/seconds,p95FrameMs:intervals[Math.floor(intervals.length*.95)],pixelRatio:devicePixelRatio,canvas:[document.querySelector('#unity-canvas').width,document.querySelector('#unity-canvas').height],preview:document.querySelector('#preview').tagName};
      });
      measuring=false; latency.sort((a,b)=>a-b);
      result.push({players,...stats,inferenceFps:latency.length/stats.seconds,frameRequestMedianMs:latency[Math.floor(latency.length*.5)],frameRequestP95Ms:latency[Math.floor(latency.length*.95)]});
      page.off('request',requested); page.off('response',received);
      await page.locator('#back').click();
    }
    fs.writeFileSync(path.resolve(__dirname,`../reports/Performance-${label}.json`),JSON.stringify(result,null,2));
    console.log(JSON.stringify(result,null,2));
  } finally {await browser.close();}
})().catch(error=>{console.error(error);process.exitCode=1;});
