# SCADA 一鍵打包腳本 — 在開發機執行，產出可直接部署的資料夾
# 用法: .\BuildRelease.ps1
# 產出: .\Release\SCADA_Release_yyyyMMdd\

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

$RootPath = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Definition }
if (-not $RootPath) { $RootPath = (Get-Location).Path }
$DateTag = Get-Date -Format "yyyyMMdd_HHmm"
$ReleasePath = Join-Path $RootPath "Release\SCADA_Release_$DateTag"

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  SCADA Release Builder" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Output: $ReleasePath"
Write-Host ""

# Clean
if (Test-Path $ReleasePath) { Remove-Item $ReleasePath -Recurse -Force }
New-Item -ItemType Directory -Path $ReleasePath -Force | Out-Null

# ──────────────────────────────────────
# 1. Build Engine
# ──────────────────────────────────────
Write-Host "[1/4] Building Engine (self-contained)..." -ForegroundColor Yellow
$engineProject = Join-Path $RootPath "ScadaEngine.Engine"
dotnet publish $engineProject -c Release --self-contained true --runtime win-x64 -o "$ReleasePath\Engine\App" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Engine build FAILED" -ForegroundColor Red; exit 1 }

# Copy Engine configs
$engineConfigs = @("Setting", "Modbus", "MqttSetting", "DatabaseSchema", "DBPoint", "Algorithms")
foreach ($dir in $engineConfigs) {
    $src = Join-Path $engineProject $dir
    if (Test-Path $src) {
        $dst = "$ReleasePath\Engine\App\$dir"
        if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
        Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force
    }
}

# Copy Engine scripts
Copy-Item -Path (Join-Path $engineProject "Scripts\DeployService.ps1") -Destination "$ReleasePath\Engine\" -Force
Copy-Item -Path (Join-Path $engineProject "Scripts\QuickDeploy.bat") -Destination "$ReleasePath\Engine\" -Force

