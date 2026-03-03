# SCADA Engine Service Deployment Script
# Encoding: UTF-8

param([string]$Action = "status")

# Configure output encoding
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

# Get current directory
$ScriptPath = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectPath = Split-Path -Parent $ScriptPath

# 目標部署路徑
$TargetPath = "C:\SCADA\Engine\App"

# Service configuration
$ServiceName = "ScadaEngineService"
$ServiceDisplayName = "SCADA Engine Service"
$ServiceDescription = "Industrial SCADA data collection engine"

# Function: Check service status
function Get-ServiceStatus {
    try {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($service) {
            Write-Host "Service Found: $ServiceDisplayName"
            Write-Host "Status: $($service.Status)"
            Write-Host "Start Type: $($service.StartType)"
            return $true
        } else {
            Write-Host "Service not found: $ServiceName"
            return $false
        }
    } catch {
        Write-Host "Failed to get service status: $($_.Exception.Message)"
        return $false
    }
}

# Function: Install service
function Install-Service {
    Write-Host "Starting service installation..."
    try {
        # Build and publish the project with all dependencies included
        Write-Host "Building project with self-contained deployment..."
        Set-Location $ProjectPath
        
        # 清理舊的發布檔案
        if (Test-Path "$ProjectPath\bin\Release\Publish") {
            Remove-Item "$ProjectPath\bin\Release\Publish" -Recurse -Force
        }
        
        # 使用 self-contained 發布避免 .NET Runtime 依賴問題
        Write-Host "Publishing self-contained application (includes .NET 8 runtime)..."
        dotnet publish -c Release --self-contained true --runtime win-x64 -o "$ProjectPath\bin\Release\Publish"
        
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Self-contained publish failed. Trying framework-dependent..." -ForegroundColor Yellow
            dotnet clean
            dotnet restore --force
            dotnet publish -c Release --self-contained false -o "$ProjectPath\bin\Release\Publish"
            if ($LASTEXITCODE -ne 0) {
                Write-Host "Build failed. Exiting."
                return $false
            }
            Write-Host "WARNING: Framework-dependent build. .NET 8 Runtime must be installed on target machine." -ForegroundColor Yellow
        } else {
            Write-Host "Self-contained build successful. No .NET Runtime installation required." -ForegroundColor Green
        }

        # Create target directories and clean up locked files
        Write-Host "Creating target directory: $TargetPath"
        if (-not (Test-Path $TargetPath)) { 
            New-Item -ItemType Directory -Path $TargetPath -Force | Out-Null 
        } else {
            # 清理可能被鎖定的檔案
            Write-Host "Cleaning existing installation..."
            try {
                # 先嘗試殺死可能的殘留進程
                $processes = Get-Process -Name "ScadaEngine*" -ErrorAction SilentlyContinue
                if ($processes) {
                    Write-Host "Terminating existing ScadaEngine processes..."
                    $processes | ForEach-Object { 
                        try { $_.Kill(); Start-Sleep 1 } catch { }
                    }
                }
                
                # 等待檔案解鎖
                Start-Sleep -Seconds 3
                
                # 嘗試刪除舊檔案
                Get-ChildItem -Path $TargetPath -File -Recurse -ErrorAction SilentlyContinue | ForEach-Object {
                    try {
                        Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue
                    } catch {
                        Write-Host "Warning: Could not remove $($_.Name) (may be in use)" -ForegroundColor Yellow
                    }
                }
            } catch {
                Write-Host "Warning during cleanup: $($_.Exception.Message)" -ForegroundColor Yellow
            }
        }

        $SubDirs = @("Setting", "Modbus", "Mqtt", "Log", "DatabaseSchema")
        foreach ($dir in $SubDirs) {
            $dirPath = Join-Path $TargetPath $dir
            if (-not (Test-Path $dirPath)) { New-Item -ItemType Directory -Path $dirPath -Force | Out-Null }
        }

        # Copy exe/dll
        Write-Host "Copying application files..."
        $publishPath = "$ProjectPath\bin\Release\Publish"
        Get-ChildItem -Path $publishPath -File | ForEach-Object { Copy-Item -Path $_.FullName -Destination $TargetPath -Force }

        # Copy configuration directories
        $dirsToCopy = @("Setting","Modbus","Mqtt","DatabaseSchema")
        foreach ($d in $dirsToCopy) {
            $src = Join-Path $ProjectPath $d
            $dst = Join-Path $TargetPath $d
            if (Test-Path $src) {
                Get-ChildItem -Path $src -File | ForEach-Object {
                    Copy-Item -Path $_.FullName -Destination $dst -Force
                    Write-Host "Copied: $d\$($_.Name)"
                }
            }
        }

        $ExePath = Join-Path $TargetPath "ScadaEngine.Engine.exe"
        if (-not (Test-Path $ExePath)) { Write-Host "Executable not found: $ExePath"; return $false }

        # Install service safely
        Write-Host "Installing Windows Service..."
        sc.exe create $ServiceName `
            binPath= "`"$ExePath`"" `
            DisplayName= "`"$ServiceDisplayName`"" `
            start= auto | Out-Null

        # Set description & recovery
        sc.exe description $ServiceName $ServiceDescription | Out-Null
        sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

        Write-Host "Service installed successfully to: $TargetPath"
        Write-Host "Configuration files structure:"
        Write-Host '  - Setting\dbSetting.json'
        Write-Host '  - Modbus\*.json'
        Write-Host '  - Mqtt\MqttSetting.json'
        Write-Host '  - DatabaseSchema\DatabaseSchema.json'
        return $true

    } catch {
        Write-Host "Installation error: $($_.Exception.Message)"
        return $false
    }
}

