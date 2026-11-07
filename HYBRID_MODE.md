# Hybrid 混合操控規格

## 目標與範圍

新增第三種控制模式 `hybrid`，保留既有 `gesture` 與 `mapping`，三種模式均支援單人和雙人。Hybrid 對同一角色、同一辨識輸入同時提供連續映射狀態與離散手勢事件：映射持續更新；有效手勢事件觸發角色動畫、語音及字幕。模式名稱、封包欄位及場景名稱應使用穩定識別值，UI 顯示可本地化。

演出角色沿用既有分配：單人為俠客；雙人左側俠客、右側琴師。每位角色獨立維護辨識、校正、手勢 arming、`actionSequence`、`inputEpoch`、動畫 busy 狀態、mapping 目標及 per-action 冷卻，不能跨角色共用可變事件狀態。Unity Hybrid 執行期由 `HybridTheatreSession` 與各角色的 `HybridPuppetActor`／`HybridEventGate` 負責，不沿用全身 Idle handoff 作為 Hybrid 交接機制。

## 手勢與 arming 狀態機

手勢 ID 對應如下：

| Gesture ID | Action ID |
| --- | --- |
| 0 | Neutral／re-arm 姿勢，不觸發動作 |
| 1 | `BOW` |
| 2 | `LANDSCAPE` |
| 3 | `DRINK` |
| 4 | `ALERT` |
| 5 | `ATTACK` |

每角色使用以下狀態轉移，計時須使用單調時鐘；手部遺失、演出重置、校正重啟或 input epoch 改變時清除計時與 arming。Cooldown 以 action ID 為鍵，各 action 各自計時，不是每角色共用一個 3 秒冷卻：

1. **未校正**：不允許觸發手勢事件。映射校正完成後進入 `WAIT_NEUTRAL`。
2. **`WAIT_NEUTRAL`**：須先連續辨識 gesture 0 至少 0.2 秒，才進入 `ARMED`。校正完成時若已是中性手勢，仍需從校正後開始累計。
3. **`ARMED`**：接受 gesture 1–5。相同指令手勢連續維持 0.5 秒且該 action 自己的 3 秒冷卻已結束，產生單一事件並進入 `WAIT_NEUTRAL`。不同手勢切換時，重新開始 hold 計時。
4. **`WAIT_NEUTRAL`**：每個事件後必須再次連續 gesture 0 達 0.2 秒才可重新 arm；不得因冷卻時間自然結束而自動重新 arm。
5. 事件觸發後該 action 冷卻 3 秒。同一 action 冷卻期間仍更新辨識與映射；若已完成 neutral re-arm，冷卻結束後方可再次觸發該 action。其他 action 不受此 action 的 cooldown 阻擋。冷卻期間不得補發指令事件。

Hold 期間追蹤中斷或觀測無效時，依追蹤狀態規則清除／重置該次 hold，不可累加未觀測時間。每個確認事件僅發送一次，不能每影格重複。

## Hybrid 狀態與事件接口

辨識端每個輸入影格只辨識一次。傳輸 envelope 沿用整合橋接介面：外層 `sessionId`、`sequence`、`state`，其中 `state` 是 JSON 字串；Unity 解開 envelope 後再依單／雙人解析 state。單人 Hybrid state 的實際欄位如下：

```json
{
  "sessionId": "performance-session",
  "sequence": 123,
  "state": "{\"version\":2,\"controlMode\":\"hybrid\",\"handDetected\":true,\"calibrated\":true,\"leftHandX\":0.1,\"leftHandY\":0.2,\"gesture\":3,\"gestureHold\":1.0,\"gestureArmed\":false,\"action\":\"DRINK\",\"actionSequence\":8,\"inputEpoch\":4}"
}
```

`state` 字串解析後的單人 Hybrid state 為 `version:2`、`controlMode:"hybrid"`，保留 V2 mapping 所需欄位，並帶 `gesture`、`gestureHold`、`gestureArmed`、`action`、`actionSequence`、`inputEpoch`。不得另加 `eventSeq`、`actionEvent` 或 `playerId` 欄位。Mapping 欄位每個有效辨識影格更新，不得等待 action event。

