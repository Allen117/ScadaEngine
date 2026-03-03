# SCADA Engine Service 自動恢復設定腳本
# 此腳本設定服務失敗時的自動重啟策略

param(
    [Parameter(Mandatory=$false)]
    [string]$szServiceName = "ScadaEngineService"
)

function Write-Log {
    param([string]$szMessage, [string]$szLevel = "INFO")
    $dtTimestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$dtTimestamp] [$szLevel] $szMessage" -ForegroundColor $(
        switch($szLevel) {
            "ERROR" { "Red" }
            "WARN" { "Yellow" }
            "SUCCESS" { "Green" }
            default { "Cyan" }
        }
    )
}

function Test-AdminPrivileges {
    $currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    return $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Set-ServiceRecoveryOptions {
    if (-not (Test-AdminPrivileges)) {
        Write-Log "需要管理員權限才能設定服務恢復選項" "ERROR"
        return $false
    }

    try {
        # 檢查服務是否存在
        $service = Get-Service -Name $szServiceName -ErrorAction SilentlyContinue
        if (-not $service) {
            Write-Log "服務 '$szServiceName' 不存在" "ERROR"
            return $false
        }

        Write-Log "正在設定 '$szServiceName' 的自動恢復選項..."

        # 設定服務失敗恢復策略
        # 第一次失敗：等待 30 秒後重啟
        # 第二次失敗：等待 60 秒後重啟  
        # 後續失敗：等待 120 秒後重啟
        # 重置失敗計數器：每 24 小時 (86400 秒)
        $scFailureResult = sc.exe failure $szServiceName reset= 86400 actions= restart/30000/restart/60000/restart/120000

        if ($LASTEXITCODE -eq 0) {
            Write-Log "服務恢復選項設定成功" "SUCCESS"
            
            # 設定重啟訊息
            sc.exe failureflag $szServiceName 1 | Out-Null
            
            # 設定恢復程式（可選）
            # sc.exe failure $szServiceName reboot= "SCADA Engine Service 發生嚴重錯誤，系統將重新啟動"
            
            Write-Log "恢復策略詳細設定："
            Write-Log "  - 失敗計數重置時間：24 小時"
            Write-Log "  - 第 1 次失敗：等待 30 秒後自動重啟"
            Write-Log "  - 第 2 次失敗：等待 60 秒後自動重啟"
            Write-Log "  - 後續失敗：等待 120 秒後自動重啟"
            
            return $true
        } else {
            Write-Log "設定服務恢復選項失敗: $scFailureResult" "ERROR"
            return $false
        }
        
    } catch {
        Write-Log "設定過程中發生錯誤: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Set-ServiceStartupType {
    param([string]$szStartupType = "auto")
    
    if (-not (Test-AdminPrivileges)) {
        Write-Log "需要管理員權限才能設定服務啟動類型" "ERROR"
        return $false
    }

    try {
        Write-Log "設定服務啟動類型為 '$szStartupType'..."
        
        $scConfigResult = sc.exe config $szServiceName start= $szStartupType
        
        if ($LASTEXITCODE -eq 0) {
            Write-Log "服務啟動類型設定成功" "SUCCESS"
            return $true
        } else {
            Write-Log "設定服務啟動類型失敗: $scConfigResult" "ERROR"
            return $false
        }
        
    } catch {
        Write-Log "設定過程中發生錯誤: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Set-ServiceDependencies {
    try {
        Write-Log "設定服務依賴項..."
        
        # 設定依賴於基本網路服務
        $scConfigResult = sc.exe config $szServiceName depend= "Tcpip"
        
        if ($LASTEXITCODE -eq 0) {
            Write-Log "服務依賴項設定成功" "SUCCESS"
            Write-Log "  - 依賴服務：TCP/IP 網路服務"
            return $true
        } else {
            Write-Log "設定服務依賴項失敗: $scConfigResult" "ERROR"
            return $false
        }
        
    } catch {
        Write-Log "設定過程中發生錯誤: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Set-ServiceSecurity {
    try {
        Write-Log "設定服務安全性選項..."
        
        # 設定服務在 LocalSystem 帳戶下運行（預設）
        # 如果需要特定帳戶，可以修改此處
        $scConfigResult = sc.exe config $szServiceName obj= "LocalSystem"
        
        if ($LASTEXITCODE -eq 0) {
            Write-Log "服務安全性設定成功" "SUCCESS"
            Write-Log "  - 執行帳戶：LocalSystem"
            return $true
        } else {
            Write-Log "設定服務安全性失敗: $scConfigResult" "ERROR"
            return $false
        }
        
    } catch {
        Write-Log "設定過程中發生錯誤: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

function Show-ServiceConfiguration {
    try {
        Write-Log "查詢服務完整配置..."
        Write-Log "==============================================="
        
        # 查詢服務基本配置
        $configOutput = sc.exe qc $szServiceName
        $configOutput | ForEach-Object {
            if ($_ -match "^\s*(\w+)\s*:\s*(.+)$") {
                $key = $matches[1].Trim()
                $value = $matches[2].Trim()
                Write-Log "$key : $value"
            }
        }
        
        Write-Log "==============================================="
        
        # 查詢失敗恢復配置
        Write-Log "失敗恢復配置："
        $failureOutput = sc.exe qfailure $szServiceName
        $failureOutput | ForEach-Object {
            if ($_ -match "^\s*(\w+.*?)\s*:\s*(.+)$") {
                $key = $matches[1].Trim()
                $value = $matches[2].Trim()
                Write-Log "  $key : $value"
            }
        }
        
        return $true
        
    } catch {
        Write-Log "查詢服務配置時發生錯誤: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

# 主程式邏輯
Write-Log "==============================================="
Write-Log "SCADA Engine Service 恢復設定工具"
Write-Log "==============================================="

Write-Log "開始設定服務恢復與監控選項..."

# 設定自動恢復選項
$recoveryResult = Set-ServiceRecoveryOptions
if (-not $recoveryResult) {
    Write-Log "恢復選項設定失敗" "ERROR"
    exit 1
}

# 設定自動啟動
$startupResult = Set-ServiceStartupType -szStartupType "auto"
if (-not $startupResult) {
    Write-Log "啟動類型設定失敗" "WARN"
}

# 設定服務依賴項
$dependencyResult = Set-ServiceDependencies
if (-not $dependencyResult) {
    Write-Log "依賴項設定失敗" "WARN"
}

# 設定安全性
$securityResult = Set-ServiceSecurity
if (-not $securityResult) {
    Write-Log "安全性設定失敗" "WARN"
}

Write-Log ""
Write-Log "==============================================="
Write-Log "服務恢復設定完成！"
Write-Log "==============================================="

# 顯示最終配置
Show-ServiceConfiguration

Write-Log ""
Write-Log "建議的後續動作："
Write-Log "1. 使用 'MonitorService.ps1' 監控服務狀態"
Write-Log "2. 定期檢查日誌檔案"
Write-Log "3. 設定定時重啟任務（可選）"
Write-Log "4. 監控服務效能與資源使用"