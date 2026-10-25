# 掌中智戲 Palm Theatre

以攝影機手部辨識控制 Unity 立體戲偶。整合舞台支援一人俠客、雙人左俠客／右琴師，並提供三種控制模式：手勢動作、手部映射、Hybrid 混合操控。Hybrid 持續以手部映射操偶，同時辨識手勢並讓角色動畫暫時接管指定骨架部位。

## 啟動

最新修改與驗證範圍見 [CHANGELOG.md](CHANGELOG.md)，組員分支與回退方式見 [VERSION_CONTROL.md](VERSION_CONTROL.md)。

安裝並啟動 Docker Desktop，在儲存庫根目錄執行：

```sh
docker compose up -d --build
```

開啟 <http://127.0.0.1:5000/>，使用 Chrome 或 Edge 並允許攝影機。Windows 也可執行 `Open-PalmTheatre.bat`；首次下載請先執行上方建置指令。已建置的 WebGL 舞台可直接使用，演出不需要安裝 Unity Editor。

## 控制模式

- **手勢動作**：維持指定手勢 0.5 秒觸發動作及語音；雙人語音輪流播放。
- **手部映射**：保持自然姿勢約 1.5 秒校正，再用手掌及手指控制角色。
- **Hybrid**：映射持續控制角色；手勢 1–5 分別觸發 `BOW`、`LANDSCAPE`、`DRINK`、`ALERT`、`ATTACK`。校正後須先偵測手勢 0 持續 0.2 秒完成 arm；指令手勢需維持 0.5 秒，每個 action 各自冷卻 3 秒，每次事件後須回到手勢 0 才能再次 arm。雙人以逐角色 `actionSequence` 和 `inputEpoch` 隔離事件，防止重播或串線。動畫只暫時接管其骨架部位遮罩，其他部位映射不中斷；忙碌時消費新 action 而不排隊。動畫結束後依動作的淡出設定追蹤最新映射姿態，並等關節穩定接上後才交回操控，不再固定到時直接切換；詳見 [HYBRID_HANDOFF.md](HYBRID_HANDOFF.md)。雙人語音輪流播放。

三種模式皆支援單人與雙人，共六種演出組合。映射及 Hybrid 皆需校正；提供暫停、重新開始、音量、全螢幕、返回設定及重新校正。桌面以約 75% 舞台、25% 攝影機與辨識狀態排列，窄視窗改為上下排列。

## 攝影機與效能

攝影機預覽直接播放本機鏡頭，骨架另外疊加。JPEG 編碼與上傳在背景 Worker 執行，辨識結果與控制狀態同次回傳；同時最多處理一張影像，避免舊畫面排隊。辨識影像最大 640×480。鏡頭下方顯示實際鏡頭與辨識 FPS；實際速度依設備與光線而異。

## 專案與建置

```text
hand-tracking/                 Flask、MediaPipe 與網頁介面
  static/theatre/              Unity WebGL 整合版輸出
  static/theatre-ui/           介面、背景影像處理與控制傳輸
unity/UnityPuppetTest/         Unity 原始專案
  Assets/IntegratedTheatre/    整合場景、瀏覽器橋接與建置器
audio/、voice/                 語音、音效及背景音樂
tests/                         演出隔離、辨識及瀏覽器驗證
reports/                       驗證紀錄（依文件所述版本）
```

使用 Unity 專案指定版本及 WebGL Build Support 開啟 `unity/UnityPuppetTest`。整合版建置入口為 `Build-IntegratedTheatre.bat`／`Build-IntegratedTheatre.ps1`，呼叫 `IntegratedTheatreBuilder.Build`，預設輸出至 `hand-tracking/static/theatre/`；可傳入 `-UnityPath` 指定 Unity Editor。舊 `Build-WebGL.bat` 是較早的獨立測試建置入口，不會建置整合舞台。

**修改 Unity 原始碼後須重建 WebGL；獨立網頁 JS/CSS 更新後須重新載入網頁。** 本工作副本已更新混合模式 WebGL。2026-10-23 使用本機 Unity 6000.5.3f1 建置成功；儲存庫原指定版本 6000.5.9f1 保留不變。62 項 Python 測試、原四組模式瀏覽器回歸及兩組 Hybrid 瀏覽器測試通過。完整範圍與異常見 [HYBRID_PROGRESS.md](HYBRID_PROGRESS.md)。

## Hybrid 工作與驗收狀態

Hybrid 動作已完成第一輪逐動作流暢度調整，詳見 [MOTION_REVIEW.md](MOTION_REVIEW.md)。真人跟手感及表演自然度仍需實測。

- [x] 文件定義三種模式、單／雙人、手勢與動畫交接規則：見 [HYBRID_MODE.md](HYBRID_MODE.md)。
- [x] 實作辨識輸出、逐角色 event sequence／`inputEpoch`、Unity 部位遮罩交接及介面選項。
- [x] Python baseline（50 項）、Hybrid（12 項）與 Unity baseline compile 測試通過。
- [x] 完成 Hybrid Unity/WebGL 建置及模擬輸入瀏覽器驗證，並更新報告。
- [ ] 實際鏡頭端到端驗收與真人手勢成功率確認。

Unity Play Mode 骨架檢查均通過，但完整 Editor 驗收另記錄一筆 UnityEditor.Search 搜尋索引例外，因此該報告整體仍為未通過；未隱藏或忽略此異常。瀏覽器測試使用實際新版 WebGL，不是以 C# 編譯結果代替。

更完整的操作與架構說明見 [INTEGRATED_THEATRE.md](INTEGRATED_THEATRE.md)。第三方辨識程式來源見 [hand-tracking/THIRD_PARTY_NOTICES.md](hand-tracking/THIRD_PARTY_NOTICES.md)。
