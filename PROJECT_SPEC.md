# 手部辨識 Unity 戲偶控制系統 — PROJECT SPEC

## 1. 專案目標

本專案是一套本地端操作的互動式戲偶控制系統。

使用者透過瀏覽器進入本地端網站，先設定演出內容，再進入 Unity WebGL 舞台控制頁面。

系統透過 Webcam 進行手部辨識，並提供兩種戲偶控制模式：

1. 手勢對應戲偶動作模式
2. 手部映射模式

系統需支援：

- 1 隻或 2 隻戲偶
- 手部辨識
- 手勢辨識
- 手部座標映射
- Unity WebGL 舞台
- 戲偶動畫
- 語音播放
- 字幕顯示
- 背景切換
- 背景音樂切換
- 雙人控制區域限制

---

# 2. 整體系統架構

系統分成四個主要部分：

```text
Browser Frontend
        │
        ├── Setup Page
        ├── Control Page
        ├── Webcam
        └── MediaPipe Hand Tracking
                │
                ↓
           HandManager
                │
        ┌───────┴────────┐
        ↓                ↓
Gesture Mode        Mapping Mode
        │                │
        └───────┬────────┘
                ↓
          UnityBridge
                ↓
          Unity WebGL
                ↓
          GameManager
                ↓
         PuppetManager
                ↓
    ┌───────────┼───────────┐
    ↓           ↓           ↓
Animation     Voice      Subtitle
```

---

# 3. 技術架構

建議技術：

## Frontend

- HTML
- CSS
- JavaScript

## Hand Tracking

- MediaPipe Hand Landmarker

## Unity

- Unity
- Unity WebGL
- C#

## Local Server

可使用：

- Python Flask

或：

- Node.js Express

初期建議以簡單本地 Server 為主。

---

# 4. 系統頁面

系統主要包含兩個頁面。

## 4.1 Setup Page

功能：

- 選擇戲偶數量
- 選擇操控模式
- 選擇舞台背景
- 選擇背景音樂

設定完成後進入 Control Page。

---

# 5. Setup Page 設定項目

## Puppet Count

```text
1 Puppet
2 Puppets
```

資料格式：

```javascript
puppetCount: 1
```

或：

```javascript
puppetCount: 2
```

---

## Control Mode

兩種模式：

```text
gesture
mapping
```

gesture：

手勢對應戲偶動作模式。

mapping：

手部映射模式。

---

## Background

例如：

```text
stage01
stage02
stage03
stage04
```

之後可以自由新增。

---

## Background Music

例如：

```text
bgm01
bgm02
bgm03
bgm04
```

---

# 6. Setup Config

使用者完成設定後，建立：

```javascript
const config = {
    puppetCount: 2,
    controlMode: "gesture",
    background: "stage01",
    bgm: "bgm01"
};
```

暫時使用：

```javascript
sessionStorage
```

保存。

例如：

```javascript
sessionStorage.setItem(
    "gameConfig",
    JSON.stringify(config)
);
```

之後跳轉：

```text
setup.html
↓
control.html
```

---

# 7. Control Page UI

畫面配置：

```text
┌────────────────────────────────────────────┐
│                                            │
│                                            │
│                Unity Stage                 │
│                                            │
│                                            │
│                                            │
│              Subtitle Area                 │
│                                            │
├────────────────────────────┬───────────────┤
│                            │ Camera        │
│                            │               │
│                            │ Hand Tracking │
│                            │               │
│                            │ [Start Camera]│
│                            ├───────────────┤
│                            │ Mode Info     │
│                            │ Gesture Info  │
│                            │ Mapping Info  │
└────────────────────────────┴───────────────┘
```

建議比例：

```text
Unity Stage：75%
Right Panel：25%
```

---

# 8. Camera 啟動機制

進入控制頁時，不立即啟動 Camera。

初始狀態：

```text
Camera OFF
```

顯示：

```text
[ 開啟鏡頭 ]
```

使用者點擊後：

```text
navigator.mediaDevices.getUserMedia()
```

取得 Webcam。

之後才啟動：

```text
MediaPipe Hand Tracking
```

流程：

```text
Control Page
↓
Unity Load
↓
Camera OFF
↓
User Click Start Camera
↓
getUserMedia
↓
MediaPipe Start
↓
Hand Tracking Start
```

---

# 9. Hand Tracking

