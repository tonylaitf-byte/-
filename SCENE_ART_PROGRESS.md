# 庭院與戲偶美術進度

更新日期：2026-11-02。以下是獨立預覽，不是正式網頁已替換的畫面。

## Unity 預覽入口

以 Unity 開啟 `unity/UnityPuppetTest`，選擇下列場景並按 Play：

| 場景 | 用途 |
| --- | --- |
| `Assets/IntegratedTheatre/Scenes/CourtyardLayoutPreview.unity` | 建築位置、加寬正殿及矮桌／席墊占位 |
| `Assets/IntegratedTheatre/Scenes/CourtyardBanquetPreview.unity` | Blender 酒席、杯盤、編織席與周邊擺設 |
| `Assets/IntegratedTheatre/Scenes/HeroRefinementPreview.unity` | 酒席場景加左側持劍戲偶材質精修 |

專案指定 Unity 6000.5.9f1；本次背景建置與截圖使用本機 6000.5.3f1。
預覽在沒有鏡頭輸入時可查看靜態場景，正式網站仍使用原有 WebGL。

## 組員可編輯的檔案

- Blender 原始檔：`unity/UnityPuppetTest/ArtSources/CourtyardBanquet/courtyard_banquet_v1.blend`。
- 場景參考概念圖：同目錄的 `References/approved-banquet-concept.png`，屬 AI 配置參考，不是實際遊戲截圖。
- 酒席模型、材質、貼圖：`unity/UnityPuppetTest/Assets/Environment/CourtyardBanquet/`。
- 酒席重建腳本：`unity/UnityPuppetTest/Tools/BuildCourtyardBanquet.py`。
- 戲偶精修材質：`unity/UnityPuppetTest/Assets/Environment/HeroRefinement/`。
- 戲偶預覽 Shader：`unity/UnityPuppetTest/Assets/Shaders/RefinedPuppetSurface.shader`。

Blender 原始檔保留邏輯物件分組；FBX 匯出整理了靜態細節，保留各杯、壺、盤的獨立網格。
改動杯盤或酒席模型後需重新匯出；Unity 材質與燈光仍應在 Unity 內確認，不能只看 Blender 渲染。

## 實際畫面與驗證

- 庭院配置：`reports/courtyard-layout/layout-front.png`。
- 酒席全景與近照：`reports/courtyard-banquet/layout-front.png`、`banquet-detail.png`。
- 戲偶材質對照：`reports/hero-refinement/before-hero.png`、`after-hero.png`。
- 同目錄也有頭部近照與舞台全景，所有 before/after 配對維持相同鏡頭、燈光與姿勢。
- 酒席比例、材質、物件數檢查見 `reports/courtyard-banquet/Integration.json`。
- 戲偶網格、骨架、姿勢及 240 幀模擬操作檢查見 `reports/hero-refinement/Validation.json`。

各資料夾 README 說明細節及限制。既有 Unity Editor Search 索引錯誤保留於報告，不能把局部檢查通過說成整個專案全綠。

## 未完成項目

這輪沒有修補戲偶原模型的臉型、髮束、冠飾輪廓或不規則分色接縫，沒有重新綁定權重。
酒席沒有碰撞／拿取互動，也沒有跪坐、飲酒、倒酒的新動畫。整體燈光與材質尚待進一步調整。
未重建正式 WebGL，未重新完成真人手感、全模式或 Docker 驗收。

本次版本上傳至 [PR #1](https://github.com/tonylaitf-byte/-/pull/1) 的獨立分支供組員查看；沒有直接合併 main。
