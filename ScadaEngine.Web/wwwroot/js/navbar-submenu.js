// 導覽列多層下拉子選單行為：
// 1. 桌機靠 CSS :hover 展開，此處只負責偵測右側空間不足時翻向左邊
// 2. 觸控 / 小螢幕沒有 hover，改用點擊切換
(function () {
    'use strict';

    var MOBILE_BREAKPOINT = 992;

    function isMobile() {
        return window.innerWidth < MOBILE_BREAKPOINT;
    }

    function closeAll(exceptItem) {
        document.querySelectorAll('.dropdown-submenu.show').forEach(function (el) {
            if (el !== exceptItem) {
                el.classList.remove('show');
            }
        });
    }

    // 右側空間不足時，把子選單翻到左邊展開（只影響展開位置，箭頭一律朝下不跟著轉）
    function adjustPosition(item) {
        var menu = item.querySelector(':scope > .dropdown-menu');
        if (!menu || isMobile()) { return; }

        menu.classList.remove('submenu-flip');
        var rect = menu.getBoundingClientRect();
        if (rect.right > window.innerWidth - 8) {
            menu.classList.add('submenu-flip');
        }
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('.dropdown-submenu').forEach(function (item) {
            var toggle = item.querySelector(':scope > .dropdown-toggle');
            if (!toggle) { return; }

            item.addEventListener('mouseenter', function () {
                if (isMobile()) { return; }
                // 滑到別的父項時，先收掉上一個被「點」開的（.show 不會自己消失）
                closeAll(item);
                adjustPosition(item);
            });

            // 桌機：點開後滑鼠移開就收。
            // 沒有這段的話，click 掛上的 .show 會一路留著，滑到同一張選單的其他項
            // （或另一個子選單群組）時，舊的子選單仍展開並疊在新的上面。
            // 子選單是 item 的子元素，滑進子選單不會觸發 mouseleave，不影響操作。
            item.addEventListener('mouseleave', function () {
                if (isMobile()) { return; }
                item.classList.remove('show');
            });

            toggle.addEventListener('click', function (e) {
                // 父項不導頁，也不讓 Bootstrap 關掉外層下拉
                e.preventDefault();
                e.stopPropagation();

                var isOpen = item.classList.contains('show');
                closeAll(item);
                item.classList.toggle('show', !isOpen);
                if (!isOpen) {
                    adjustPosition(item);
                }
            });
        });

        // 外層下拉關閉時，一併收掉子選單
        document.querySelectorAll('.nav-item.dropdown').forEach(function (dd) {
            dd.addEventListener('hide.bs.dropdown', function () {
                closeAll(null);
            });
        });
    });
})();
