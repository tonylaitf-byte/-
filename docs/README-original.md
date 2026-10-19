# 手部辨識 Unity 戲偶控制系統

本目錄是依照 `PROJECT_SPEC.md` 整理中的正式專案。

原始資料來自：

```text
C:\Users\User\Desktop\PuppetTeamDocker_2026-08-19\PuppetTeamDocker
```

整理過程只讀取來源並複製必要檔案，沒有修改來源內容。

## 本階段範圍

- 第一個舞台：`PuppetBackgroundTest` 的三合院環境。
- 第一個角色：`SwordCharacter/sword_01_testblend_colored.fbx`。
- 動作：Idle、Drink、Attack、Bow（鞠躬）。
- Action ID：`DRINK`、`ATTACK`、`BOW`。
- 保留既有 Camera、MediaPipe、Docker、Unity IK Mapping。
- 語音、字幕、BGM、第二角色與第二模型暫置。

目前刻意未納入：

- `Meshy_AI_Moonlit_Imperial_Warr_biped` 第二模型。
- FFmpeg 錄影套件與執行檔。
- `_Recovery` 場景、舊影片及 Unity 快取。

## 主要目錄

```text
hand-tracking/                 Flask + MediaPipe 手部辨識
verification/                  既有驗證腳本與報告
unity/UnityPuppetTest/         Unity 6000.5.9f1 專案
  Assets/Scenes/
    PuppetBackgroundTest.unity 原始複製場景，保留作參考
    Stage01.unity              已移除受控角色的三合院舞台
    PuppetActionTest.unity     單角色動作測試場景來源
  Assets/Scripts/
    Action/                    Action ID、Duration、Cooldown
    Core/                      GameManager 與 Game State
    Puppet/                    PuppetManager、PuppetController
    UI/                        Editor Play Mode 動作測試面板
```

## 第一次建立 Unity 動作資產

Unity Editor 必須先在 Unity Hub 完成授權啟用。完成後可使用其中一種方式：

1. 用 Unity Hub 開啟 `unity/UnityPuppetTest`。
2. 等待所有 C# 編譯完成。
3. 執行選單 `Puppet > Build Phase 1 And 2 Baseline`。

或在 Unity 授權有效時執行根目錄的：

```text
Build-UnityBaseline.bat
```

產生器會建立：

- `Assets/Animations/Idle.anim`
- `Assets/Animations/Drink.anim`
- `Assets/Animations/Attack.anim`
- `Assets/Animations/Bow.anim`
- `Assets/Animations/Puppet01.controller`
- `Assets/Puppets/Puppet01.prefab`
- 更新 `PuppetActionTest.unity`
- 更新 `Stage01.unity`
- 將兩個正式場景加入 Build Settings

## 測試動作

1. 開啟 `Assets/Scenes/PuppetActionTest.unity`。
2. 按 Unity Play。
3. 使用左上角 `DRINK`、`ATTACK`、`BOW / 鞠躬` 按鈕。
4. 確認特殊動作期間 Mapping IK 暫停，動作完成後恢復。
5. 同一動作在 Cooldown 期間不應重複觸發。

本階段不需要先啟動 Docker，也不播放語音或音樂。

## Phase 3：Unity WebGL

目前已提供 WebGL 建置入口與瀏覽器舞台頁面：

1. 執行根目錄的 `Build-WebGL.bat`。
2. WebGL 產物會輸出到 `hand-tracking/static/unity/`。
3. 執行 `Start-Puppet.bat` 啟動本地服務。
4. 開啟 `http://127.0.0.1:5000/control.html`。

控制頁目前只負責載入 Unity 舞台。Camera + MediaPipe 骨架顯示屬於下一個 Phase，
Web → Unity 的手勢動作指令則會在後續 UnityBridge Phase 接入。

## 手部追蹤控制模式

需要測試既有 Mapping 時：

1. 啟動 Docker Desktop。
2. 執行 `Start-Puppet.bat`。
3. 開啟 `http://127.0.0.1:5000/` 並選擇模式。
4. 手勢模式：進入 `/gesture` 後開啟鏡頭，穩定維持手勢 0.5 秒。
   - 僅食指：`DRINK`
   - 食指＋中指：`ATTACK`
   - 拇指＋食指＋中指：`BOW`
5. 映射模式：進入 `/mapping`，開啟鏡頭並完成校正。
6. 在 Unity 開啟 `PuppetActionTest.unity` 後按 Play。

手勢模式使用不可打斷的 Unity 動作佇列。下一個動作會等待目前動畫及角色語音都播放完成後才開始；最多保留四個待播動作。

目前 HTTP polling 僅作為 Editor/Mapping 原型通道。正式 WebGL 仍應依規格新增 `UnityBridge` JSON 通道。