使用 MediaPipe Hand Landmarker。

每隻手包含 21 個 Landmark。

需要取得：

```text
Handedness

Left / Right

Landmark X
Landmark Y
Landmark Z
```

主要會使用：

```text
Wrist
Index Tip
Middle Tip
Ring Tip
Pinky Tip
Thumb Tip
```

---

# 10. HandManager

所有 MediaPipe 結果統一交給：

```text
HandManager
```

HandManager 不直接控制 Unity。

它只負責整理：

```text
Left Hand
Right Hand
Gesture
Coordinate
Confidence
Tracking State
```

資料概念：

```javascript
handState = {
    left: {
        detected: true,
        x: 0.3,
        y: 0.5,
        z: -0.01,
        gesture: 1
    },

    right: {
        detected: true,
        x: 0.7,
        y: 0.4,
        z: -0.02,
        gesture: 5
    }
};
```

---

# 11. Control Mode Router

HandManager 後方需要一個模式判斷。

```text
HandManager
↓
ControlModeManager
```

根據：

```javascript
controlMode
```

選擇：

```text
GestureController
```

或：

```text
MappingController
```

---

# 12. Gesture Mode

Gesture Mode 的核心：

```text
Hand Landmark
↓
Gesture Classifier
↓
Gesture ID
↓
Action ID
↓
Unity
```

例如：

```text
Gesture 1
↓
DRINK

Gesture 2
↓
ATTACK

Gesture 3
↓
BOW

Gesture 4
↓
POINT

Gesture 5
↓
WAVE
```

---

# 13. Gesture Action Mapping

手勢不能直接綁 Animation。

必須經過 Action ID。

正確架構：

```text
Gesture
↓
Action
↓
ActionManager
```

例如：

```text
1 → DRINK
2 → ATTACK
3 → BOW
4 → POINT
5 → WAVE
```

---

# 14. Gesture Confirmation

不可單幀判斷手勢。

需要至少加入：

## Hold Time

例如：

```text
0.4 ~ 0.6 秒
```

手勢持續一段時間後才觸發。

流程：

```text
Gesture 1 Detected
↓
Hold 0.5 sec
↓
Confirmed
↓
DRINK
```

---

# 15. Gesture Cooldown

同一手勢不能每幀重複觸發。

例如：

```text
DRINK
Cooldown = 3 sec
```

流程：

```text
Gesture Trigger
↓
Action Start
↓
Cooldown
↓
Allow Trigger Again
```

---

# 16. Gesture Mode UI

右下角需顯示目前手勢對應動作。

例如：

```text
手勢對應

1 → 喝酒
2 → 攻擊
3 → 行禮
4 → 指向
5 → 招手
```

並可以顯示：

```text
Current Gesture: 1

Detected Action: DRINK
```

---

# 17. Mapping Mode

Mapping Mode 不以手勢觸發動作為主。

而是：

```text
Hand Coordinate
↓
Puppet Joint / Puppet Position
```

例如：

```text
左手高度
↓
左手臂角度

右手高度
↓
右手臂角度

手部左右
↓
戲偶位置
```

---

# 18. Coordinate Mapping

MediaPipe：

```text
X = 0 ~ 1
Y = 0 ~ 1
```

Unity 必須做 Range Mapping。

例如：

```text
MediaPipe Y

0.2 ~ 0.8
```

對應：

```text
Unity Arm Rotation

-60° ~ 60°
```

Unity 可使用：

```csharp
Mathf.Lerp(minAngle, maxAngle, normalizedValue);
```

---

# 19. Coordinate Smoothing

MediaPipe 原始 Landmark 不可直接控制戲偶。

需要 Smooth。

例如：

```text
Smooth =
OldValue × 0.8
+
NewValue × 0.2
```

或 Unity 使用：

```csharp
Mathf.Lerp(
    currentValue,
    targetValue,
    smoothing * Time.deltaTime
);
```

目的：

- 降低抖動
- 讓戲偶動作自然

---

# 20. Mapping Mode 語音事件

Mapping Mode 仍然可以觸發 Action。

例如：

拿酒壺的手抬到一定高度。

MediaPipe：

```text
Y 越小代表越高
```

例如：

```javascript
if (rightHandY < 0.30)
```

觸發：

```text
DRINK_TALK
```

再交給：

```text
ActionManager
```

處理。

