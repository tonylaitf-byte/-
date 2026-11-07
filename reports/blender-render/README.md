# Blender 庭院試渲染

## 展示姿勢版

新增 `blender-display-pose.png` 與 `courtyard-display-pose.blend`：俠客持劍戒備、另一手放低，琴師左手扶琴頸、右手撫弦，不再兩人平舉雙臂。Unity 的 `BlenderSceneSnapshot.CaptureDisplayPose` 先檢查既有骨架／動作，使用 ALERT 與 LANDSCAPE（慢速撥琴）動作取 1.5 秒的靜態姿勢，俠客非動作手臂搭配較低映射輸入。

`display-rig-inspection.txt` 記錄骨架路徑與動作；`Tools/RenderDisplayPose.py` 將這份新姿勢替換進已確認的微亮高低石板 Blender 場景，只替換 34 個角色及其道具網格。`DisplayPose.json` 確認鏡頭、非角色幾何及 -0.30 曝光維持，原人物材質沿用（兼容 Unity 材質名的 Instance 尾綴）。已檢視完整渲染，未見人物與桌面明顯穿插，但不是精密持物接觸／碰撞驗收。

這是展示圖的靜態姿勢，不新增或修改正式動畫、控制參數或待機規則，也沒有儲存改寫正式 Unity 場景或重建 WebGL。原平舉版本保留，姿勢效果待使用者確認。

## 石板暗夜版小幅提亮

依使用者要求，從高低石板版另存 `blender-uneven-stones-brighter.png` 與 `courtyard-uneven-stones-brighter.blend`。只提高曝光 0.35 級（-0.65 至 -0.30），模型、鏡頭、材質與各燈光設定不變，保留原暗版供比較。參數見 `StonesBrightness.json`，重現腳本為 `Tools/BrightenBlenderStones.py`；Unity 未修改，效果待使用者確認。

## 2026-11-07 真正的高低石板

使用者認為平整程序化接縫仍像磁磚，要求地板有高低起伏。新增 `blender-uneven-stones.png` 及 `courtyard-uneven-stones.blend`，重現腳本為 `Tools/BuildUnevenCourtyard.py`，載入前一版 `courtyard-night-refined.blend` 後執行。

原平面地板保留但隱藏，改建為 567 塊有厚度的獨立石板，採不同寬度、錯縫、不規則切角、磨損倒角、略微傾斜及實際高度差。頂面相對原地面約 -3.9 至 +5.3 公分，庭院地基仍連續覆蓋縫隙。移除原材質假磁磚網格，保留不均勻潮濕和石材細紋。酒席、地席、人物腳底和近旁擺設區域維持原高度，避免靜態姿勢穿插。

`UnevenStones.json` 記錄數量、幾何高度範圍及非地板物件／鏡頭不變檢查。暗夜燈光沿用已確認的版本，沒有加亮全景；已檢視實際渲染。這是 Blender 靜態試作，未加入 Unity 或碰撞／行走系統，也不是物理可行走地形驗收。

## 2026-11-07 局部補光與石板改善

新增 `blender-night-refined.png` 及 `courtyard-night-refined.blend`，來源為已確認的暗夜版，舊版保留。重現腳本：`Tools/RefineBlenderNight.py`。只增加兩盞人物臉部及四盞手部的柔邊小範圍冷色補光，曝光、環境光、鏡頭及模型不變。石板沿用原接縫，加入不規則濕潤區域、乾濕粗糙度差異、潮濕處色澤加深和細微石材凹凸，減少整片打蠟的觀感。

`NightRefinement.json` 確認鏡頭與網格／物件變換未變。PNG 像素比較見 `NightRefinementPixels.json`：兩個臉部區域平均亮度約增加 15% 與 11%，後方建築區域幾乎不變，整張平均亮度約增加 3%。這是固定影像區域的 RGB 統計，不是物理照度測量；已人工檢視實際 1600×900 輸出。

本輪未加建築雕刻或改動霧的位置，也沒有改 Unity 正式版，效果待使用者確認。

## 暗夜方向

使用者偏好原本「月黑風高」的低亮度氣氛，不喜歡第一版整體過亮。另存 `blender-moonlit-night.png` 及 `courtyard-moonlit-night.blend`，保留亮版作比較；暗夜版僅調整月光、補光、環境光、曝光與霧密度，鏡頭、模型和桌高不變，待使用者確認。暖燈與冷色輪廓仍保留，後方古寺退入陰影。

重現工具為 `Tools/RenderBlenderNight.py`，需先載入本資料夾的 `courtyard-cycles.blend` 再執行。參數記錄於 `NightRender.json`。本輪沒有把暗夜燈光套回 Unity，也未修改正式網頁版。

2026-11-06。`blender-cycles.png` 為 Blender 5.1 Cycles 的實際 1600 × 900 渲染，96 samples、降噪、RTX 3080 OptiX，並非 AI 生成圖或 Unity 截圖後製。`Render.json` 記錄實際裝置與物件數。

## 場景來源

`BlenderSceneSnapshot.Capture` 在正式 DuetHybrid 場景的 Play Mode 初始化角色，保持中立姿勢後擷取可見網格、世界座標、UV、頂點法線、材質及正面鏡頭。約 691 個 renderer、76 個材質，包含兩個戲偶、月琴、目前 87.5 公分桌高與庭院配置。

匯出為靜態姿勢網格，沒有轉移可繼續操控的 Blender 骨架。FBX 匯入角色需在 BakeMesh 時補償 scale；已檢查兩個主要角色網格的垂直尺寸約 1.90 與 1.48 單位，避免琴師比例錯誤。Unity 自訂建築及石板 Shader 分別讀取 `_BaseColor`、`_StoneColor`，不能只讀一般 `_Color`。

## Blender 試作

`RenderBlenderCourtyard.py` 重建同一份場景幾何與鏡頭，將 Unity 材質近似為 Principled PBR，附帶九個可攜貼圖。另加離線月光、暖光、柔和面部補光及局部薄霧。原本 Unity 程序化石板接縫改以 Blender 程序化材質近似，部分表面增加微小凹凸及建築邊緣倒角；並非逐像素等價的 Shader 轉換。

`courtyard-cycles.blend` 已將貼圖打包，可獨立開啟檢查及再次渲染。大型 `scene-snapshot.json` 是可重新產生的中介資料，已在本資料夾 `.gitignore` 排除，不適合直接推送 GitHub。

## 限制

這是靜態材質與燈光試作，未重新雕刻戲偶、增加屋瓦幾何或動畫，也未將新 Blender 燈光套回 Unity。現有低面數造型、袖口／頭飾細節仍受來源模型限制。Cycles 進階反射與體積效果不代表網頁版能以相同成本即時運作。

本輪沒有儲存改寫正式 Unity 場景或重建 WebGL，僅新增匯出與試渲染工具；專案 Unity 版本設定維持原版本。未推送 GitHub。