# Copy Python setup files (get-pip.py + SetupPythonRuntime.ps1)
$getPip = Join-Path $engineProject "Scripts\get-pip.py"
if (Test-Path $getPip) { Copy-Item $getPip -Destination "$ReleasePath\Engine\" -Force }
$setupPy = Join-Path $engineProject "Scripts\SetupPythonRuntime.ps1"
if (Test-Path $setupPy) { Copy-Item $setupPy -Destination "$ReleasePath\Engine\" -Force }

# PythonRuntime：缺少時自動執行 SetupPythonRuntime.ps1 建立（需網路），確保部署包一定含可攜式 Python
$pythonRuntime = Join-Path $engineProject "PythonRuntime"
if (-not (Test-Path (Join-Path $pythonRuntime "python.exe"))) {
    Write-Host "  PythonRuntime not found - running SetupPythonRuntime.ps1 (requires internet)..." -ForegroundColor Yellow
    & (Join-Path $engineProject "Scripts\SetupPythonRuntime.ps1")
    if (-not (Test-Path (Join-Path $pythonRuntime "python.exe"))) {
        Write-Host "PythonRuntime setup FAILED - LogicFlow algorithm nodes will not work on target machines" -ForegroundColor Red
        exit 1
    }
}
Write-Host "  Copying PythonRuntime (portable)..." -ForegroundColor Gray
$pyDst = "$ReleasePath\Engine\App\PythonRuntime"
if (-not (Test-Path $pyDst)) { New-Item -ItemType Directory -Path $pyDst -Force | Out-Null }
Copy-Item -Path "$pythonRuntime\*" -Destination $pyDst -Recurse -Force

# LineSetting.json：正式檔含 token 不進 git，打包時若缺就用範本補上（placeholder token 會被程式視為未設定、安全停用）
$lineSettingDst = "$ReleasePath\Engine\App\Setting\LineSetting.json"
$lineSettingExample = Join-Path $engineProject "Setting\LineSetting.example.json"
if (-not (Test-Path $lineSettingDst) -and (Test-Path $lineSettingExample)) {
    Copy-Item $lineSettingExample -Destination $lineSettingDst -Force
    Write-Host "  LineSetting.json created from example (fill token on-site to enable Line notify)" -ForegroundColor Gray
}

Write-Host "  Engine OK" -ForegroundColor Green

# ──────────────────────────────────────
# 2. Build Web
# ──────────────────────────────────────
Write-Host "[2/4] Building Web (self-contained)..." -ForegroundColor Yellow
$webProject = Join-Path $RootPath "ScadaEngine.Web"
dotnet publish $webProject -c Release --self-contained true --runtime win-x64 -o "$ReleasePath\Web\App" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Web build FAILED" -ForegroundColor Red; exit 1 }

# Copy Web configs
$webMqtt = Join-Path $webProject "MqttSetting\MqttSetting.json"
$webMqttDst = "$ReleasePath\Web\App\MqttSetting"
if (-not (Test-Path $webMqttDst)) { New-Item -ItemType Directory -Path $webMqttDst -Force | Out-Null }
if (Test-Path $webMqtt) { Copy-Item $webMqtt -Destination "$webMqttDst\MqttSetting.json" -Force }

# Copy shared DB config from Engine
$dbSetting = Join-Path $engineProject "Setting\dbSetting.json"
$webSettingDir = "$ReleasePath\Web\App\Setting"
if (-not (Test-Path $webSettingDir)) { New-Item -ItemType Directory -Path $webSettingDir -Force | Out-Null }
if (Test-Path $dbSetting) { Copy-Item $dbSetting -Destination "$webSettingDir\dbSetting.json" -Force }

# Copy DatabaseSchema from Engine
$schemaFile = Join-Path $engineProject "DatabaseSchema\DatabaseSchema.json"
$webSchemaDir = "$ReleasePath\Web\App\DatabaseSchema"
if (-not (Test-Path $webSchemaDir)) { New-Item -ItemType Directory -Path $webSchemaDir -Force | Out-Null }
if (Test-Path $schemaFile) { Copy-Item $schemaFile -Destination "$webSchemaDir\DatabaseSchema.json" -Force }

# Copy Algorithms from Engine（Web LogicFlow 需要讀取演算法清單）
$algoSrc = Join-Path $engineProject "Algorithms"
if (Test-Path $algoSrc) {
    $algoDst = "$ReleasePath\Web\App\Algorithms"
    if (-not (Test-Path $algoDst)) { New-Item -ItemType Directory -Path $algoDst -Force | Out-Null }
    Copy-Item -Path "$algoSrc\*" -Destination $algoDst -Recurse -Force
    Write-Host "  Algorithms copied to Web" -ForegroundColor Gray
}

# Copy Web scripts
Copy-Item -Path (Join-Path $webProject "Scripts\DeployWebService.ps1") -Destination "$ReleasePath\Web\" -Force
Copy-Item -Path (Join-Path $webProject "Scripts\QuickDeploy.bat") -Destination "$ReleasePath\Web\" -Force

# 內網 HTTPS 憑證腳本 → Web\App\certs\（Program.cs 由 ContentRoot\certs 讀 pfx，故腳本須落在 App\certs）
# 只帶「腳本」，不帶任何 pfx/crt（憑證由部署機現產；升級時 xcopy 不刪除既有憑證故保留）。詳見 docs/功能說明書_內網HTTPS部署.md
$certScriptSrc = Join-Path $webProject "certs"
$certScriptDst = "$ReleasePath\Web\App\certs"
if (Test-Path $certScriptSrc) {
    if (-not (Test-Path $certScriptDst)) { New-Item -ItemType Directory -Path $certScriptDst -Force | Out-Null }
    Copy-Item -Path "$certScriptSrc\*.ps1" -Destination $certScriptDst -Force
    Write-Host "  HTTPS cert scripts copied to Web\App\certs (no pfx/crt bundled)" -ForegroundColor Gray
}

Write-Host "  Web OK" -ForegroundColor Green

# 工程師工具不落地客戶伺服器：reset-engineer-password.ps1 移到包根目錄（隨工程師 USB 攜帶使用），
# 並自兩個 App\Setting 移除 — 留在伺服器上等於任何能碰檔案系統的人一鍵重設 engineer 密碼
$resetTool = "$ReleasePath\Engine\App\Setting\reset-engineer-password.ps1"
if (Test-Path $resetTool) { Copy-Item $resetTool -Destination "$ReleasePath\reset-engineer-password.ps1" -Force }
Remove-Item "$ReleasePath\Engine\App\Setting\reset-engineer-password.ps1", "$ReleasePath\Web\App\Setting\reset-engineer-password.ps1" -Force -ErrorAction SilentlyContinue
Write-Host "  reset-engineer-password.ps1 moved to package root (engineer tool, not installed on server)" -ForegroundColor Gray

# ──────────────────────────────────────
# 3. Create install script for on-site
# ──────────────────────────────────────
Write-Host "[3/4] Creating install scripts..." -ForegroundColor Yellow

# On-site install script (simplified, no build needed)
@"
@echo off
echo ========================================
echo   SCADA On-Site Installer
echo ========================================
echo.

net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] Please run as Administrator
    pause
    exit /b 1
)

