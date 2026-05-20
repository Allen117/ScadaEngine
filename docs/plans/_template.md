# {任務名稱}

**狀態**: 進行中 <!-- 進行中 | 已完成 | 廢棄 -->
**建立**: YYYY-MM-DD
**最後更新**: YYYY-MM-DD
**相關 commit**: <!-- 完成後回填 commit hash -->

---

## 目標 & 背景

<!-- 為什麼做這件事？要達成什麼？使用者情境是什麼？ -->

## 驗收條件

<!-- 具體、可驗證。完成時逐條勾選 -->

- [ ] 條件 1（例：登入後能從側欄進入 XXX 頁）
- [ ] 條件 2（例：數值顯示正確且每秒更新）
- [ ] 條件 3（例：斷線時顯示 Bad 品質）

## 檔案異動清單

<!-- 預計要改/新增哪些檔案。實際動工時勾選 -->

### 新增

- [ ] `ScadaEngine.Web/Features/XXX/Controllers/XxxController.cs`
- [ ] `ScadaEngine.Web/Features/XXX/Models/XxxViewModel.cs`
- [ ] `ScadaEngine.Web/Features/XXX/Views/Index.cshtml`
- [ ] `ScadaEngine.Web/wwwroot/css/xxx.css`
- [ ] `ScadaEngine.Web/wwwroot/js/xxx.js`

### 修改

- [ ] `ScadaEngine.Web/Program.cs` — 註冊新 Service
- [ ] `ScadaEngine.Web/Views/Shared/_Layout.cshtml` — 加入側欄連結

### 資料庫

- [ ] 新增資料表 `XXX`（欄位：...）
- [ ] 調整 `DatabaseSchema.json` 自動建表

## 關鍵設計決策

<!-- 為什麼選 A 不選 B？有什麼取捨？ -->

### 決策 1：{標題}
- **選擇**:
- **理由**:
- **放棄的選項**: 及放棄理由

## 實作步驟

<!-- 依序執行，有依賴就序列、無依賴可平行 -->

1. [ ] 步驟 1
2. [ ] 步驟 2
3. [ ] 步驟 3

## 已知風險 / 待釐清

<!-- 動工前不清楚、需使用者確認的事 -->

- ❓ 問題 1
- ⚠️ 風險 1

## 測試計畫

- [ ] 單元測試：
- [ ] 整合測試：
- [ ] 手動驗證（瀏覽器操作）：

## 文件同步

<!-- CLAUDE.md 規定：修改功能後須更新 docs/ 下對應說明書 -->

- [ ] 更新 `docs/功能說明書_XXX.md`
- [ ] 若引入新架構模式，更新 `CLAUDE.md`

---

## 進度日誌

<!-- 跨對話時的交接備忘，最新的寫在最上面 -->

### YYYY-MM-DD
-

## 完成後補充

<!-- 實作完成時回填，作為下次類似任務的參考 -->

### 實際做法 vs 原計畫差異
-

### 踩到的雷
-

### 對 memory / CLAUDE.md 的更新建議
-