雙人 Hybrid 的 state 是 duet root，含 `version:1`、`controlMode:"duet_hybrid"` 及 `left`／`right` slots；每個 slot 有 `ownerHeld`、`ownerId`、`handDetected`、`controls`，其中 `controls` 是上述 hybrid V2 state：

```json
{
  "sessionId": "performance-session",
  "sequence": 124,
  "state": "{\"version\":1,\"controlMode\":\"duet_hybrid\",\"left\":{\"ownerHeld\":true,\"ownerId\":11,\"handDetected\":true,\"controls\":{\"version\":2,\"controlMode\":\"hybrid\",\"handDetected\":true,\"calibrated\":true,\"leftHandX\":0.1,\"gesture\":3,\"gestureHold\":1.0,\"gestureArmed\":false,\"action\":\"DRINK\",\"actionSequence\":8,\"inputEpoch\":4}},\"right\":{\"ownerHeld\":true,\"ownerId\":1,\"handDetected\":true,\"controls\":{\"version\":2,\"controlMode\":\"hybrid\",\"handDetected\":true,\"calibrated\":true,\"rightHandX\":0.2,\"gesture\":0,\"gestureHold\":1.0,\"gestureArmed\":true,\"action\":\"\",\"actionSequence\":0,\"inputEpoch\":4}}}"
}
```

`sequence` 是外層 snapshot sequence。每個角色自己的 `actionSequence` 單調遞增；`inputEpoch` 隨該角色輸入生命週期重置而更新。`HybridEventGate` 以 epoch／action sequence 去重並拒絕舊事件。雙人角色界定由 left/right slot 和既有 owner 欄位承載，不增設 `playerId` 或事件物件。Mapping 與 gesture/action 欄位同次快照，維持同影格一致性。

角色歸屬須先由既有單／雙人路由固定，再更新各角色自己的 hybrid controls。重置演出、換手歸屬、清除追蹤 ownership 或重新建立 recognizer 時遞增該角色 `inputEpoch` 並清空 hold、armed 狀態及該角色各 action cooldown。不可把一個角色的 `actionSequence` 拿來驅動另一角色。

## Unity 動畫與映射交接

- 每種 action 定義骨架部位 mask；mask 外部位在動畫期間繼續套用最新 mapping。Mask 內部位由 Animator／action 動畫接管，同期間仍保存最新映射目標，但不將該目標寫到被接管骨架。
- Action 開始時以該角色為單位取得 mask 控制權。Animator 與 IK 不得同時寫同一部位；角色根部移動或非 mask mapping 不因 action 全域凍結。
- 動畫 busy 時仍消費並記錄新確認事件序號，但不啟動、不排隊、不延遲補播該事件。語音／字幕只隨實際開始的 action 發出，不隨被消費而略過的事件發出。
- Action 動畫完成時，mask 部位直接以 依動作約 0.48–0.60 秒平滑混合至**當下最新** mapping pose，不要求先做全身 Idle handoff，也不回到動畫開始前的舊座標。`HybridPuppetActor` 管理 mask 部位的動畫控制及 mapping blend；非 mask 部位持續映射。處理新 action／停播／追蹤遺失時仍須避免 Animator 與 IK 同幀競寫。
- 雙人每個 actor 各自持有 mask、busy 狀態、目標 pose 和交接時間；一人 action 不應暫停另一人的映射或動畫。
- Hybrid 台詞／語音在全場採雙人輪流播放；忙碌而略過的 action 不播放語音，也不占用輪替順序。

## 場景與建置

整合 WebGL 須包含六個演出場景：

| 人數 | Gesture | Mapping | Hybrid |
| --- | --- | --- | --- |
| 單人 | `SoloGesture` | `SoloMapping` | `SoloHybrid` |
| 雙人 | `DuetGesture` | `DuetMapping` | `DuetHybrid` |

場景命名與列入 Build Settings 是預期實作工作，不能只改文件或 UI 就視為支援。入口選擇仍只在 Unity 場景載入與設定確認後開始辨識。Hybrid 的校正控制應依角色顯示；單人可重新校正，雙人可獨立重新校正。

