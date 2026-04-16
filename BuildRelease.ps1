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
$engineConfigs = @("Setting", "Modbus", "MqttSetting", "DatabaseSchema", "Algorithms")
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

# Copy PythonRuntime if exists (portable, no need to reinstall)
$pythonRuntime = Join-Path $engineProject "PythonRuntime"
if (Test-Path $pythonRuntime) {
    Write-Host "  Copying PythonRuntime (portable)..." -ForegroundColor Gray
    $pyDst = "$ReleasePath\Engine\App\PythonRuntime"
    if (-not (Test-Path $pyDst)) { New-Item -ItemType Directory -Path $pyDst -Force | Out-Null }
    Copy-Item -Path "$pythonRuntime\*" -Destination $pyDst -Recurse -Force
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

Write-Host "  Web OK" -ForegroundColor Green

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
)
if exist "C:\SCADA\Web\App\Setting" (
    if not exist "%_BACKUP%" mkdir "%_BACKUP%"
    xcopy /E /I /Y "C:\SCADA\Web\App\Setting"         "%_BACKUP%\Web\Setting"         >nul
    xcopy /E /I /Y "C:\SCADA\Web\App\MqttSetting"     "%_BACKUP%\Web\MqttSetting"     >nul
)

echo [1/5] Stopping existing services (if any)...
echo.
net stop ScadaEngineService >nul 2>&1
net stop ScadaWebService >nul 2>&1

echo [2/5] Installing Engine...
echo.
xcopy /E /I /Y "%~dp0Engine\App" "C:\SCADA\Engine\App"
sc create ScadaEngineService binPath= "\"C:\SCADA\Engine\App\ScadaEngine.Engine.exe\"" DisplayName= "\"SCADA Engine Service\"" start= auto
sc description ScadaEngineService "Industrial SCADA data collection engine"
sc failure ScadaEngineService reset= 86400 actions= restart/5000/restart/10000/restart/30000
echo Engine installed.
echo.

echo [3/5] Installing Web...
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
    if exist "%_BACKUP%\Web\Setting" (
        xcopy /E /I /Y "%_BACKUP%\Web\Setting"     "C:\SCADA\Web\App\Setting"         >nul
        xcopy /E /I /Y "%_BACKUP%\Web\MqttSetting" "C:\SCADA\Web\App\MqttSetting"     >nul
    )
    rmdir /S /Q "%_BACKUP%"
    echo [INFO] Site config restored successfully.
    echo.
)

echo [4/5] Opening firewall port 5038...
netsh advfirewall firewall add rule name="ScadaEngine Web" dir=in action=allow protocol=TCP localport=5038
echo.

echo [5/5] Starting services...
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
    echo [OK] Site config was PRESERVED (Modbus, Setting, MqttSetting).
) else (
    echo [NOTE] First install — edit config files as needed:
    echo   C:\SCADA\Engine\App\Setting\dbSetting.json      (DB connection)
    echo   C:\SCADA\Engine\App\Modbus\*.json                (Modbus devices)
    echo   C:\SCADA\Engine\App\MqttSetting\MqttSetting.json (MQTT broker)
)
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
