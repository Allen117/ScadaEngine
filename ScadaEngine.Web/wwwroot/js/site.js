// SCADA 系統 Web 應用程式 - 全域 JavaScript

/**
 * 登入頁面相關功能
 */
class LoginPageHandler {
    constructor() {
        this.init();
    }

    init() {
        // 當 DOM 內容載入完成時執行
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', () => this.setupLoginPage());
        } else {
            this.setupLoginPage();
        }
    }

    setupLoginPage() {
        const loginForm = document.getElementById('frmLogin');
        if (!loginForm) return; // 非登入頁面，直接返回

        this.setupAutoFocus();
        this.setupFormSubmission(loginForm);
    }

    setupAutoFocus() {
        // 自動聚焦使用者名稱輸入框
        const userNameInput = document.getElementById('tbUserName');
        if (userNameInput) {
            userNameInput.focus();
        }
    }

    setupFormSubmission(form) {
        form.addEventListener('submit', (e) => {
            this.handleFormSubmission();
        });
    }

    handleFormSubmission() {
        // 顯示載入中狀態（UI 回饋，不做驗證）
        const btnLogin = document.getElementById('btnLogin');
        if (btnLogin) {
            btnLogin.innerHTML = '<i class="fas fa-spinner fa-spin"></i> 登入中..';
            btnLogin.disabled = true;
        }

        // 所有驗證邏輯交給 Controller 和 Model 處理
    }
}

/**
 * 主頁面相關功能
 */
class MainPageHandler {
    constructor() {
        // 為未來主頁面功能預留
    }
}

// 初始化應用程式
document.addEventListener('DOMContentLoaded', function() {
    // 初始化登入頁面處理器
    new LoginPageHandler();
    
    // 未來可以新增其他頁面處理器
    // new MainPageHandler();
});
