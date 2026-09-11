---
name: mock-render
description: 用模擬（假）數字離線渲染本專案 Web 任一頁面並截圖，不登入站台、不碰 DB（線上主機唯讀安全）。觸發詞：「帶數字渲染 XX 頁」「模擬截圖」「demo 畫面」「假資料渲染」。適用：簡報/展示圖、版面確認。不適用：驗證真實資料或後端邏輯（那要走真站台）。
---

# 頁面模擬渲染截圖（通用）

## 原理（為什麼不走真站台）

登入無後門帳號（SEC-08 已移除 admin/admin）、本機是線上生產主機 → 不登入、不碰 DB。
改用**真實前端資產**離線組頁：View/partial 的 HTML + `wwwroot/` 的真實 CSS/JS + Chart.js，
在 `<head>` 攔截 `fetch` 餵假 JSON、覆寫 `Date` 固定時鐘，頁面 JS 照常執行 → Playwright 以 `file://` 開啟截圖。
畫面 = 真版面 + 指定數字，且換機器可用（`{{WWWROOT}}` 佔位符由 render script 依 repo 位置代入）。

## 結構

```
assets/shell-ems.html      ← EMS 模式外殼（淡綠導覽列 + ems.css + 時鐘/fetch-mock 骨架）
assets/shell-scada.html    ← SCADA 模式外殼（深藍導覽列，無 ems.css）
recipes/<page>.html        ← 每頁一份完成品（外殼 + 該頁 HTML + MOCK + 頁面 JS），做過就留著重用
scripts/render_page.py     ← 代入 {{WWWROOT}} → Playwright 截圖；不帶 --html 會列出現有 recipes
```

## 流程

1. **先查 `recipes/` 有沒有現成的**（有 → 複製到 scratchpad 改 MOCK 數字即可，跳到步驟 4）
2. 沒有 → 做新 recipe：
   - 判斷頁面歸屬（EMS 綠 / SCADA 藍，看 `PermissionService.EmsRoutes` 或路由），複製對應 shell 到 scratchpad
   - 讀該頁三樣東西：`Features/<X>/Views/*.cshtml`（HTML，Localizer 字串查 `Resources/*.resx` 代入 zh-TW）、
     `wwwroot/js/<x>.js`（吃哪些 API、值怎麼格式化）、`Features/<X>/Controllers/<X>Controller.cs`（API 回傳形狀）
   - HTML 貼進 shell 的 PAGE_CONTENT、JS `<script>` 掛在 PAGE_SCRIPTS（順序照該頁 `@section Scripts`）、
     頁面 css `<link>` 加進 head、MOCK 物件照 Controller 回傳形狀填假資料
3. 執行：`python .claude/skills/mock-render/scripts/render_page.py --html <harness> --out <png>`
4. 用 Read 檢視 png 確認（數字、版面、圖表高度）再交付
5. 新 recipe 驗證 OK 後存回 `recipes/<page>.html`，並在下方登錄表加一列

## 已知眉角（踩過的坑）

- **Chart.js 動畫必關**（shell 已含 `Chart.defaults.animation = false`，勿移除），否則截到半高柱
- **假數字要自洽**：占比合計 = 總表 = 長條加總、子項差異加總 = 總表差異 —— 使用者會驗算
- **時鐘**：shell 的「模擬時鐘」區塊改 `new RealDate(...)` 一行即可；若頁面 JS 依「現在」切資料
  （如需量曲線只畫到現在），mock 資料的終點要跟著改
- 截圖 `OSError: Invalid argument` → 舊 png 被預覽程式鎖住，換輸出檔名
- Razor precompiled 與此法無關 —— 這裡不跑 Razor，`.cshtml` 只是拿 HTML 骨架的來源
- 頁面若用 `window.i18n.t(...)`（i18n 範圍頁）：shell 沒載 `i18n.js`，最簡做法是在 PAGE_SCRIPTS 最前面
  塞 `window.i18n = { t: function (k, a) { return ({...字典...})[k] || k; } }` 只填該頁用到的 key
- ~~SignalR / MQTT 即時頁要另外 stub~~ 已查證：ScadaPage 全走 fetch 輪詢（`/Designer/Load` + `/api/realtime/latest` 1 秒 + `/api/scadapage/accumulation`、`/api/scadapage/circuit-metric` 30 秒 + `/Realtime/ActiveAlarms` 3 秒），fetch-mock 即可，無 SignalR
- **CSS transition 在 headless 凍結**：`document.timeline.currentTime` 停在 0，transition 永遠卡在起始值（症狀：警報面板 collapse 的 `max-height` 280→0 收不起來，inline style 蓋了也沒用，因為 computed 回報的是動畫中的值）。shell 已無解法，**recipe 的 `<style>` 要加** `*, *::before, *::after { transition: none !important; animation: none !important; }`
- **`szBgDataUrl` 餵 SVG data URL**：`renderScadaCanvas` 用不帶引號的 `url(...)` 塞 CSS，`encodeURIComponent` 不編碼 `( ) '`，SVG 內含 `rgba(...)`/`url(#id)` 會截斷 CSS → encode 後要再 `.replace(/\(/g,'%28').replace(/\)/g,'%29').replace(/'/g,'%27')`

## Recipes 登錄

| recipe | 頁面 | 內容 | 備註 |
|---|---|---|---|
| `recipes/ems-index.html` | /EMS 首頁（EMS 綠） | 電力五卡：主要電表資訊 / 今日即時需量 / 用電長條 / 子迴路圓餅 / 去年同期比較 | API 形狀對照表在檔頭註解；水/氣/電費卡未含，要加時讀 `EmsCardRegistry.cs` + `EmsController.cs` 對應 action，照現有模式補 |
| `recipes/scadapage-power-overview.html` | /ScadaPage 即時監控（SCADA 藍） | 「電力迴路總攬」圖面：SVG 單線圖背景（父台電電表 + 子生產大樓A/B、辦公大樓）+ realtimeValue kW + cmetric day_kwh，含頁面樹側欄與警報面板（收合） | 圖面全在 `/Designer/Load` mock 內組（SVG 以 JS 函數產生、widget state JSON.stringify）；改數字動 `/api/realtime/latest` 與 `/api/scadapage/circuit-metric` 兩個 mock，注意子迴路加總 = 父迴路 |
| `recipes/logicflow-hvac-optimize.html` | /LogicFlow 流程圖控制（SCADA 藍） | 空調最佳化邏輯：3 溫度 + 2 用電讀取點位 → 空調最佳化演算法（虛構，雙輸出 freq1/freq2）→ 排程 A 接點×2（ON）→ 冰水泵 46.5 Hz / 冷卻水 42 Hz 寫入點位（自動） | 用真實 logicflow/*.js 跑求值：mock `/LogicFlow/api/{tree,diagram/2,algorithms,algo-eval/hvac_optimize,timer-state/2}` + `/api/realtime/by-sids`、`/api/control/manual-values`、`/api/schedules`；window.i18n 為內嵌 stub 字典；尾端 autoSelect script 自動展開並選取邏輯；輸出節點 x 勿超過 ~1000（畫布 overflow hidden 會裁掉更右邊）；排程 ON/OFF 由 mock 時鐘（週三 15:42）對排程 08:00-18:00 平日決定 |
