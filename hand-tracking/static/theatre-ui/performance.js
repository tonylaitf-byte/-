'use strict';
(() => {
  const $ = id => document.getElementById(id);
  const query = new URLSearchParams(location.search);
  const requestedMode = query.get('mode');
  const config = {players: query.get('players') === '2' ? 2 : 1, mode: ['mapping', 'hybrid'].includes(requestedMode) ? requestedMode : 'gesture'};
  const duet = config.players === 2, hybrid = config.mode === 'hybrid', mapping = config.mode !== 'gesture';
  const video = $('preview'), overlay = $('hand-overlay'), overlayContext = overlay.getContext('2d');
  let unity = null, session = null, stream = null, ready = false, paused = false, leaving = false;
  let generation = 0, cameraGeneration = 0, cameraStarting = false, sending = false, sequence = 0, lastCapture = 0;
  let readyWaiter = null, latestStatus = null, requestError = false;
  let inputRevision = 0, inputPending = false;
  let cameraWorker = null, lastResult = 0, overlayTimeout = null;
  let metricStart = 0, cameraFrames = 0, recognizedFrames = 0, previousPresented = null;
  const connections = [[0,1],[1,2],[2,3],[3,4],[0,5],[5,6],[6,7],[7,8],[5,9],[9,10],[10,11],[11,12],[9,13],[13,14],[14,15],[15,16],[13,17],[0,17],[17,18],[18,19],[19,20]];
  const warriorNames = ['鞠躬', '賞月', '喝酒', '警惕', '舞劍'];
  const musicianNames = ['抱琴行禮', '慢速撥琴', '停琴回應', '急弦挑戰', '快速演奏'];
  const poses = ['僅伸出食指', '食指＋中指', '拇指＋食指＋中指', '拇指收進掌心，其餘四指伸出', '五指張開'];
  $('performance-title').textContent = duet ? '月下琴劍 · 兩人對戲' : '俠客 · 一人演出';
  $('mode-label').textContent = hybrid ? '混合操控' : mapping ? '手部映射' : '手勢動作';
  $('zone-labels').hidden = !duet;
  $('instrument').hidden = !(duet && mapping);
  $('stage-hint').textContent = duet ? '左側俠客、右側琴師。每人使用一隻手，左右手皆可；兩個角色各自留在舞台半邊。' : '單人預設角色為俠客。使用一隻手即可操作，左右手皆可。';
  $('help-title').textContent = hybrid ? '即時操偶 · 手勢演出' : mapping ? '自然姿勢校正後開始操偶' : '手勢維持 0.5 秒，開始演出';
  const help = $('instructions');
  if (mapping) {
    help.innerHTML = '<p>拇指 → 左臂<br>小指 → 右臂<br>中指 → 頭部<br>移動、轉動手掌 → 身體</p>';
    const detail = document.createElement('p');
    detail.textContent = hybrid ? '校正完成後，先保持非 1–5 的自然手勢 0.2 秒，再維持指定手勢 0.5 秒。每次觸發後須回到自然手勢；相同動作冷卻 3 秒。' : '將手放在舒適位置，保持自然姿勢約 1.5 秒，等待校正完成。映射模式以即時操偶為主，不自動播放整段台詞。';
    help.append(detail);
  }
  if (!mapping || hybrid) {
    poses.forEach((pose, i) => {
      const row = document.createElement('div'); row.className = 'gesture-row';
      const number = document.createElement('b'); number.textContent = i + 1;
      const text = document.createElement('span'); text.textContent = pose;
      const action = document.createElement('small'); action.textContent = warriorNames[i] + (duet ? ' ／ ' + musicianNames[i] : '');
      text.append(action); row.append(number, text); help.append(row);
    });
    if (!hybrid) { const detail = document.createElement('p'); detail.textContent = duet ? '動作與台詞完整播放，兩角台詞輪流進行。手掌左右移動可平移角色。' : '動作與台詞完整播放；手掌左右移動可平移角色。'; help.append(detail); }
  }
  if (duet) { const note = document.createElement('p'); note.textContent = '辨識畫面左區控制俠客、右區控制琴師，中央 45%–55% 為緩衝區。'; help.append(note); }
  const sides = duet ? ['left', 'right'] : ['left'];
  sides.forEach(side => {
    const role = document.createElement('section'); role.className = 'role-state';
    const name = side === 'left' ? '俠客' : '琴師';
    role.innerHTML = `<header><strong>${duet ? (side === 'left' ? '左側 · ' : '右側 · ') : ''}${name}</strong><span id="${side}-owner">等待手部</span></header><div class="role-detail" id="${side}-detail">${mapping ? '等待自然姿勢校正' : '等待穩定手勢'}</div><div class="meter"><span id="${side}-progress"></span></div>`;
    if (hybrid) {
      const gesture = document.createElement('div'); gesture.className = 'hybrid-gesture';
      gesture.id = side + '-gesture'; gesture.textContent = '等待校正 · 手勢未啟用';
      const meter = document.createElement('div'); meter.className = 'meter hybrid-meter';
      const fill = document.createElement('span'); fill.id = side + '-gesture-progress'; meter.append(fill);
      role.append(gesture, meter);
    }
    if (mapping) { const button = document.createElement('button'); button.className = 'calibrate'; button.textContent = duet ? '重新校正' + name : '重新校正'; button.disabled = true; button.dataset.side = side; button.addEventListener('click', () => calibrate(side)); role.append(button); }
    $('role-states').append(role);
  });
  function feedback(message, error = false) { $('feedback').textContent = message; $('feedback').classList.toggle('error', error); }
  function controls() {
    $('camera-button').disabled = !ready || cameraStarting || paused || inputPending;
    $('pause').disabled = !ready || inputPending;
    $('reset').disabled = !ready;
    $('instrument').disabled = !ready;
    document.querySelectorAll('.calibrate').forEach(b => b.disabled = !ready || paused || inputPending);
  }
  function command(method, data = '') { if (unity) unity.SendMessage('TheatreBridge', method, data); }
  async function api(path, body, token = session?.sessionId) {
    const headers = {'X-Performance-Session': token || ''};
    if (body !== undefined) headers['Content-Type'] = 'application/json';
    const response = await fetch(path, {method: body === undefined ? 'GET' : 'POST', headers, body: body === undefined ? undefined : JSON.stringify(body), cache: 'no-store', signal: AbortSignal.timeout(8000)});
    if (!response.ok) {
      const payload = await response.json().catch(() => ({}));
      throw Object.assign(new Error(payload.error || '辨識服務未回應，請稍後重試。'), {status: response.status});
    }
    return response.json();
  }
  window.onTheatreStatus = status => {
    if (!session || status.sessionId !== session.sessionId || leaving) return;
    latestStatus = status;
    if (status.error && readyWaiter) { readyWaiter.reject(new Error(status.error)); readyWaiter = null; }
    if (status.ready && readyWaiter) { readyWaiter.resolve(); readyWaiter = null; }
    if (status.ready) {
      $('subtitle').textContent = status.subtitle || (paused ? '演出已暫停' : mapping ? '隨手而動，自由操偶' : '等待你的手勢，讓故事開始。');
      $('stage-status').textContent = paused ? '演出已暫停' : status.speaking ? '台詞演出中' : '舞台就緒';
    }
  };
  function loadUnity() {
    return new Promise((resolve, reject) => {
      const loader = document.createElement('script');
      loader.src = '/static/theatre/Build/theatre.loader.js';
      loader.onerror = () => reject(new Error('舞台檔案尚未就緒，請重新載入或執行整合版啟動器。'));
      loader.onload = async () => {
        try {
          const instance = await createUnityInstance($('unity-canvas'), {
            dataUrl: '/static/theatre/Build/theatre.data', frameworkUrl: '/static/theatre/Build/theatre.framework.js', codeUrl: '/static/theatre/Build/theatre.wasm',
            streamingAssetsUrl: '/static/theatre/StreamingAssets', companyName: 'PalmTheatre', productName: '掌中智戲', productVersion: '1.0',
            devicePixelRatio: Math.min(window.devicePixelRatio || 1, 1.5)
          }, value => { $('load-progress').value = value; $('load-detail').textContent = `載入舞台 ${Math.round(value * 100)}%`; });
          resolve(instance);
        } catch (error) { reject(new Error('舞台載入失敗：' + (error.message || error))); }
      };
      document.body.append(loader);
    });
  }
  async function begin() {
    const current = ++generation;
    ready = false; paused = false; requestError = false; latestStatus = null; sequence = 0;
    inputRevision++; inputPending = false;
    stopLocalCamera(); controls(); $('pause').textContent = '暫停演出';
    $('loading').hidden = false; $('retry').hidden = true; $('load-progress').hidden = false;
    $('loading-label').textContent = '正在準備演出…'; $('subtitle').textContent = '舞台準備中';
    feedback('正在套用人數與操作模式…');
    try {
      if (unity) command('SetPaused', 'true');
      session = await api('/performance/start', config, '');
      if (current !== generation || leaving) return;
      if (!unity) unity = await loadUnity();
      if (current !== generation || leaving) { await unity.Quit(); return; }
      const configured = new Promise((resolve, reject) => { readyWaiter = {resolve, reject}; });
      const timeout = setTimeout(() => { if (readyWaiter) { readyWaiter.reject(new Error('舞台切換逾時，請重新載入。')); readyWaiter = null; } }, 45000);
      command('Configure', JSON.stringify(session));
      try { await configured; } finally { clearTimeout(timeout); }
      if (current !== generation || leaving) return;
      ready = true; controls(); $('loading').hidden = true;
      command('SetVolume', $('volume').value);
      feedback(mapping ? '請保持自然手勢，等待校正完成。' : '舞台已就緒，請將手放進辨識區。');
      poll(current);
      await startCamera();
    } catch (error) {
      if (current !== generation || leaving) return;
      ready = false; controls();
      $('loading-label').textContent = '暫時無法進入舞台'; $('load-detail').textContent = error.message;
      $('retry').hidden = false; $('load-progress').hidden = true;
      feedback(error.message, true);
      // A failed load must not keep legacy pages locked behind an abandoned session.
      if (session) await api('/performance/stop', {}, session.sessionId).catch(() => {});
      session = null;
    }
  }
  function stopLocalCamera() {
    cameraGeneration++; cameraStarting = false;
    if (cameraWorker) cameraWorker.terminate();
    cameraWorker = null; sending = false; lastResult = 0;
    clearTimeout(overlayTimeout); drawHands([]);
    if (hybrid) sides.forEach(side => updateRole(side, {}));
    if (stream) stream.getTracks().forEach(track => track.stop());
    stream = null; video.srcObject = null;
    $('camera-metrics').textContent = '鏡頭未開啟';
    $('camera-empty').hidden = false; $('camera-message').textContent = '按「開啟鏡頭」開始辨識';
    $('camera-status').textContent = '鏡頭未開啟'; $('camera-button').textContent = '開啟鏡頭';
    controls();
  }
  async function stopCamera() {
    stopLocalCamera(); command('Disconnect');
    const current = cameraGeneration;
    cameraStarting = true; controls();
    try { if (session) await api('/performance/input-stop', {}); }
    catch (error) { feedback(error.message, true); }
    finally { if (current === cameraGeneration) cameraStarting = false; controls(); }
  }
  async function startCamera() {
    if (!ready || stream || cameraStarting || leaving || paused) return;
    const current = ++cameraGeneration;
    cameraStarting = true; controls(); $('camera-message').textContent = '正在啟動攝影機…';
    try {
      if (hybrid) {
        inputRevision++;
        await api('/performance/input-stop', {});
        if (current !== cameraGeneration || leaving || !ready) return;
      }
      if (!navigator.mediaDevices?.getUserMedia) throw new Error('這個瀏覽器無法開啟攝影機，請使用本機 Chrome 或 Edge。');
      const acquired = await navigator.mediaDevices.getUserMedia({video: {width: {ideal: 640}, height: {ideal: 480}, frameRate: {ideal: 30}}, audio: false});
      if (current !== cameraGeneration || leaving || !ready) { acquired.getTracks().forEach(track => track.stop()); return; }
      stream = acquired; video.srcObject = stream; await video.play();
      if (current !== cameraGeneration || leaving) return;
      const width = video.videoWidth || 640, height = video.videoHeight || 480;
      overlay.width = width; overlay.height = height;
      video.parentElement.style.aspectRatio = `${width} / ${height}`;
      cameraWorker = new Worker('/static/theatre-ui/camera-worker.js');
      cameraWorker.onmessage = ({data}) => {
        if (current !== cameraGeneration || leaving || !ready) return;
        sending = false;
        if (data.inputRevision !== inputRevision) return;
        if (data.error) { handleInputError(Object.assign(new Error(data.error), {status:data.status})); return; }
        if (paused || data.result.sessionId !== session?.sessionId) return;
        recognizedFrames++; lastResult = performance.now();
        applyState(data.result); drawHands(data.result.landmarks || []);
        clearTimeout(overlayTimeout);
        overlayTimeout = setTimeout(() => drawHands([]), 350);
      };
      cameraWorker.onerror = () => {
        if (current !== cameraGeneration) return;
        stopCamera(); feedback('影像處理無法啟動，請更新 Chrome 或 Edge 後重新開啟鏡頭。', true);
      };
      metricStart = performance.now(); cameraFrames = 0; recognizedFrames = 0; previousPresented = null; lastCapture = 0;
      stream.getVideoTracks().forEach(track => track.addEventListener('ended', () => { if (current === cameraGeneration) { stopCamera(); feedback('攝影機已中斷，請重新開啟。', true); } }));
      $('camera-empty').hidden = true; $('camera-button').textContent = '停止鏡頭'; $('camera-status').textContent = '鏡頭已開啟';
      $('camera-metrics').textContent = '正在測量幀率…';
      feedback(mapping ? '鏡頭已啟動，請保持自然姿勢約 1.5 秒。' : '鏡頭已啟動，將手勢穩定維持 0.5 秒。');
      scheduleCapture(current);
    } catch (error) {
      if (current !== cameraGeneration) return;
      stopLocalCamera();
      const message = error.name === 'NotAllowedError' ? '請允許攝影機存取，再按「開啟鏡頭」重試。' : error.name === 'NotFoundError' ? '找不到攝影機，請連接後重試。' : error.name === 'NotReadableError' ? '攝影機可能被其他程式占用，關閉後重試。' : error.message;
      $('camera-message').textContent = message; feedback(message, true);
    } finally { if (current === cameraGeneration) cameraStarting = false; controls(); }
  }
  function scheduleCapture(current) {
    if (video.requestVideoFrameCallback) video.requestVideoFrameCallback((timestamp, metadata) => capture(timestamp, current, metadata));
    else requestAnimationFrame(timestamp => capture(timestamp, current));
  }
  function drawHands(hands) {
    overlayContext.clearRect(0, 0, overlay.width, overlay.height);
    overlayContext.lineWidth = Math.max(2, overlay.width / 240);
    overlayContext.strokeStyle = '#79efb5'; overlayContext.fillStyle = '#ffdc97';
    hands.forEach(hand => {
      if (hand.length !== 21) return;
      overlayContext.beginPath();
      connections.forEach(([a,b]) => {
        overlayContext.moveTo(hand[a][0] * overlay.width, hand[a][1] * overlay.height);
        overlayContext.lineTo(hand[b][0] * overlay.width, hand[b][1] * overlay.height);
      });
      overlayContext.stroke();
      hand.forEach(([x,y]) => { overlayContext.beginPath(); overlayContext.arc(x*overlay.width, y*overlay.height, overlay.width/160, 0, Math.PI*2); overlayContext.fill(); });
    });
  }
  function capture(timestamp, current, metadata) {
    if (!stream || current !== cameraGeneration || leaving) return;
    if (metadata) {
      if (previousPresented !== null) cameraFrames += metadata.presentedFrames - previousPresented;
      previousPresented = metadata.presentedFrames;
    }
    if (timestamp - metricStart >= 1000) {
      const seconds = (timestamp - metricStart) / 1000;
      $('camera-metrics').textContent = paused ? '辨識已暫停' : `${metadata ? '鏡頭 ' + Math.round(cameraFrames / seconds) + ' FPS · ' : ''}辨識 ${Math.round(recognizedFrames / seconds)} FPS`;
      metricStart = timestamp; cameraFrames = 0; recognizedFrames = 0;
    }
    if (!paused && !inputPending && ready && !sending && timestamp - lastCapture >= 30 && video.readyState >= 2) {
      sending = true; lastCapture = timestamp;
      const token = session.sessionId, worker = cameraWorker, revision = inputRevision;
      const calibrationRevision = session.calibrationRevision;
      createImageBitmap(video).then(bitmap => {
        if (current !== cameraGeneration || worker !== cameraWorker) { bitmap.close(); return; }
        if (!ready || paused || inputPending || revision !== inputRevision || session?.sessionId !== token) { bitmap.close(); sending = false; return; }
        worker.postMessage({bitmap, sessionId:token, inputRevision:revision, calibrationRevision}, [bitmap]);
      }).catch(error => { if (current === cameraGeneration) { sending = false; handleInputError(error); } });
    }
    scheduleCapture(current);
  }
  function updateRole(side, state) {
    state = state || {};
    const controlsState = duet && mapping ? state.controls || {} : state;
    const detected = !!state.handDetected;
    $(side + '-owner').textContent = detected ? '追蹤中' : state.ownerHeld ? '暫失追蹤' : '等待手部';
    const progress = mapping ? controlsState.calibrationProgress : state.gestureHold;
    $(side + '-progress').style.width = Math.round(Math.max(0, Math.min(1, Number(progress) || 0)) * 100) + '%';
    if (mapping) $(side + '-detail').textContent = !detected ? '請將手放入辨識區' : controlsState.calibrated ? '校正完成 · 即時操偶中' : '保持自然姿勢 · 校正 ' + Math.round((Number(progress) || 0) * 100) + '%';
    else {
      const gesture = Number(state.gesture || 0), name = (side === 'right' ? musicianNames : warriorNames)[gesture - 1];
      $(side + '-detail').textContent = detected && name ? `${gesture} · ${name} · ${Number(progress) >= 1 ? '已確認' : '確認中'}` : '等待穩定手勢';
    }
    if (hybrid) {
      const gesture = Number(controlsState.gesture || 0);
      const names = side === 'right' ? musicianNames : warriorNames;
      const lastIndex = ['BOW', 'LANDSCAPE', 'DRINK', 'ALERT', 'ATTACK'].indexOf(controlsState.action);
      const latest = lastIndex >= 0 ? ` · 最近：${names[lastIndex]} #${controlsState.actionSequence}` : '';
      $(side + '-gesture').textContent = !detected ? '無追蹤 · 手勢未啟用' : !controlsState.calibrated ? '校正中 · 手勢未啟用' : `${gesture ? gesture + ' · ' + names[gesture - 1] : '自然手勢'} · ${controlsState.gestureArmed ? (gesture ? '確認中' : '已就緒') : '等待自然手勢 0.2 秒'}${latest}`;
      $(side + '-gesture-progress').style.width = Math.round(Math.max(0, Math.min(1, Number(controlsState.gestureHold) || 0)) * 100) + '%';
    }
  }
  function applyState(result) {
    if (paused || inputPending || result.sessionId !== session?.sessionId) return;
    if (result.discardedFrame || result.calibrationRevision !== session.calibrationRevision) return;
    const state = result.state;
    sides.forEach(side => updateRole(side, duet ? state[side] : state));
    command('ReceiveState', JSON.stringify({sessionId: session.sessionId, sequence: ++sequence, state: JSON.stringify(state)}));
    if (requestError && stream) { requestError = false; feedback('辨識服務已恢復，請將手放入辨識區。'); }
  }
  function handleInputError(error) {
    command('Disconnect'); requestError = true; feedback(error.message, true);
    drawHands([]);
    if (error.status === 409) {
      ready = false; stopLocalCamera(); command('SetPaused', 'true');
      $('stage-status').textContent = '演出已由另一個頁面接手';
      $('loading').hidden = false; $('loading-label').textContent = '本次演出已結束';
      $('load-detail').textContent = error.message; $('load-progress').hidden = true; $('retry').hidden = false;
      controls();
    }
  }
  async function poll(current) {
    if (current !== generation || leaving || !ready) return;
    try {
      // Frames carry their own atomic state. Poll only when idle/stalled so the
      // watchdog can release a lost hand without competing with normal input.
      if (!stream || paused || performance.now() - lastResult > 500) {
        const frameAtRequest = lastResult, revision = inputRevision;
        const result = await api('/performance/state');
        if (current !== generation || leaving || result.sessionId !== session?.sessionId) return;
        if (lastResult === frameAtRequest && revision === inputRevision) applyState(result);
      }
    } catch (error) {
      if (current !== generation || leaving) return;
      handleInputError(error);
    }
    if (current === generation && !leaving && ready) setTimeout(() => poll(current), 250);
  }
  async function calibrate(side) {
    if (inputPending) return;
    const current = generation;
    inputRevision++; inputPending = true; controls();
    clearTimeout(overlayTimeout); drawHands([]);
    try {
      const result = await api('/performance/calibrate', {side});
      if (current !== generation || leaving) return;
      session.calibrationRevision = result.calibrationRevision;
      inputPending = false;
      applyState(result);
      feedback('請保持自然姿勢穩定，等待重新校正完成。');
    }
    catch (error) {
      if (current !== generation || leaving) return;
      // The reset may have completed despite a lost response. Recover its
      // revision before allowing new frames to reach the detector.
      try {
        const result = await api('/performance/state');
        if (current !== generation || leaving) return;
        session.calibrationRevision = result.calibrationRevision;
      } catch (recoveryError) { handleInputError(recoveryError); stopLocalCamera(); }
      feedback(error.message, true);
    }
    finally { if (current === generation) { inputPending = false; controls(); } }
  }
  function cleanup() {
    if (leaving) return;
    leaving = true; generation++; ready = false; stopLocalCamera();
    command('EndPerformance');
    if (session) navigator.sendBeacon('/performance/stop', new Blob([JSON.stringify({sessionId: session.sessionId})], {type: 'application/json'}));
  }
  $('camera-button').addEventListener('click', () => stream ? stopCamera() : startCamera());
  $('reset').addEventListener('click', begin);
  $('pause').addEventListener('click', async () => {
    if (inputPending) return;
    const next = !paused, current = generation;
    if (hybrid) {
      inputRevision++; inputPending = true;
      if (next) {
        paused = true; command('SetPaused', 'true');
        clearTimeout(overlayTimeout); drawHands([]);
        sides.forEach(side => {
          $(side + '-gesture').textContent = '手勢已暫停';
          $(side + '-gesture-progress').style.width = '0%';
        });
      }
      controls();
      try { await api('/performance/pause', {paused: next}); }
      catch (error) {
        if (current !== generation) return;
        paused = true; command('SetPaused', 'true');
        inputPending = false; $('pause').textContent = '繼續演出'; controls();
        handleInputError(error); return;
      }
      if (current !== generation || leaving) return;
      inputPending = false;
      if (!next) sides.forEach(side => { $(side + '-gesture').textContent = '等待自然手勢 0.2 秒'; });
    }
    paused = next; command('SetPaused', String(paused));
    if (paused) { clearTimeout(overlayTimeout); drawHands([]); }
    $('pause').textContent = paused ? '繼續演出' : '暫停演出';
    $('camera-status').textContent = paused ? '辨識已暫停' : stream ? '鏡頭已開啟' : '鏡頭未開啟';
    feedback(paused ? '演出與辨識已暫停。' : '演出繼續，請將手放回辨識區。'); controls();
  });
  $('volume').addEventListener('input', () => command('SetVolume', $('volume').value));
  $('instrument').addEventListener('click', () => command('ToggleInstrument'));
  $('fullscreen').addEventListener('click', async () => {
    try { if (document.fullscreenElement) await document.exitFullscreen(); else await document.querySelector('.performance-grid').requestFullscreen(); }
    catch (_) { feedback('瀏覽器未允許全螢幕，仍可在目前視窗演出。'); }
  });
  $('retry').addEventListener('click', () => location.reload());
  ['back', 'back-link'].forEach(id => $(id).addEventListener('click', cleanup));
  window.addEventListener('pagehide', cleanup);
  window.addEventListener('pageshow', event => { if (event.persisted) location.reload(); });
  begin();
})();
