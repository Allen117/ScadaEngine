# docs/plans/ — 個人實作計畫工作區

此目錄用於存放「一個任務 = 一份 plan.md」的工作流程，主要解決 Claude Code 長對話
context 被壓縮、早期決策流失的問題。

## 為什麼要這樣做

把計畫存成檔案 → 新對話注入，相當於把最關鍵的決策放進乾淨的 context，
避開「對話變長 → 自動壓縮 → 細節流失」的問題。

## Git 政策

- **個別 plan.md**：不 commit（已由 `.gitignore` 排除）
- **README.md 與 `_template.md`**：commit（讓其他人知道這個工作流的存在，可選用）
- **`_archive/` 內容**：不 commit（完成/廢棄的 plan 僅留本地）
- **若某份 plan 有通用設計價值**：手動搬到 `docs/design/` 或整併進對應的
  `docs/功能說明書_*.md` 再 commit

## 命名規則

```
YYYY-MM-DD-{kebab-case-任務名}.md
```

範例：
- `2026-04-22-logicflow-refactor.md`
- `2026-04-25-alarm-severity-color.md`
- `2026-05-01-history-data-backup.md`

## 工作流程

### 1. 開新任務

```
建立 docs/plans/2026-04-22-xxx.md，依 _template.md 結構規劃 YYY 功能
```

### 2. 繼續任務（可跨天、跨對話）

```
讀 docs/plans/2026-04-22-xxx.md 依計畫繼續實作，並更新勾選狀態
```

### 3. 完成歸檔

```
將 docs/plans/2026-04-22-xxx.md 狀態改為「已完成」，
補上相關 commit hash，搬到 _archive/ 目錄
```

## 何時新開 vs 覆蓋同一份

| 情境 | 做法 |
|------|------|
| 原計畫對，細節微調 | 覆蓋同一份（或底部加「修訂」區塊） |
| 原計畫完成，做下個功能 | **新開** md |
| 原計畫方向錯誤要重來 | **新開** md，舊的移 `_archive/` |
| 原計畫太大拆階段 | 新開 `xxx-phase1.md`、`xxx-phase2.md` |

## 與其他機制的分工

| 儲存位置 | 放什麼 |
|---------|--------|
| **plan.md**（本目錄） | 這次任務的目標、步驟、決策、驗收條件 |
| **CLAUDE.md**（專案根） | 架構規則、命名、build/run 指令（長期不變） |
| **auto memory**（Claude 個人記憶） | 踩過的雷、個人偏好、專案長期 context |
| **docs/功能說明書_*.md** | 功能正式規格（交付產物） |