WebGL 整合建置入口是根目錄 `Build-IntegratedTheatre.bat`／`Build-IntegratedTheatre.ps1`，其 Unity method 為 `IntegratedTheatreBuilder.Build`，輸出預設 `hand-tracking/static/theatre/`。`Build-WebGL.bat` 保留舊獨立測試用途，不可拿它作整合版 Hybrid 的建置驗收。Unity 或 web source 修改後，須重建 WebGL 才會更新瀏覽器執行成品。

## 實作工作計畫

1. **前端選項及契約**：新增 Hybrid 選項與模式識別；顯示每角色校正進度、gesture／hold、armed／等待中狀態，保留攝影機生命週期及舊模式行為。
2. **辨識管線**：每影格僅執行一次手部辨識，從同一觀測更新連續 mapping、gesture hold 狀態和 action event；沿用既有 mapping 校正與雙人 ownership。
3. **事件生命週期**：加入逐角色 `actionSequence`、`inputEpoch`、0.2 秒 neutral arm/re-arm、0.5 秒 hold、per-action 3 秒冷卻與 session/reset/lost-hand 隔離；以 `HybridEventGate` 消費重複／busy 事件，不排隊。
4. **Unity 路由**：以 `HybridTheatreSession` 管理 SoloHybrid、DuetHybrid 場景及 envelope/state 解析；按角色更新 mapping，再交由各自 `HybridPuppetActor` 和 `HybridEventGate` 處理 action。
5. **部位動畫交接**：定義各 action mask，調整 IK／Animator ownership；非 mask mapping 持續，action 完成後 依動作約 0.48–0.60 秒直接混到最新 mask mapping pose；busy event 消費不排隊，雙人語音輪流。
6. **測試與建置**：擴增 session／recognizer 單元測試及瀏覽器模擬輸入驗證；license 可用後以整合入口重建六場景 WebGL，記錄本次 build ID、結果和報告。
7. **端到端驗收**：先以模擬輸入覆蓋所有場景和事件，再以實體攝影機檢查辨識與動作品質；把實際結果寫回 README、整合說明及對應報告。

## 驗收清單

Python 62 項、WebGL 建置、原四組模式回歸及兩組 Hybrid 模擬輸入瀏覽器驗證已通過。下列功能檢查依各自證據標記；完整 Editor 驗收另有搜尋索引例外，詳細界線與真人待驗項目見 [HYBRID_PROGRESS.md](HYBRID_PROGRESS.md)。

- [x] 原 Gesture 與 Mapping 的單／雙人行為回歸通過。
- [x] 單人 Hybrid 校正後先 neutral 0.2 秒才 armed；手勢 1–5 的 action 對應正確，hold 0.5 秒只產生單一事件。
- [x] 每次 action 後須 neutral 0.2 秒才能 re-arm；各 action 獨立 3 秒冷卻生效，冷卻結束不會補發或自行重複觸發。
- [x] 同一辨識 frame 的 mapping 持續更新與 gesture event 一致；校正未完成、手部遺失、reset、session 換代不觸發舊事件。
- [x] Duet Hybrid 兩角色各自校正、hold、per-action cooldown、`actionSequence`／`inputEpoch`；左右快速交替與舊封包重送均不串線、不重播。
- [x] action 動畫期間 mask 外 mapping 仍即時；mask 內無 Animator/IK 雙重寫入；busy 時新 action 被消費、不排隊；完成後直接 依動作約 0.48–0.60 秒混到最新映射 pose。
- [x] 單人與雙人語音依規則播放，雙人輪替；被 busy 消費的事件不播放語音或字幕。
- [x] 整合 WebGL 含六種場景，瀏覽器實際載入 SoloHybrid／DuetHybrid 並完成模擬輸入端到端測試。
- [ ] 實體攝影機和真人手勢檢查結果另行記錄，不以模擬鏡頭結果代替。

## 目前狀態與舊報告

本文件記錄 Hybrid 契約。工作副本已更新 WebGL，驗收證據與殘留問題見 [HYBRID_PROGRESS.md](HYBRID_PROGRESS.md)。原效能基準沒有重新量測，不能作為本次效能證據；真人鏡頭與動作品質尚未驗收。Editor 骨架功能旗標均通過，但整份 Editor 報告仍因 Unity 搜尋索引例外而為 false，不能與成功的 WebGL/瀏覽器報告混稱全部通過。
