# 劍師舞劍重新設計

新版沿用原劍師造型及骨架，重製 `Assets/Animations/Attack.anim`。原動畫主要帶動右臂，但武器 `Mesh1.0` 實際附掛在 `LeftHand`；新版以左手主刀、右手護身。

| 時間 | 動作 |
|---|---|
| 0.00–0.28 秒 | 外側提刀起勢，軀幹蓄力，右手護身 |
| 0.28–0.48 秒 | 高位定勢，刀及袖口移到臉的外側 |
| 0.48–0.92 秒 | 由高向低斜劈，肩、肘、腕與上身一起帶動 |
| 0.92–1.38 秒 | 順勢繞回，準備反向揮斬 |
| 1.38–1.82 秒 | 反向橫斬，展開清楚的第二條刀路 |
| 1.82–2.58 秒 | 減速回刀、側身定勢 |
| 2.58–3.00 秒 | 平順回到原本待機姿態 |

全段 3 秒，60 FPS，13 根上身／手臂骨骼。根節點與腿部沒有位移曲線，原有左右平移仍可操控。持刀手腕的扭轉分攤至前臂，武器握持位置在揮斬時對齊掌部；右手酒壺在舞劍時隱藏，動作結束恢復。

單人播放從起勢完整開始。雙人版若台詞較長，延長 0.36 秒處的準備姿勢，兩次揮斬仍以原速度完成。

## 修改檔案

- `Assets/Animations/Attack.anim`：新動作，保留原 GUID，既有引用繼續生效。
- `Assets/SwordDance/Editor/SwordDanceAuthor.cs`：可重建的動作設計與 IK 曲線生成器。
- `SwordDancePreview.cs`、`SwordDanceValidation.cs`：逐格預覽與單人／雙人 Play Mode 檢查。
- `Assets/SwordDance/Scripts/SwordDanceTiming.cs`：雙人台詞較長時的準備姿勢停留。
- `Assets/Scripts/Puppet/PuppetController.cs`：舞劍不再跳過開頭。
- `Assets/MoonlitDuetGesture/Scripts/DuetGestureActor.cs`：舞劍保留斬擊速度。
- `Assets/Editor/PuppetProjectBootstrap.cs`：未來重建基礎動畫時沿用新舞劍。

網頁 WebGL 同步重建；既有錄音、字幕、角色模型及其他動作沿用原設定。

## 驗證與預覽

`Validation.json` 記錄實際單人／雙人場景播放、持刀手、腳底高度、道具恢復、較長台詞時的準備姿勢停留、揮斬、收勢連續性及獨立平移。

`CurveContinuity.json` 逐格檢查 13 根骨骼的旋轉差。前臂扭轉使用連續角度，避免跨過 180 度時突然翻轉；本版前臂最大每格轉動約 8.92 度，手腕約 7.04 度（60 FPS）。

`BrowserValidation.json` 記錄重建 WebGL 中的單人及雙人實際播放；測試使用合成控制資料，不會使用實體攝影機。

`SwordDance.gif` 和 `Frames/` 為 Unity 逐格渲染，預覽補光用於看清肢體；網頁舞台維持原場景燈光與材質。

Unity 選單「掌中智戲 → 劍師 → 重建舞劍動畫」可重新生成素材。網頁端更新後，使用 Ctrl+F5 重新載入舞台即可查看。
