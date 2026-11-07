# 庭院與戲偶美術進度

更新日期：2026-11-06。已將確認過的庭院、87.5 公分高酒席、戲偶材質與燈光整合至正式六種演出場景並重建本機 WebGL。以下獨立預覽保留作為美術編輯來源；最新建置及網頁驗證見 `reports/production-art/`。尚未推送 GitHub 或重建 Docker 映像。

## Unity 預覽入口

以 Unity 開啟 `unity/UnityPuppetTest`，選擇下列場景並按 Play：

| 場景 | 用途 |
| --- | --- |
| `Assets/IntegratedTheatre/Scenes/CourtyardLayoutPreview.unity` | 建築位置、加寬正殿及矮桌／席墊占位 |
| `Assets/IntegratedTheatre/Scenes/CourtyardBanquetPreview.unity` | Blender 酒席、杯盤、編織席與周邊擺設 |
| `Assets/IntegratedTheatre/Scenes/HeroRefinementPreview.unity` | 酒席場景加左側持劍戲偶材質精修 |

專案指定 Unity 6000.5.9f1；本次背景建置與截圖使用本機 6000.5.3f1。
預覽在沒有鏡頭輸入時可查看靜態場景；本機正式網站現已使用新美術 WebGL。

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

使用者新增的下一輪需求另記於 [TODO.md](TODO.md)。桌面首輪抬高 6 公分後，再依要求提高至首輪高度的兩倍（離地 87.5 公分），杯盤同步上移、桌腳底部不變；最新 Unity 尺寸驗證與實際畫面見 `reports/table-height-double/`，首輪紀錄保留於 `reports/table-height/`，本輪尚未推送。初次辨識等待與電腦攝影機入口仍待處理。

這輪沒有修補戲偶原模型的臉型、髮束、冠飾輪廓或不規則分色接縫，沒有重新綁定權重。
酒席沒有碰撞／拿取互動，也沒有跪坐、飲酒、倒酒的新動畫。整體燈光與材質尚待進一步調整。
已重建正式 WebGL；真人手感、完整動作演出及 Docker 驗收仍待後續測試。

本次版本上傳至 [PR #1](https://github.com/tonylaitf-byte/-/pull/1) 的獨立分支供組員查看；沒有直接合併 main。
