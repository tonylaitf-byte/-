# 混合模式進度與驗收

版本狀態更新（2026-10-25）：本機程式／測試已保存為 `6d8d629`，對應 WebGL 為 `af94ff7`，尚未推送 GitHub。以下保留早期階段紀錄；最新動畫交接規格以 `HYBRID_HANDOFF.md` 為準，完整修改與版本方式見 `CHANGELOG.md`、`VERSION_CONTROL.md`。

更新：2026-10-23。本次在獨立工作副本實作第三種 `hybrid` 模式，原 `gesture`、`mapping` 及原四個場景均保留。前端、後端、同影格資料傳輸、逐角色事件去重、骨架部位遮罩與動畫交接已實作；已產生包含入口及六個演出場景的 WebGL 成品。使用者改變模型配置後，未再呼叫 Astra；剩餘 Unity 修改由 Sol 接手，主代理整合測試。

後續已完成十個 Hybrid 動作的第一輪流暢度調整與新版 WebGL 重建。相同固定輸入下，峰值角速度下降 7.0%–52.6%，峰值角加速度下降 37.5%–83.6%；不是自然度評分。前後關鍵影格、測量界線與逐動作結果見 [MOTION_REVIEW.md](MOTION_REVIEW.md)。本輪沒有修改原手勢／映射模式、原動畫資產或辨識參數。

## 實際通過項目

| 範圍 | 結果 | 證據 |
| --- | --- | --- |
| Python 回歸與 Hybrid | 62 項通過：原 50 項及新 12 項 | `reports/HybridBackendValidation.json` |
| C# runtime/editor assembly | 編譯通過，保留既有警告 | `tests/check_unity_scripts.ps1` |
| WebGL 建置 | 0 errors，入口及六個演出場景 | `reports/HybridBuildValidation.json` |
| 原四組模式回歸 | 輸入、暫停、40 次重置、鏡頭重試及釋放通過 | `reports/BrowserValidation.json` |
| 單人、雙人 Hybrid | 映射不中斷、重複事件不重播、busy 不排隊、最新目標回復、暫停、重置 | `reports/HybridBrowserValidation.json` |
| 桌面／手機畫面 | 實際舞台可見、無橫向溢出，四張 canvas 截圖像素變異均大於 1500 | `reports/*hybrid*canvas.png` |
| 延遲預覽回歸 | 辨識刻意延遲 350ms，預覽約 20 FPS、最多一張辨識中影像、停止後無舊骨架 | `reports/CameraLatencyValidation.json` |
| Hybrid 動作比較 | 十個動作速度、加速度、收尾與手腕尖峰均改善，時間曲線檢查通過 | `reports/HybridMotionComparison.json` |

建置使用本機 Unity 6000.5.3f1；保留儲存庫原指定 6000.5.9f1，不宣稱本次已用 .9 重建。可透過 `Build-IntegratedTheatre.ps1 -UnityPath` 指定本機 Editor。

## 未通過與待驗

Unity 無圖形 Play Mode 在反射探針的原生渲染程序崩潰；改用背景圖形模式後，事件、單雙人、遮罩、未接管手臂映射、busy、暫停、epoch 交接及琴師有限退出檢查均為 true，但編輯器啟動期間另產生 `UnityEditor.Search.SearchDatabase.EnumerateAll` 的 `ArgumentOutOfRangeException`。因此 `HybridEditorValidation.json` 整體仍保留 `passed:false, errors:1`，不能稱為完整乾淨通過。這與已通過的實際 WebGL 瀏覽器測試分開記錄。

本次未開啟實體攝影機、未操作使用者螢幕。真人手勢辨識成功率、現場光線、動作自然度及實際聲音品質仍需實機驗收；模擬輸入和字幕狀態測試不等同這些結果。Docker 未啟動，本次用相同 Flask 程式的本機虛擬環境驗證，未宣稱重新完成 Docker 容器驗收。

## 開啟本次版本

本次測試服務為 `http://127.0.0.1:5086/`。在首頁選一人或兩人，再選「混合操控」。校正完成後，先維持非指令自然手勢 0.2 秒，再維持指定手勢 0.5 秒。動畫只接管指定部位，結束後 依動作約 0.48–0.60 秒交回最新映射；忙碌中的新動作會被略過，不補播。

本次修改只在 `PalmTheatreHybrid` 工作副本，尚未提交或推送 GitHub。測試服務停止後，可在該副本根目錄執行：

```powershell
$env:MPLCONFIGDIR = Join-Path (Get-Location) '.venv\mpl'
.\.venv\Scripts\python.exe -m flask --app hand-tracking/docker_app run --host 127.0.0.1 --port 5086 --no-reload
```

攝影機僅會在使用者進入舞台並允許瀏覽器後啟用。