:: ── Detect upgrade: backup site-specific configs ──
set "_BACKUP=C:\SCADA\_ConfigBackup"
set "_IS_UPGRADE=0"

if exist "C:\SCADA\Engine\App\Modbus" (
    set "_IS_UPGRADE=1"
    echo [INFO] Detected existing installation — preserving site config...
    echo.
    if not exist "%_BACKUP%" mkdir "%_BACKUP%"
    xcopy /E /I /Y "C:\SCADA\Engine\App\Modbus"      "%_BACKUP%\Engine\Modbus"      >nul
    xcopy /E /I /Y "C:\SCADA\Engine\App\Setting"      "%_BACKUP%\Engine\Setting"      >nul
    xcopy /E /I /Y "C:\SCADA\Engine\App\MqttSetting"  "%_BACKUP%\Engine\MqttSetting"  >nul
    xcopy /E /I /Y "C:\SCADA\Engine\App\DBPoint"      "%_BACKUP%\Engine\DBPoint"      >nul
)
if exist "C:\SCADA\Web\App\Setting" (
    if not exist "%_BACKUP%" mkdir "%_BACKUP%"
    xcopy /E /I /Y "C:\SCADA\Web\App\Setting"         "%_BACKUP%\Web\Setting"         >nul
    xcopy /E /I /Y "C:\SCADA\Web\App\MqttSetting"     "%_BACKUP%\Web\MqttSetting"     >nul
)

echo [1/6] Stopping existing services (if any)...
echo.
net stop ScadaEngineService >nul 2>&1
net stop ScadaWebService >nul 2>&1

echo [2/6] Installing Engine...
echo.
xcopy /E /I /Y "%~dp0Engine\App" "C:\SCADA\Engine\App"
sc create ScadaEngineService binPath= "\"C:\SCADA\Engine\App\ScadaEngine.Engine.exe\"" DisplayName= "\"SCADA Engine Service\"" start= auto
sc description ScadaEngineService "Industrial SCADA data collection engine"
sc failure ScadaEngineService reset= 86400 actions= restart/5000/restart/10000/restart/30000
echo Engine installed.
echo.

echo [3/6] Installing Web...
echo.
xcopy /E /I /Y "%~dp0Web\App" "C:\SCADA\Web\App"
sc create ScadaWebService binPath= "\"C:\SCADA\Web\App\ScadaEngine.Web.exe\"" DisplayName= "\"SCADA Web Service\"" start= auto
sc description ScadaWebService "SCADA Web Dashboard (http://0.0.0.0:5038)"
sc failure ScadaWebService reset= 86400 actions= restart/5000/restart/10000/restart/30000
echo Web installed.
echo.

:: ── Restore site-specific configs ──
if "%_IS_UPGRADE%"=="1" (
    echo [INFO] Restoring site config...
    xcopy /E /I /Y "%_BACKUP%\Engine\Modbus"      "C:\SCADA\Engine\App\Modbus"      >nul
    xcopy /E /I /Y "%_BACKUP%\Engine\Setting"      "C:\SCADA\Engine\App\Setting"      >nul
    xcopy /E /I /Y "%_BACKUP%\Engine\MqttSetting"  "C:\SCADA\Engine\App\MqttSetting"  >nul
    if exist "%_BACKUP%\Engine\DBPoint" xcopy /E /I /Y "%_BACKUP%\Engine\DBPoint" "C:\SCADA\Engine\App\DBPoint" >nul
    if exist "%_BACKUP%\Web\Setting" (
        xcopy /E /I /Y "%_BACKUP%\Web\Setting"     "C:\SCADA\Web\App\Setting"         >nul
        xcopy /E /I /Y "%_BACKUP%\Web\MqttSetting" "C:\SCADA\Web\App\MqttSetting"     >nul
    )
    :: Setting restore is for site JSON configs only; *.ps1 scripts are part of the app,
    :: always take the package version (otherwise old scripts get restored over new ones)
    copy /Y "%~dp0Engine\App\Setting\*.ps1" "C:\SCADA\Engine\App\Setting\" >nul
    if exist "C:\SCADA\Web\App\Setting" copy /Y "%~dp0Web\App\Setting\*.ps1" "C:\SCADA\Web\App\Setting\" >nul
    rmdir /S /Q "%_BACKUP%"
    :: Engineer tool must never live on the server (anyone with file access could reset the engineer password)
    del /Q "C:\SCADA\Engine\App\Setting\reset-engineer-password.ps1" 2>nul
    del /Q "C:\SCADA\Web\App\Setting\reset-engineer-password.ps1" 2>nul
    echo [INFO] Site config restored successfully.
    echo.
)

