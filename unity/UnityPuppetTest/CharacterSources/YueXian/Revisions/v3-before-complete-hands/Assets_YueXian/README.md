# 月弦公子 · 月下琴劍

第二版直接以原俠客 `sword_01_testblend_colored.fbx` 的頭臉、衣褶、層疊袍身、刺繡浮雕和手部網格重新建造，保留相同的雕刻戲偶外觀與身體比例。移除大型武冠，改為半束墨黑長髮、月牙小冠、青藍與月白戲服、銀玉配件，配上獨立月琴。原俠客來源檔沒有被修改。

本次修訂將保留的雕刻臉面與顱部接合，修補額際、側面和後腦，髮殼具有內外層及封閉下緣。袖口收短並補上內緣；衣片與手掌分離，袖口權重改由前臂控制。手掌、指骨依原網格方向重新對齊，右手持琴位置也配合調整。

## 開啟方式

- `Scenes/MoonlitDuet.unity`：《月下琴劍》雙人展示，月弦公子與原俠客同台。
- `Scenes/YueXianPreview.unity`：月弦公子單人展示。
- 將 `Prefabs/YueXianGongZi.prefab` 拖入其他場景，即可使用完整持琴角色。
- `Prefabs/Yueqin.prefab`：獨立月琴道具；`Models/Yueqin.fbx` 是獨立模型來源。

Play 模式：滑鼠左鍵拖曳環視、滾輪縮放、R 重設視角、空白鍵切換撥弦示範、H 隱藏文字。

## 骨架與動畫

`Models/YueXian.fbx` 以原角色的張臂姿勢匯出，角色 Prefab 保存持琴姿勢。依照原角色關節位置重新配置 52 根骨頭，其中 51 根映射至 Unity Humanoid，含左右各五指、每指三節。原網格保留蒙皮，手指區域延伸分配至新指節。Root 位於腳下，長袍下保留完整雙腿與腳趾骨架。

主要階層為 Root → Pelvis → Spine → Chest → Neck → Head；Chest 下接雙肩與手臂，Pelvis 下接雙腿。衣服、頭髮和五官均已配置骨骼權重。長袍採簡化蒙皮，沒有布料模擬；大跨步或極端抬腿需另外調整裙擺。

`YueXianPerformance` 提供輕微呼吸及右手撥弦。將 Humanoid Animator Controller 指派至角色 Animator 後，示範腳本會讓動畫控制骨架。自訂演奏動畫時，請保留扶琴姿勢，或以左手 IK 對準月琴上的 `Yueqin_LeftGrip`。本次提供模型與動作示範，未加入演奏音訊、口型或手部辨識映射。

## 獨立月琴

月琴沒有併入角色 Mesh。角色中位置為：

`YueXian_Humanoid / YueXianRig / Root / Pelvis / Spine / Chest / Yueqin_Socket_Adjustable / Yueqin_Independent`

調整 `Yueqin_Socket_Adjustable` 可移動整把琴；琴上的 `Yueqin_LeftGrip` 與 `Yueqin_StrumPoint` 是左右手對位參考。`Yueqin_FaceForward`、`Yueqin_NeckTip` 是方向標記。材質與各琴框、琴面、四弦、琴軸、琴穗物件可單獨編輯。

## 來源與重建

主專案的 `CharacterSources/YueXian` 保存 Blender 原檔與生成工具，避免 Unity 自動匯入 `.blend` 時連同攝影機、燈光和預覽底座一起導入。

可編輯原檔是 `CharacterSources/YueXian/Source/YueXian_and_Yueqin.blend`。`Tools/build_yuexian.py` 及同資料夾三個輔助腳本共同重建角色，需保留它們。使用生成工具時可傳入 `--unity-project "專案根目錄"`；FBX 會輸出至指定 Unity 專案，Blender 原檔與美術預覽仍保存於工具所在來源資料夾。請用 Blender 的 `--factory-startup` 啟動批次生成，避免個人外掛干擾。

第一版琴師備份在 `CharacterSources/YueXian/Revisions/v1-before-reference-rebuild`，其中包含舊模型、Prefab、場景及 Blender 原檔。

本次頭部與袖口修正前的第二版保存於 `CharacterSources/YueXian/Revisions/v2-before-head-sleeve-fix`。

Unity 選單「掌中智戲 → 月下琴劍 → 建立月弦公子與雙角色場景」可重建本資料夾內的 Prefab 與兩個展示場景。重建會覆寫這些生成資產的手動修改；需要保留修改時請先複製 Prefab 或場景。原俠客模型與原場景不會被此指令覆寫。

`YueXianReports` 保存 Unity 匯入、Humanoid／持琴檢查、Play 模式測試結果和舞台預覽。

`Previews/HeadSleeveGeometryQA.json` 記錄頭部／髮殼封閉性及 Blender 持琴姿勢的表面交叉檢查。`YueXianReports/UnitySurfaceValidation.json` 另使用 Unity 實際蒙皮後的三角面，檢查預設持琴及內建撥弦動作的兩端位置。這些檢查針對本場景的持琴動作；若更換其他動畫，應重新調整持琴位置並檢查衣袖。

整合時加入 Unity 內建的 `imageconversion` 模組，供編輯器輸出預覽圖片；既有 `PuppetVisualEnhancer` 增加庭院物件檢查，使舊庭院燈光只作用於原庭院。修改前的兩個檔案保存在 `CharacterSources/YueXian/IntegrationBackup`。
