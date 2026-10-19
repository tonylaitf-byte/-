# 掌中智戲：整合演出入口

開啟本機 `http://127.0.0.1:5000/`，或雙擊桌面專案資料夾中的 `Open-PalmTheatre.bat`。不需要另外開 Unity Editor 或手動換測試場景。

## 操作流程

1. 首頁只選 **一人／兩人** 及 **手勢動作／手部映射**，按「進入舞台」。
2. 舞台載入後自動嘗試開啟攝影機；首次請允許攝影機存取。拒絕權限時可以按「開啟鏡頭」重試。程式不開啟麥克風。
3. 單人固定為俠客；雙人固定左俠客、右琴師。每人使用一隻手，左右手皆可。
4. 手勢模式維持指定手勢 0.5 秒，觸發動作、既有台語錄音與中文字幕。雙人語音輪流播放。
5. 映射模式先保持自然手勢約 1.5 秒，校正後控制頭部、雙臂及身體。雙人可分別重新校正；此模式沿用即時操偶行為，不自動觸發整段台詞。

桌面配置為舞台約 75%、右側辨識區約 25%；字幕在舞台下方。小視窗與手機改為上下排列。沿用月夜廟宇、原背景音樂、俠客與琴師的既有模型和動作。

舞台提供暫停／繼續、重新開始、音量、全螢幕、返回設定；雙人映射另有月琴顯示／收起。停止鏡頭會釋放攝影機，重新開啟映射鏡頭後需再次校正。

鏡頭下方顯示實際的鏡頭與辨識 FPS。攝影機預覽直接在本機播放，辨識骨架另行疊加；辨識速度低於攝影機時，預覽仍保持流暢。使用支援 Web Worker、OffscreenCanvas 的新版 Chrome 或 Edge。

影像編碼與上傳由背景 Worker 處理，避免 Unity 佔用主執行緒時，HTML canvas 的閒置編碼工作長時間延後。辨識影像保留比例、最大 640×480，同時僅允許一張影像處理中，避免舊影像排隊。每張影像直接回傳同次辨識的控制狀態及骨架；整合頁不再下載 MJPEG，正常追蹤時也不另外輪詢狀態。辨識模型、手勢門檻及原舞台畫質維持原設定。

## 場景與資料生命週期

- 同一份 WebGL 內含入口及四個演出場景，選項經確認後才開放辨識輸入。
- 每次開始／重置產生新演出識別，清除舊手勢、角色歸屬及校正；Unity 重載本模式場景，清除舊語音、字幕及動作。
- 返回設定會停止攝影機與演出。不同頁面同時操作時，以最後開始的整合演出為準，舊頁面會停止輸入並提示。
- 請只開一個正在演出的頁面。整合演出進行中，舊獨立辨識頁無法改寫人數／模式；返回設定後可繼續使用舊測試頁。

## 開發檔案

- `hand-tracking/templates/index.html`：人數與模式選擇。
- `hand-tracking/templates/control.html`、`static/theatre-ui/`：共同舞台、攝影機與狀態。
- `hand-tracking/performance_session.py`：演出識別、輸入隔離、校正與重置。
- Unity `Assets/IntegratedTheatre/`：獨立整合場景、瀏覽器橋接、建置器。
- Unity 既有 `ActionVoicePresenter` 新增可讀字幕及可關閉的舊字幕畫面；既有獨立場景仍保留原顯示。
- Unity `PuppetArchitectureMaterialPass` 允許重新建立卸載後的材質快取，支援多次進出演出。
- WebGL 成品在 `hand-tracking/static/theatre/`；舊 `static/unity/` 保留。

Unity 選單「掌中智戲 → 整合舞台 → 建置 WebGL」可重建四種模式，不重新製作原角色或動畫。建置輸出也可使用 `THEATRE_BUILD_OUTPUT` 環境變數指定。

## 驗證範圍

`reports/BrowserValidation.json` 記錄實際 WebGL 場景、角色數、輸入、暫停、重置及鏡頭拒絕後重試測試；`YueXianWorkspace/IntegrationReports/Build.json` 記錄 Unity 建置結果。

`tests/performance_benchmark.cjs` 以相同 Unity 舞台、1440×1000 視窗、1.5 倍像素比例與 Chrome 模擬鏡頭量測每種人數 15 秒。改善前後數據存於 `reports/Performance-baseline.json` 與 `reports/Performance-optimized.json`。此模擬鏡頭約 20 FPS；真人鏡頭幀率依設備與光線而異。

`tests/camera_latency_validation.cjs` 額外延遲辨識回應 350 毫秒，驗證預覽獨立播放、最多一張待處理影像、骨架位置，以及停止鏡頭後不會重新顯示舊骨架。`tests/test_performance_session.py` 驗證新 JSON 回傳及原 204 影像介面的相容性。

瀏覽器自動測試使用模擬攝影機與合成控制資料，能驗證畫面、傳輸及舞台邏輯，不能代替真人手勢成功率、現場光線、鏡頭延遲與主觀動作品質驗收。

安裝前備份及逐檔雜湊清單保存在 `reports/BeforeIntegration_*`、`reports/InstallManifest.json`；原單人／雙人測試場景沒有被覆寫。
