# Unity 錯誤記錄檢查與改善（2026-11-07）

## 原因與修正

本機 Unity 6000.5.3f1 背景測試立即呼叫 `EnterPlaymode`，比編輯器延遲執行的搜尋索引啟動回呼更早進入播放。本次修正前確認 `UserSettings/Search.index` 不存在；原堆疊是 `SearchInit.IndexationOnStartup` → `GetDefaultSearchDatabase` → `EnumerateAll` 的 `ArgumentOutOfRangeException`。

對照 [Unity 官方 SearchDatabase 參考原始碼](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/QuickSearch/Editor/Indexing/SearchDatabase.cs) 與 [SearchInit](https://github.com/Unity-Technologies/UnityCsReference/blob/master/Modules/QuickSearch/Editor/SearchInit.cs)：首次建立索引在正在／準備播放時會提前返回，但後續仍會讀取索引清單的第一項。參考碼為查閱時的 master，並非本機版本完整原始碼；啟動順序的判斷另經本機缺少索引的冷啟動驗證。

新增共用 `EditorPlayModeStartup.EnterAfterInitialization`，先讓啟動回呼完成，再等待編譯與資產更新停止後進入播放。套用於整合舞台的三種驗證與四種動作／美術預覽入口；設有 120 秒逾時。不修改 Unity 內部方法，不停用搜尋，不刪除 Library，不過濾例外。

修正後第一次冷啟動由 Unity 正常建立 Search.index，日誌出現 `[Indexing] Starting Initial Indexing for Assets`，原索引越界未再出現。

## 驗收流程改善

- 交接驗收新增 `functionalChecksPassed`，只有功能斷言完成且 `engineDiagnostics` 為空才標為 `passed`。
- 動作檢查遇到診斷錯誤或未完成十組動作時，回傳失敗狀態與非零退出碼。
- 整體混合模式驗證保留完整錯誤訊息及堆疊，不再只有錯誤筆數。
- 注入診斷只由背景測試環境變數 `PUPPET_DIAGNOSTIC_PROBE=1` 啟用，使用獨立 `DiagnosticProbe.json`，不覆寫正常報告。執行後已清除該測試程序的變數。

## 實際結果

| 檢查 | 結果 |
| --- | --- |
| 整體單人／雙人 Hybrid 驗證 | 通過，errors 0，engineDiagnostics 0 |
| 交接測試：兩角色 × 五動作 × 三種 FPS × 自然結束／取消 | 60 組通過，engineDiagnostics 0 |
| 動作與時間曲線檢查 | 10 組完成，engineDiagnostics 0 |
| 故意注入一次 LogError | 功能檢查完成，但 passed=false、退出碼 1，正確攔截 |

修正前的兩份錯誤報告保留在 `before/`。正常報告與完整執行日誌位於本目錄。`DiagnosticProbe.log` 的 `EXPECTED_DIAGNOSTIC_PROBE` 是刻意製造的測試訊息，不是新的專案故障。

## 尚存訊息與範圍

Unity Licensing 原生日誌仍有 `Access token is unavailable; failed to update`，後續則有 `Successfully resolved entitlement details`，本次四次背景執行皆能使用現有授權。這不是已修復的帳號問題，也沒有將它算成遊戲執行期零錯誤；若未來阻擋啟動，再由 Unity Hub 重新登入處理。部分舊工具仍有 Unity API 棄用警告，本輪未擴大改動。

僅修改 Editor 腳本與驗收記錄，沒有改動畫、模型、辨識、正式場景或網頁播放器，不需要重建 WebGL。GitHub／Docker 未發布；專案版本設定保留原先的 6000.5.9f1，實際驗證使用本機已安裝的 6000.5.3f1。
