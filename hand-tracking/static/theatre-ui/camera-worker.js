'use strict';
// Encoding must not wait for idle time on Unity's busy main thread.
let canvas, context;
self.onmessage = async ({data}) => {
  const {bitmap, sessionId, inputRevision, calibrationRevision = 0} = data;
  try {
    const scale = Math.min(1, 640 / bitmap.width, 480 / bitmap.height);
    const width = Math.max(1, Math.round(bitmap.width * scale));
    const height = Math.max(1, Math.round(bitmap.height * scale));
    if (!canvas || canvas.width !== width || canvas.height !== height) {
      canvas = new OffscreenCanvas(width, height);
      context = canvas.getContext('2d', {alpha:false});
    }
    context.drawImage(bitmap, 0, 0, width, height);
    bitmap.close();
    const blob = await canvas.convertToBlob({type:'image/jpeg', quality:.76});
    const response = await fetch('/frame', {method:'POST', headers:{
      'Content-Type':'image/jpeg', 'Accept':'application/json', 'X-Performance-Session':sessionId,
      'X-Calibration-Revision':String(calibrationRevision)
    }, body:blob, signal:AbortSignal.timeout(5000)});
    const result = await response.json();
    if (!response.ok) throw Object.assign(new Error(result.error || '影像傳送失敗，請確認辨識服務。'), {status:response.status});
    self.postMessage({result, inputRevision});
  } catch (error) {
    bitmap.close();
    self.postMessage({error:error.message, status:error.status, inputRevision});
  }
};
