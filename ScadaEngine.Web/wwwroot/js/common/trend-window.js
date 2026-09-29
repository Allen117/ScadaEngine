/**
 * common/trend-window.js — 即時曲線（trendChart widget）時間窗 / 點數計算單一真相
 *
 * Designer 屬性面板（點數提示）與 ScadaPage 執行期（實際取樣間隔）必須算出同一個數字，
 * 否則面板說「360 點」實際畫 720 點。故抽成共用模組，兩頁都載入。
 *
 * 硬上限（MAX_POINTS）是 UI 誤設的護欄：使用者若設「24 小時 × 1 秒」= 86400 點，
 * 一頁擺 8 張圖就會凍住瀏覽器。超過上限時自動放大畫點間隔（不截斷時間窗），
 * 屬性面板同步顯示被放大後的實際間隔。
 */
(function () {
    'use strict';

    var MAX_POINTS = 3600;

    // 實際生效的畫點間隔（秒）：使用者設定值，但不得使點數超過 MAX_POINTS
    function effectiveSampleSec(nWindowSec, nSampleSec) {
        var nWin = Math.max(60, parseInt(nWindowSec, 10) || 1800);
        var nSmp = Math.max(1, parseInt(nSampleSec, 10) || 5);
        var nMinSmp = Math.ceil(nWin / MAX_POINTS);
        return Math.max(nSmp, nMinSmp);
    }

    // 時間窗內的點數（以實際生效間隔計）
    function pointCount(nWindowSec, nSampleSec) {
        var nWin = Math.max(60, parseInt(nWindowSec, 10) || 1800);
        return Math.max(2, Math.floor(nWin / effectiveSampleSec(nWin, nSampleSec)));
    }

    // 使用者設定的間隔是否被上限放大（屬性面板據此顯示提示）
    function isCapped(nWindowSec, nSampleSec) {
        return effectiveSampleSec(nWindowSec, nSampleSec) > Math.max(1, parseInt(nSampleSec, 10) || 5);
    }

    window.TrendWindow = {
        MAX_POINTS: MAX_POINTS,
        effectiveSampleSec: effectiveSampleSec,
        pointCount: pointCount,
        isCapped: isCapped
    };
})();
