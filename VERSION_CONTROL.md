# 共編與版本控制

## v1.1 發布

- 發布分支：`release/v1.1`；版本標籤：`v1.1`。
- [GitHub Release](https://github.com/tonylaitf-byte/-/releases/tag/v1.1) 收錄完整原始碼與預先建置的 WebGL；啟動不需要 Unity Editor。
- 來源為下列組員分支及本機整合，再加入快速校正、新手勢、混合待機及單人鏡頭修正；詳細內容見 [RELEASE_NOTES_v1.1.md](RELEASE_NOTES_v1.1.md)。
- 發布分支推送後透過 PR 審查合併 main；保留組員分支，不改寫歷史。

## 前期整合紀錄

2026-11-06 本機整合：來源為 `origin/codex/hybrid-stability-20261025` 的 `d36fd98`，整合工作保存在本機 `integration/20261106`。本次不會替組員的 GitHub 分支改寫歷史；遠端推送與 PR 合併須以實際 Git 紀錄為準。詳細來源見 [INTEGRATION_20261106.md](INTEGRATION_20261106.md)。

## 前期共編版本

- 儲存庫：https://github.com/tonylaitf-byte/-
- 基準：`0497210`。
- 工作分支：`codex/hybrid-stability-20261025`。
- 目前狀態：已推送獨立分支，並建立 [Pull Request #1](https://github.com/tonylaitf-byte/-/pull/1) 供組員審查；尚未合併至 main。
- 程式／測試：`6d8d629`（feat: add hybrid puppetry and tracking stability safeguards）。
- WebGL 成品：`af94ff7`（build: update WebGL player for stable hybrid handoff）。
- 2026-11-02 新增庭院酒席與持劍戲偶材質預覽，推送至同一 PR 分支；版本編號以 Git 紀錄為準。這批預覽尚未替換上述正式 WebGL。
- 修改內容與已知限制：見 [CHANGELOG.md](CHANGELOG.md)。
- 2026-10-25 已執行 `git fetch origin --prune`；當時本機與遠端 main 同為基準版本。
- 本機提交不代表 GitHub 已更新。推送及合併前應再次確認遠端狀態。

依序保存程式／測試、對應 WebGL 成品、修改紀錄。這是將先前累積工作整理成版本，
不是補造當時每個功能的歷史提交。提交時間使用實際執行時間。

## 組員協作方式

1. 每項工作各開分支，避免多人直接修改 main。
2. 開始前先 fetch 並查看差異；工作區仍有修改時，先提交或明確保存，不直接覆蓋。
3. Unity 場景及 `.meta` 必須一起納入版本。避免兩人同時修改同一場景、模型或 WebGL 二進位檔。
4. Unity 程式修改由一位負責人整合後重建 WebGL；一般網頁或 Python 修改不需要重建 Unity。
5. 完成功能後透過 Pull Request 說明變更、測試及限制，再合併至 main；不要強制推送共用分支。

檢查本機狀態：

```sh
git status --short
git log --oneline --decorate -6
git diff origin/main...HEAD --stat
```

本分支已完成首次推送。之後在此分支新增本機提交時，可使用：

```sh
git push -u origin codex/hybrid-stability-20261025
```

## 回退

先用 `git show <commit>` 看要撤回的內容。已分享的版本使用 `git revert <commit>` 建立反向提交，
不改寫組員已取得的歷史。若回退 Unity 程式，應一併還原相容的 WebGL 成品或重新建置，
不能只還原 C# 而繼續提供較新的網頁成品。不要用 `reset --hard` 清理尚未保存的工作。

`reports/` 是測試證據，不是保證任何電腦都能重現的成功宣告；實際使用的版本、Editor、
假鏡頭／真鏡頭與未通過項目須一起閱讀。個人 token、Email 密碼及 `.env` 不應加入版本庫。