---

# 21. Mapping Trigger Cooldown

高度事件也必須有 Cooldown。

例如：

```text
手抬高
↓
DRINK_TALK
↓
Cooldown 5 sec
```

手一直保持舉高時不能一直播放語音。

---

# 22. Action System

這是整個系統的核心。

任何特殊事件都轉換成：

```text
Action ID
```

例如：

```text
DRINK
ATTACK
BOW
POINT
WAVE
DRINK_TALK
```

不管事件來自：

```text
Gesture Mode
```

或：

```text
Mapping Mode
```

最後都交給：

```text
ActionManager
```

---

# 23. Action Definition

每個 Action 可包含：

```text
Action ID
Animation
Voice
Subtitle
Duration
Cooldown
```

例如：

```text
Action ID:
DRINK

Animation:
Drink

Voice:
drink01.wav

Subtitle:
好友相逢，喝一杯吧！

Duration:
4 sec

Cooldown:
5 sec
```

---

# 24. ActionManager

Unity：

```text
ActionManager.cs
```

主要負責：

```text
收到 Action ID
↓
找到 Action Definition
↓
播放 Animation
↓
播放 Voice
↓
顯示 Subtitle
```

---

# 25. PuppetManager

所有戲偶由：

```text
PuppetManager
```

統一管理。

不要讓 JavaScript 直接控制 GameObject。

架構：

```text
UnityBridge
↓
GameManager
↓
PuppetManager
↓
PuppetController
```

---

# 26. PuppetController

每隻戲偶擁有：

```text
PuppetController.cs
```

負責：

- Position
- Rotation
- Arm Movement
- Animation
- Action
- Mapping

---

# 27. Puppet Prefab 架構

建議：

```text
Puppet

├── Root
├── Body
├── Head
├── LeftArm
├── LeftHand
├── RightArm
├── RightHand
├── Animator
└── AudioSource
```

如果 FBX 已經 Rig：

以實際 Bone Transform 為主。

---

# 28. Single Puppet Mode

設定：

```text
puppetCount = 1
```

控制方式：

```text
Left Hand
→ Puppet Left Hand

Right Hand
→ Puppet Right Hand
```

戲偶可以在整個允許舞台區域移動。

仍需設定：

```text
StageMinX
StageMaxX
```

避免跑出畫面。

---

# 29. Two Puppet Mode

設定：

```text
puppetCount = 2
```

Camera 畫面分成：

```text
Left Control Zone
Right Control Zone
```

原始規則：

```text
Camera 左半邊
→ 左邊 Puppet

Camera 右半邊
→ 右邊 Puppet
```

---

# 30. Two Puppet Dead Zone

不要直接使用：

```text
X = 0.5
```

當切換點。

改成：

```text
LEFT

0.00 ~ 0.45

DEAD ZONE

0.45 ~ 0.55

RIGHT

0.55 ~ 1.00
```

目的：

避免手在中央附近時控制權一直跳動。

---

# 31. Puppet Ownership

雙人模式需建立 Control Ownership。

例如：

```text
Left Zone Hand
→ Puppet 1

Right Zone Hand
→ Puppet 2
```

手被指定給 Puppet 後不要每幀重新判定。

只有在：

```text
Hand Lost > 1 sec
```

時才解除 Ownership。

---

# 32. Hand Tracking Lost

如果手短暫消失：

不要立即解除控制。

設定：

```text
Lost Grace Period
```

例如：

```text
0.5 ~ 1 sec
```

超過才視為手真正離開。

---

# 33. Stage Movement Limit

兩隻 Puppet 不可越過舞台中央。

例如 Unity X 軸：

```text
-10 -------- 0 -------- +10
```

左 Puppet：

```text
-8 ~ -1
```

右 Puppet：

```text
+1 ~ +8
```

中央：

```text
-1 ~ +1
```

為禁止區。

---

# 34. Unity Clamp

左 Puppet：

```csharp
leftX = Mathf.Clamp(
    leftX,
    leftStageMin,
    -centerLimit
);
```

右 Puppet：

```csharp
rightX = Mathf.Clamp(
    rightX,
    centerLimit,
    rightStageMax
);
```

---

# 35. Camera Coordinate → Puppet Position

雙 Puppet Mapping：

Camera Left Zone：

```text
X 0.0 ~ 0.45
```

Mapping：

