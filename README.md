# 掌中智戲 Palm Theatre

以攝影機手部辨識控制 Unity 立體戲偶。整合版包含一人／兩人選擇、手勢動作／手部映射、月夜舞台、俠客與琴師、台語語音和中文字幕。

## 啟動

安裝並啟動 Docker Desktop，在這個儲存庫的根目錄執行：

```sh
docker compose up -d --build
```

開啟 <http://127.0.0.1:5000/>，使用 Chrome 或 Edge 並允許攝影機。
Windows 也可以執行 `Open-PalmTheatre.bat`；首次下載請先執行上方建置指令。
已包含可執行的 Unity WebGL 舞台，使用演出頁面不需要安裝 Unity Editor。

## 演出方式

- 首頁選擇 1–2 人及手勢動作／手部映射，按「進入舞台」。
- 一人固定為俠客；兩人為左側俠客、右側琴師，每人使用一隻手。
- 手勢模式維持指定手勢 0.5 秒，觸發動作及語音；雙人台詞輪流播放。
- 映射模式先保持自然姿勢約 1.5 秒校正，再用手掌及手指控制角色。
- 提供暫停、重新開始、音量、全螢幕、返回設定及重新校正。

桌面以約 75% 舞台、25% 攝影機與辨識狀態排列。手機及窄視窗改為上下排列。

## 攝影機效能修正

預覽直接播放本機鏡頭，骨架另外疊加。JPEG 編碼與上傳在背景 Worker 執行，辨識結果與控制狀態在同一個回應中傳回；同時最多處理一張影像，避免舊畫面排隊。辨識影像最大 640×480，維持原有辨識模型及舞台畫質。

在開發電腦以約 20 FPS 的模擬鏡頭和實際 Unity WebGL 舞台驗證，辨識更新由原本約 1 FPS 提升到約 20 FPS。實體鏡頭速度會隨設備及光線改變。鏡頭下方會顯示實際鏡頭與辨識 FPS。

## 專案結構

```text
hand-tracking/                 Flask、MediaPipe 與網頁介面
  static/theatre/              已建置的整合版 Unity WebGL
  static/theatre-ui/           介面、背景影像處理與控制傳輸
unity/UnityPuppetTest/         Unity 原始專案
  Assets/IntegratedTheatre/    四種演出模式與瀏覽器橋接
  CharacterSources/           原角色來源素材
audio/、voice/                語音、音效及背景音樂
voice-tools/                  語音產生與檢查工具
tests/                        演出隔離、辨識及瀏覽器驗證
reports/                      本次版本的驗證紀錄
```

使用 Unity **6000.5.9f1** 與 WebGL Build Support 開啟 `unity/UnityPuppetTest`。
重建目前整合舞台請使用 Unity 選單「掌中智戲 → 整合舞台 → 建置 WebGL」。
原有的 `Build-WebGL.bat` 建置較早的獨立測試頁；整合版請使用上述選單。

Unity 快取、個人設定、歷史擷取圖及本機語音模型執行環境不列入版本控制。
語音生成工具的安裝方式見 [voice-tools/README.md](voice-tools/README.md)。

## 驗證

辨識與演出邊界驗證共 50 個測試；瀏覽器測試涵蓋四種實際 WebGL 模式、40 次重置、語音暫停及攝影機拒絕後重試。另以 350 毫秒人工回應延遲驗證預覽獨立播放、影像不排隊及停止後不顯示舊骨架。

PowerShell 可使用容器的既有依賴執行 Python 測試：

```powershell
docker compose run --rm -v "${PWD}/tests:/tests:ro" -e PYTHONPATH=/app hand-tracking python /tests/test_performance_session.py
docker compose run --rm -v "${PWD}/tests:/tests:ro" -e PYTHONPATH=/app hand-tracking python -m unittest discover -s /tests/legacy -p 'test_*.py'
```

瀏覽器驗證需要 Node.js、Chrome 與 Playwright。先啟動本機服務，再執行：

```sh
npm install
npm run test:browser
npm run test:camera
npm run benchmark
```

自動測試使用模擬鏡頭及合成控制資料，不能代替真人手勢成功率與現場光線測試。瀏覽器測試會開始及重置演出，執行時請關閉其他正在演出的頁面。

更完整的操作說明見 [INTEGRATED_THEATRE.md](INTEGRATED_THEATRE.md)。既有整理紀錄保留在 [docs/README-original.md](docs/README-original.md)。第三方辨識程式來源見 [hand-tracking/THIRD_PARTY_NOTICES.md](hand-tracking/THIRD_PARTY_NOTICES.md)。