# Function: Uninstall service
function Uninstall-Service {
    Write-Host "Starting service removal..."
    try {
        # 1. 停止服務
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($service) {
            if ($service.Status -eq 'Running') {
                Write-Host "Stopping service..."
                Stop-Service -Name $ServiceName -Force
                Start-Sleep -Seconds 5
                
                # 確認服務已停止
                $service = Get-Service -Name $ServiceName
                if ($service.Status -ne 'Stopped') {
                    Write-Host "Service still running, waiting longer..."
                    Start-Sleep -Seconds 10
                }
            }
        }
        
        # 2. 強制終止相關進程
        Write-Host "Checking for running ScadaEngine processes..."
        $processes = Get-Process -Name "ScadaEngine*" -ErrorAction SilentlyContinue
        if ($processes) {
            Write-Host "Found $($processes.Count) ScadaEngine process(es), terminating..."
            foreach ($proc in $processes) {
                try {
                    Write-Host "  Killing process $($proc.Name) (PID: $($proc.Id))"
                    $proc.Kill()
                } catch {
                    Write-Host "  Warning: Could not kill process $($proc.Name): $($_.Exception.Message)" -ForegroundColor Yellow
                }
            }
            Start-Sleep -Seconds 3
        }
        
        # 3. 等待檔案解鎖
        Write-Host "Waiting for file locks to release..."
        $maxWait = 15
        $waited = 0
        $testFile = Join-Path $TargetPath "ScadaEngine.Engine.exe"
        
        while ($waited -lt $maxWait) {
            try {
                if (Test-Path $testFile) {
                    # 嘗試訪問檔案
                    $stream = [System.IO.File]::Open($testFile, 'Open', 'Read', 'None')
                    $stream.Close()
                    break
                }
            } catch {
                Write-Host "  Files still locked, waiting... ($waited/$maxWait seconds)"
                Start-Sleep -Seconds 1
                $waited++
            }
        }
        
        # 4. 刪除服務
        Write-Host "Removing Windows service..."
        sc.exe delete $ServiceName | Out-Null
        
        if ($LASTEXITCODE -eq 0) {
            Write-Host "Service removed successfully!"
        } else {
            Write-Host "Warning: Service deletion may have failed (exit code: $LASTEXITCODE)" -ForegroundColor Yellow
        }
        
        # 5. 清理應用程式檔案（可選）
        $response = Read-Host "Do you want to delete application files in $TargetPath? (y/N)"
        if ($response -eq 'y' -or $response -eq 'Y') {
            try {
                Write-Host "Removing application files..."
                Remove-Item -Path $TargetPath -Recurse -Force
                Write-Host "Application files removed."
            } catch {
                Write-Host "Warning: Could not remove all files: $($_.Exception.Message)" -ForegroundColor Yellow
                Write-Host "You may need to manually delete: $TargetPath" -ForegroundColor Yellow
            }
        } else {
            Write-Host "Application files preserved in: $TargetPath"
        }
        
        return $true
    } catch {
        Write-Host "Removal error: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# Function: Start service
function Start-ServiceInstance {
    Write-Host "Starting service..."
    try {
        # 先檢查服務是否存在
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if (-not $service) {
            Write-Host "Service not found. Please install first." -ForegroundColor Red
            return $false
        }
        
        if ($service.Status -eq 'Running') {
            Write-Host "Service is already running." -ForegroundColor Green
            return $true
        }
        
        # 檢查相依項和配置
        Write-Host "Checking service dependencies..."
        $ExePath = Join-Path $TargetPath "ScadaEngine.Engine.exe"
        
        if (-not (Test-Path $ExePath)) {
            Write-Host "ERROR: Executable not found: $ExePath" -ForegroundColor Red
            return $false
        }
        
        # 檢查關鍵 DLL
        $requiredDlls = @('Microsoft.Data.SqlClient.dll', 'Newtonsoft.Json.dll')
        $missingDlls = @()
        foreach ($dll in $requiredDlls) {
            $dllPath = Join-Path $TargetPath $dll
            if (-not (Test-Path $dllPath)) {
                $missingDlls += $dll
            }
        }
        
        if ($missingDlls.Count -gt 0) {
            Write-Host "ERROR: Missing DLL files: $($missingDlls -join ', ')" -ForegroundColor Red
            Write-Host "Recommendation: Reinstall service with option 1" -ForegroundColor Yellow
        }
        
        # 檢查配置檔案
        $configPath = Join-Path $TargetPath "Setting\dbSetting.json"
        if (-not (Test-Path $configPath)) {
            Write-Host "WARNING: Database config not found: $configPath" -ForegroundColor Yellow
        }
        
        Write-Host "Attempting to start service..."
        
        # 移除執行檔測試，因為服務應用程式可能不支援命令行參數測試
        # 且可能造成 hang 的情況
        
        Start-Service -Name $ServiceName
        
        # 等待啟動並檢查狀態
        $timeout = 30
        $elapsed = 0
        do {
            Start-Sleep -Seconds 1
            $elapsed++
            $service = Get-Service -Name $ServiceName
            Write-Host "Waiting for service to start... ($elapsed/$timeout) - Status: $($service.Status)"
        } while ($service.Status -ne 'Running' -and $service.Status -ne 'Stopped' -and $elapsed -lt $timeout)
        
        if ($service.Status -eq 'Running') {
            Write-Host "Service started successfully!" -ForegroundColor Green
            Get-ServiceStatus
            return $true
        } else {
            Write-Host "Service failed to start. Status: $($service.Status)" -ForegroundColor Red
            
            # 檢查 Windows 事件日誌
            Write-Host "Checking Windows Event Log for errors..."
            $events = Get-WinEvent -FilterHashtable @{LogName='System'; Level=2; StartTime=(Get-Date).AddMinutes(-5)} -ErrorAction SilentlyContinue |
                      Where-Object {$_.Message -like "*$ServiceName*" -or $_.Message -like "*ScadaEngine*"} |
                      Select-Object -First 3
            
            if ($events) {
                Write-Host "Recent error events:" -ForegroundColor Red
                foreach ($event in $events) {
                    Write-Host "  [$($event.TimeCreated)] $($event.LevelDisplayName): $($event.Message)"
                }
            }
            
            Write-Host "Troubleshooting suggestions:" -ForegroundColor Yellow
            Write-Host "1. Check Event Viewer: eventvwr.msc -> Windows Logs -> System"
            Write-Host "2. Test manually: cd `"$TargetPath`" && .\ScadaEngine.Engine.exe"
            Write-Host "3. Check database connection in dbSetting.json"
            Write-Host "4. Reinstall with option 2 (Uninstall) then option 1 (Install)"
            
            return $false
        }
    } catch {
        Write-Host "Start failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "Full error details:" -ForegroundColor Red
        Write-Host $_.Exception.ToString() -ForegroundColor Red
        return $false
    }
}

# Function: Stop service
function Stop-ServiceInstance {
    Write-Host "Stopping service..."
    try { Stop-Service -Name $ServiceName -Force; Start-Sleep 2; Get-ServiceStatus; return $true } catch { Write-Host "Stop failed: $($_.Exception.Message)"; return $false }
}

# Function: Update service
function Update-Service {
    Write-Host "Updating service files..."
    try {
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        $wasRunning = $false
        if ($service -and $service.Status -eq 'Running') { $wasRunning=$true; Stop-Service -Name $ServiceName -Force; Start-Sleep 3 }

        # Build & publish
        Set-Location $ProjectPath
        dotnet publish -c Release --self-contained false -o "$ProjectPath\bin\Release\Publish"
        if ($LASTEXITCODE -ne 0) { Write-Host "Build failed. Exiting."; return $false }

        # Update exe/dll only
        $publishPath = "$ProjectPath\bin\Release\Publish"
        Get-ChildItem -Path $publishPath -File -Include "*.exe","*.dll" | ForEach-Object {
            Copy-Item -Path $_.FullName -Destination $TargetPath -Force
            Write-Host "Updated: $($_.Name)"
        }

        if ($wasRunning) { Start-Service -Name $ServiceName; Start-Sleep 2 }

        Get-ServiceStatus
        Write-Host "Update completed!"
        return $true
    } catch { Write-Host "Update error: $($_.Exception.Message)"; return $false }
}

# Function: Show logs
function Show-Logs {
    $logPath = Join-Path $TargetPath "Log"
    if (Test-Path $logPath) {
        Write-Host "Log files in $logPath :"
        Get-ChildItem -Path $logPath -File | Sort-Object LastWriteTime -Descending | Select-Object -First 10 | ForEach-Object {
            Write-Host "  $($_.Name) - $($_.LastWriteTime)"
        }
        $latestLog = Get-ChildItem -Path $logPath -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if ($latestLog) {
            Write-Host "`nLatest log content ($($latestLog.Name)):"
            Write-Host "=================================="
            Get-Content -Path $latestLog.FullName -Tail 20
        }
    } else { Write-Host "Log directory not found: $logPath" }
}

# Function: Diagnose service environment
function Diagnose-Service {
    Write-Host "=== SCADA Engine Service Diagnostics ===" -ForegroundColor Cyan
    
    # 1. 檢查服務狀態
    Write-Host "`n1. Service Status:" -ForegroundColor Yellow
    Get-ServiceStatus
    
    # 2. 檢查執行檔和相依項
    Write-Host "`n2. File Dependencies:" -ForegroundColor Yellow
    $ExePath = Join-Path $TargetPath "ScadaEngine.Engine.exe"
    Write-Host "Executable: $(Test-Path $ExePath)"
    
    $criticalFiles = @(
        'Microsoft.Data.SqlClient.dll',
        'Newtonsoft.Json.dll',
        'FluentModbus.dll',
        'MQTTnet.dll',
        'Serilog.dll'
    )
    
    foreach ($file in $criticalFiles) {
        $filePath = Join-Path $TargetPath $file
        $exists = Test-Path $filePath
        $status = if ($exists) { "OK" } else { "MISSING" }
        $color = if ($exists) { "Green" } else { "Red" }
        Write-Host "  $file`: $status" -ForegroundColor $color
    }
    
    # 3. 檢查配置檔案
    Write-Host "`n3. Configuration Files:" -ForegroundColor Yellow
    $configFiles = @(
        'Setting\dbSetting.json',
        'MqttSetting\MqttSetting.json',
        'DatabaseSchema\DatabaseSchema.json'
    )
    
    foreach ($config in $configFiles) {
        $configPath = Join-Path $TargetPath $config
        $exists = Test-Path $configPath
        $status = if ($exists) { "OK" } else { "MISSING" }
        $color = if ($exists) { "Green" } else { "Red" }
        Write-Host "  $config`: $status" -ForegroundColor $color
    }
    
    # 4. 檢查 .NET Runtime
    Write-Host "`n4. .NET Runtime:" -ForegroundColor Yellow
    try {
        # 檢查系統安裝的 .NET
        $dotnetRuntimes = & dotnet --list-runtimes 2>$null
        if ($dotnetRuntimes) {
            Write-Host "  Installed .NET Runtimes:" -ForegroundColor Green
            $dotnetRuntimes | ForEach-Object { Write-Host "    $_" }
            
            # 檢查是否有 .NET 8
            $hasNet8 = $dotnetRuntimes -match "Microsoft\.NETCore\.App 8\."
            if ($hasNet8) {
                Write-Host "  .NET 8.0 Runtime: FOUND" -ForegroundColor Green
            } else {
                Write-Host "  .NET 8.0 Runtime: MISSING" -ForegroundColor Red
                Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
            }
        } else {
            Write-Host "  .NET Runtime: NOT INSTALLED" -ForegroundColor Red
            Write-Host "  Download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Yellow
        }
        
        # 檢查是否為 self-contained 應用程式
        $runtimeConfigPath = Join-Path $TargetPath "ScadaEngine.Engine.runtimeconfig.json"
        if (Test-Path $runtimeConfigPath) {
            $runtimeConfig = Get-Content $runtimeConfigPath | ConvertFrom-Json
            if ($runtimeConfig.runtimeOptions.includedFrameworks) {
                Write-Host "  Application Type: SELF-CONTAINED" -ForegroundColor Green
                Write-Host "  .NET Runtime dependency: NOT REQUIRED" -ForegroundColor Green
            } else {
                Write-Host "  Application Type: FRAMEWORK-DEPENDENT" -ForegroundColor Yellow
                Write-Host "  .NET Runtime dependency: REQUIRED" -ForegroundColor Yellow
            }
        }
    } catch {
        Write-Host "  Unable to check .NET Runtime: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    
    # 5. 檢查 Windows 事件日誌
    Write-Host "`n5. Recent Error Events:" -ForegroundColor Yellow
    try {
        $events = Get-WinEvent -FilterHashtable @{LogName='System'; Level=2; StartTime=(Get-Date).AddHours(-1)} -ErrorAction SilentlyContinue |
                  Where-Object {$_.Message -like "*$ServiceName*" -or $_.Message -like "*ScadaEngine*"} |
                  Select-Object -First 5
        
        if ($events) {
            foreach ($event in $events) {
                Write-Host "  [$($event.TimeCreated)] $($event.Message)" -ForegroundColor Red
            }
        } else {
            Write-Host "  No recent service-related errors found" -ForegroundColor Green
        }
    } catch {
        Write-Host "  Unable to check event log: $($_.Exception.Message)" -ForegroundColor Yellow
    }
    
    # 6. 提供解決方案
    Write-Host "`n6. Recommended Actions:" -ForegroundColor Yellow
    Write-Host "  - If DLLs are missing: Reinstall service (option 2 then 1)"
    Write-Host "  - If config files missing: Check source project structure"
    Write-Host "  - Manual test (safe): cd `"$TargetPath`" && .\\ScadaEngine.Engine.exe"
    Write-Host "  - Event Viewer: eventvwr.msc -> Windows Logs -> System"
    Write-Host "  - Service startup: Use option 3 (Start Service) for detailed monitoring"
    Write-Host "=========================================" -ForegroundColor Cyan
}
# Function: Force cleanup
function Force-Cleanup {
    Write-Host "=== FORCE CLEANUP ===" -ForegroundColor Red
    Write-Host "This will forcefully terminate processes and clean up files."
    
    $confirm = Read-Host "Are you sure you want to continue? (y/N)"
    if ($confirm -ne 'y' -and $confirm -ne 'Y') {
        Write-Host "Cleanup cancelled."
        return
    }
    
    try {
        # 1. 強制停止並刪除服務
        Write-Host "1. Stopping and removing service..."
        $service = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
        if ($service) {
            try { Stop-Service -Name $ServiceName -Force } catch { }
            sc.exe delete $ServiceName | Out-Null
        }
        
        # 2. 終止所有相關進程
        Write-Host "2. Terminating all ScadaEngine processes..."
        Get-Process -Name "*ScadaEngine*" -ErrorAction SilentlyContinue | ForEach-Object {
            Write-Host "  Killing $($_.Name) (PID: $($_.Id))"
            try { $_.Kill() } catch { }
        }
        
        # 3. 等待進程終止
        Start-Sleep -Seconds 5
        
        # 4. 使用 taskkill 強制解鎖
        Write-Host "3. Force killing any remaining processes..."
        try {
            taskkill /F /IM "ScadaEngine*" 2>$null | Out-Null
        } catch { }
        
        # 5. 強制刪除檔案
        Write-Host "4. Force removing application directory..."
        if (Test-Path $TargetPath) {
            try {
                # 先移除唯讀屬性
                Get-ChildItem -Path $TargetPath -Recurse -Force | ForEach-Object {
                    try { $_.Attributes = 'Normal' } catch { }
                }
                
                # 強制刪除
                Remove-Item -Path $TargetPath -Recurse -Force
                Write-Host "Directory removed successfully." -ForegroundColor Green
            } catch {
                Write-Host "Could not remove directory: $($_.Exception.Message)" -ForegroundColor Red
                Write-Host "Try restarting Windows and running cleanup again." -ForegroundColor Yellow
            }
        }
        
        Write-Host "5. Force cleanup completed!" -ForegroundColor Green
        Write-Host "You can now try reinstalling the service." -ForegroundColor Green
        
    } catch {
        Write-Host "Force cleanup error: $($_.Exception.Message)" -ForegroundColor Red
    }
}
# Main execution
switch ($Action.ToLower()) {
    "install" { if (Get-ServiceStatus) { Write-Host "Service already installed. Use 'uninstall' first or 'update'." } else { Install-Service } }
    "uninstall" { if (Get-ServiceStatus) { Uninstall-Service } else { Write-Host "Service not found. Nothing to uninstall." } }
    "start" { if (Get-ServiceStatus) { Start-ServiceInstance } else { Write-Host "Service not installed. Use 'install' first." } }
    "stop" { if (Get-ServiceStatus) { Stop-ServiceInstance } else { Write-Host "Service not installed." } }
    "restart" { if (Get-ServiceStatus) { Stop-ServiceInstance; Start-Sleep 2; Start-ServiceInstance } else { Write-Host "Service not installed. Use 'install' first." } }
    "update" { Update-Service }
    "logs" { Show-Logs }
    "status" { Get-ServiceStatus }
    "diagnose" { Diagnose-Service }
    "cleanup" { Force-Cleanup }
    default {
        Write-Host "Available actions: install, uninstall, start, stop, restart, update, logs, status, diagnose, cleanup"
        Write-Host ""
        Write-Host "Examples:"
        Write-Host "  .\DeployService.ps1 install   - Install service to C:\SCADA\Engine\App\"
        Write-Host "  .\DeployService.ps1 diagnose  - Run comprehensive diagnostics"
        Write-Host "  .\DeployService.ps1 cleanup   - Force cleanup (use if files are locked)"
        Write-Host "  .\DeployService.ps1 start     - Start service with detailed feedback"
        Write-Host "  .\DeployService.ps1 logs      - Show recent log files"
    }
}