```text
Unity X

-8 ~ -1
```

Camera Right Zone：

```text
X 0.55 ~ 1
```

Mapping：

```text
Unity X

1 ~ 8
```

---

# 36. UnityBridge

Browser 與 Unity WebGL 間統一使用：

```text
UnityBridge
```

JavaScript：

```javascript
unityInstance.SendMessage(
    "UnityBridge",
    "ReceiveCommand",
    JSON.stringify(data)
);
```

Unity：

```csharp
public void ReceiveCommand(string json)
{
}
```

---

# 37. Web → Unity Command Types

所有 Command 使用 JSON。

主要 Type：

```text
CONFIG
HAND
ACTION
```

---

# 38. CONFIG Example

```json
{
    "type": "CONFIG",
    "puppetCount": 2,
    "mode": "gesture",
    "background": "stage01",
    "music": "bgm01"
}
```

---

# 39. ACTION Example

```json
{
    "type": "ACTION",
    "puppet": 1,
    "action": "DRINK"
}
```

---

# 40. HAND Example

```json
{
    "type": "HAND",
    "puppet": 1,
    "leftHand": {
        "x": 0.31,
        "y": 0.52,
        "z": -0.01
    },
    "rightHand": {
        "x": 0.48,
        "y": 0.30,
        "z": -0.02
    }
}
```

---

# 41. Subtitle System

Unity 建立：

```text
SubtitleManager.cs
```

功能：

- 顯示字幕
- 自動消失
- Fade In
- Fade Out

建議：

```text
Show Duration：依語音長度或 2~5 sec
```

字幕顯示在 Unity 舞台下方。

---

# 42. Voice System

Unity 建立：

```text
VoiceManager.cs
```

負責播放角色語音。

每個 Action 可綁：

```text
AudioClip
```

未來可支援：

```text
同一 Action 多句語音隨機播放
```

---

# 43. Background System

建立：

```text
BackgroundManager.cs
```

例如：

```text
BG_Stage01
BG_Stage02
BG_Stage03
```

收到 CONFIG：

```text
background = stage02
```

就切換對應背景。

---

# 44. BGM System

建立：

```text
BGMManager.cs
```

負責：

- 背景音樂播放
- Loop
- 切換曲目
- 音量控制

---

# 45. GameManager

Unity 系統最高層管理：

```text
GameManager.cs
```

負責：

```text
Config
Game State
PuppetManager
ActionManager
Stage
```

---

# 46. Game State

建議使用：

```text
LOADING
READY
CAMERA_OFF
TRACKING
PERFORMING
PAUSED
```

Camera 尚未啟動：

```text
CAMERA_OFF
```

Camera + MediaPipe 正常：

```text
TRACKING
```

執行特殊動畫：

```text
PERFORMING
```

---

# 47. Web Folder Structure

建議：

```text
web/

├── index.html
├── control.html
│
├── css/
│   ├── setup.css
│   └── control.css
│
├── js/
│   ├── config.js
│   ├── camera.js
│   ├── mediapipe.js
│   ├── handManager.js
│   ├── gestureClassifier.js
│   ├── gestureController.js
│   ├── mappingController.js
│   ├── puppetAssignment.js
│   └── unityBridge.js
│
└── unity/
    └── WebGL Build
```

---

# 48. Unity Folder Structure

```text
Assets/

├── Scripts/
│
│   ├── Core/
│   │   ├── GameManager.cs
│   │   └── UnityBridge.cs
│
│   ├── Puppet/
│   │   ├── PuppetManager.cs
│   │   ├── PuppetController.cs
│   │   └── PuppetMovement.cs
│
│   ├── Action/
│   │   ├── ActionManager.cs
│   │   └── PuppetActionData.cs
│
│   ├── Audio/
│   │   ├── VoiceManager.cs
│   │   └── BGMManager.cs
│
│   ├── UI/
│   │   └── SubtitleManager.cs
│
│   └── Stage/
│       └── BackgroundManager.cs
│
├── Animations/
├── Audio/
├── Puppets/
├── Backgrounds/
└── Scenes/
```

---

# 49. 開發順序

不要同時開發全部功能。

按照以下順序。

## Phase 1

Unity 單一 Puppet。

完成：

```text
Idle
Drink
Attack
Wave
```

先確認 Animator 正常。

---

## Phase 2

Action System。