:: ── Database setup: create DB if missing + backup folder ACL + app login ──
:: idempotent; runs AFTER config restore so it reads the site's dbSetting.json
echo [4/6] Database setup...
powershell -ExecutionPolicy Bypass -File "C:\SCADA\Engine\App\Setting\install-db.ps1"
if %errorLevel% NEQ 0 (
    echo [WARN] Database setup reported errors. Engine startup has a fallback,
    echo        but weekly backup needs the folders. Re-run manually if needed:
    echo        powershell -ExecutionPolicy Bypass -File C:\SCADA\Engine\App\Setting\install-db.ps1
)
echo.

echo [5/6] Opening firewall ports 5038 (HTTP) and 7189 (HTTPS)...
netsh advfirewall firewall add rule name="ScadaEngine Web" dir=in action=allow protocol=TCP localport=5038
netsh advfirewall firewall add rule name="ScadaEngine Web HTTPS" dir=in action=allow protocol=TCP localport=7189
echo.

echo [6/6] Starting services...
net start ScadaEngineService
net start ScadaWebService
echo.

echo ========================================
echo   Installation Complete!
echo   Web URL: http://localhost:5038
echo   Default login: ITRI / ITRI
echo ========================================
echo.
if "%_IS_UPGRADE%"=="1" (
    echo [OK] Site config was PRESERVED (Modbus, Setting, MqttSetting, DBPoint).
) else (
    echo [NOTE] First install — edit config files as needed:
    echo   C:\SCADA\Engine\App\Setting\dbSetting.json      (DB connection)
    echo   C:\SCADA\Engine\App\Modbus\*.json                (Modbus devices)
    echo   C:\SCADA\Engine\App\MqttSetting\MqttSetting.json (MQTT broker)
    echo   C:\SCADA\Engine\App\DBPoint\*.json               (DB source points)
    echo   C:\SCADA\Engine\App\Setting\DbMaintenanceSetting.json (weekly DB backup schedule)
)
echo.
echo [OPTIONAL] Enable HTTPS for LAN access (https://server-ip:7189):
echo   1. Run: powershell -ExecutionPolicy Bypass -File C:\SCADA\Web\App\certs\generate-https-cert.ps1
echo   2. Restart Web service:  net stop ScadaWebService ^&^& net start ScadaWebService
echo   3. Give C:\SCADA\Web\App\certs\ScadaEngine-CA.crt to each client, run install-ca-on-client.ps1 as Admin
echo   (Skip to keep HTTP-only on port 5038. See docs: 功能說明書_內網HTTPS部署.md)
echo.
pause
"@ | Set-Content -Path "$ReleasePath\Install.bat" -Encoding ASCII

Write-Host "  Install.bat created" -ForegroundColor Green

# ──────────────────────────────────────
# 4. Summary
# ──────────────────────────────────────
Write-Host "[4/4] Calculating size..." -ForegroundColor Yellow
$totalSize = [math]::Round(((Get-ChildItem $ReleasePath -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB), 1)

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Output: $ReleasePath" -ForegroundColor Cyan
Write-Host "Size:   ${totalSize} MB" -ForegroundColor Cyan
Write-Host ""
Write-Host "Folder structure:"
Write-Host "  SCADA_Release_$DateTag\"
Write-Host "  +-- Install.bat              <- On-site: right-click Run as Admin"
Write-Host "  +-- Engine\"
Write-Host "  |   +-- App\                 <- Engine executable + configs"
Write-Host "  |   +-- QuickDeploy.bat      <- Engine service manager"
Write-Host "  |   +-- DeployService.ps1"
Write-Host "  +-- Web\"
Write-Host "      +-- App\                 <- Web executable + wwwroot + configs"
Write-Host "      +-- QuickDeploy.bat      <- Web service manager"
Write-Host "      +-- DeployWebService.ps1"
Write-Host ""
Write-Host "On-site deployment:"
Write-Host "  1. Copy entire folder to USB"
Write-Host "  2. On target server: right-click Install.bat -> Run as Administrator"
Write-Host "  3. Done! Open http://server-ip:5038"
Write-Host ""
