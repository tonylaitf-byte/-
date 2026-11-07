# 正式網頁美術整合

2026-11-06：將 `HeroRefinementPreview.unity` 中已確認的美術配置套入 SoloGesture、SoloMapping、SoloHybrid、DuetGesture、DuetMapping、DuetHybrid，保留獨立預覽作為來源。`IntegratedTheatreBuilder` 在產生混合場景後呼叫 `ProductionTheatreArt.Apply`，避免下次建置回退到舊美術。

包含庭院建築配置、月夜背景及其正面鏡頭、統一的預覽燈光、Blender 酒席及 0.875 單位桌高，以及持劍角色的 11 個材質槽（3 個 renderer）。單人角色整體朝向對齊新鏡頭；未改動骨骼局部姿勢、角色網格、綁定、操控參數或動畫事件規則。環境使用共用 prefab，重複套用會先移除原版本及 prefab 名稱對應的舊物件，避免重複鏡頭。

`Integration.json` 記錄六種場景皆符合桌高、只有一台 Camera、材質完整及角色網格／骨架綁定不變。`Build.json` 為最終 WebGL 建置結果，錯誤數 0。使用本機 Unity 6000.5.3f1；專案設定維持 6000.5.9f1。

`Browser.json` 由 `tests/production_art_validation.cjs` 產生，測試實際 WebGL 與獨立 5089 埠 Flask，僅使用模擬攝影機。檢查六場景載入、暫停／繼續、桌面／手機寬度截圖，以及映射／混合模式接受合成座標後角色平移。未驗收實體攝影機、完整五段動畫自然度、持劍穿模或酒席碰撞。

PNG 為實際瀏覽器 Unity canvas 截圖，並非 AI 圖片。單人保持舞台中央位置，桌子會遮住部分下半身；雙人維持桌子兩側。現有 UI 在手機尺寸仍以橫向舞台縮放呈現，未重新設計版面。

本機 5000 埠服務直接提供更新後靜態檔案，重新整理頁面（必要時 Ctrl+F5）即可載入；不需再開 Unity。Docker 映像未重建，GitHub 未提交／推送。新增家具仍是靜態美術，沒有碰撞或拿取互動。