先用 Unity Button 測試：

```text
Button
↓
DRINK
↓
Animation
Voice
Subtitle
```

---

## Phase 3

Unity WebGL。

Build：

```text
Unity
↓
WebGL
```

嵌入：

```text
control.html
```

---

## Phase 4

Camera + MediaPipe。

先只做：

```text
Webcam
↓
Hand Landmark
```

畫出手部骨架。

暫時不接 Unity。

---

## Phase 5

Gesture Recognition。

做到：

```text
Gesture 1
Gesture 2
Gesture 3
Gesture 4
Gesture 5
```

畫面顯示：

```text
Current Gesture
```

---

## Phase 6

Web → Unity。

先測試最小流程：

```text
Gesture 1
↓
ACTION DRINK
↓
UnityBridge
↓
ActionManager
↓
Drink Animation
```

---

## Phase 7

整合：

```text
Gesture
+
Animation
+
Voice
+
Subtitle
```

---

## Phase 8

Mapping Mode。

先做：

```text
Hand Y
↓
Arm Rotation
```

再做：

```text
Hand X
↓
Puppet Position
```

最後加入：

```text
Coordinate Trigger
↓
Voice / Subtitle
```

---

## Phase 9

Two Puppet Mode。

加入：

```text
Puppet 2
```

再加入：

```text
Left / Right Control Zone
Dead Zone
Ownership
Stage Limit
```

---

## Phase 10

Setup Page。

最後再加入：

```text
Puppet Count
Control Mode
Background
BGM
```

以及完整 UI 美化。

---

# 50. MVP

第一個必須優先完成的 MVP：

```text
Webcam
↓
MediaPipe
↓
Gesture 1
↓
JavaScript
↓
UnityBridge
↓
Action DRINK
↓
Puppet Drink Animation
↓
Voice
↓
Subtitle
```

只要這條流程成功，代表：

```text
Browser
+
Hand Tracking
+
Unity WebGL
+
Animation Event
```

整條核心技術已經串通。

之後其他功能都建立在這個基礎上。

---

# 51. 開發原則

Codex 在後續修改專案時，請遵守以下原則。

1. 不要讓 MediaPipe 直接操作 Unity GameObject。
2. Browser 與 Unity 之間統一經過 UnityBridge。
3. 手勢不能直接綁 Animation，必須先轉換成 Action ID。
4. Gesture Mode 和 Mapping Mode 共用 ActionManager。
5. 每隻 Puppet 使用 PuppetController。
6. 多隻 Puppet 統一由 PuppetManager 管理。
7. 所有語音與字幕事件由 Action 系統管理。
8. 所有 Web → Unity 訊息統一使用 JSON。
9. Gesture 必須包含 Hold Time 與 Cooldown。
10. Mapping 座標必須包含 Smoothing。
11. 雙 Puppet 模式必須具有 Dead Zone。
12. 雙 Puppet 模式不可讓角色越過中央限制。
13. 新功能盡量以新增模組方式實作，不任意破壞既有架構。
14. 開發時優先確保 MVP 可運作，再增加 UI 與額外功能。
15. 若實作內容與本文件衝突，以本 `PROJECT_SPEC.md` 為主要架構依據。

---

# 52. Codex 開始工作前指令

開始修改程式前：

1. 先完整閱讀 `PROJECT_SPEC.md`
2. 檢查目前專案資料夾結構
3. 判斷目前完成到哪個 Phase
4. 不要直接重構整個專案
5. 優先延續既有可運作功能
6. 新增功能需符合本規格中的模組架構
7. 修改完成後說明：
   - 修改哪些檔案
   - 新增哪些檔案
   - 每個檔案用途
   - 如何測試
   - 下一個建議開發 Phase

---

# 53. Codex 建議首次 Prompt

請先完整閱讀專案根目錄的 `PROJECT_SPEC.md`，並把它視為本專案目前的主要系統規格。

接著掃描目前專案所有主要資料夾與程式碼，告訴我：

1. 目前專案已經完成到 `PROJECT_SPEC.md` 的哪個 Phase。
2. 哪些既有功能可以保留。
3. 哪些架構與 `PROJECT_SPEC.md` 不一致。
4. 目前最優先應該完成的下一個功能。
5. 請先不要大幅修改程式碼。

分析完成後，再依照目前實際程式狀態提出下一步實作方案。
