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
Write-Host "[1/6] Building Engine (self-contained)..." -ForegroundColor Yellow
$engineProject = Join-Path $RootPath "ScadaEngine.Engine"
dotnet publish $engineProject -c Release --self-contained true --runtime win-x64 -o "$ReleasePath\Engine\App" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Engine build FAILED" -ForegroundColor Red; exit 1 }

# Copy Engine configs
# Modbus / DBPoint 不列入複製清單 — 它們與 OpcUaPoint 都是「現場資料」而非產品，稍後統一清空
$engineConfigs = @("Setting", "MqttSetting", "DatabaseSchema", "Algorithms")
foreach ($dir in $engineConfigs) {
    $src = Join-Path $engineProject $dir
    if (Test-Path $src) {
        $dst = "$ReleasePath\Engine\App\$dir"
        if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
        Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force
    }
}

# 現場資料夾（Modbus / DBPoint / OpcUaPoint）：dotnet publish 會依 Engine csproj 的
# CopyToOutputDirectory 把 repo 的 dev 裝置定義（*.json）與範例（*.json.example）帶進 Engine\App。
# 這些是各站台自有的現場資料，不該隨 release 外流到客戶伺服器。作法：清掉整個資料夾內容，
# 只回填 repo 內的 Excel 產生工具（*.xlsm）——現場工程師靠它建裝置定義檔。
# 資料夾（含工具）都在，Install.bat 靠「Modbus 資料夾是否存在」判斷升級的哨兵照常運作，
# 升級時現場既有設定也照樣被備份/還原。
foreach ($siteDir in @("Modbus", "DBPoint", "OpcUaPoint")) {
    $sitePath = "$ReleasePath\Engine\App\$siteDir"
    if (Test-Path $sitePath) { Remove-Item $sitePath -Recurse -Force }
    New-Item -ItemType Directory -Path $sitePath -Force | Out-Null
    # 只回填產生工具（*.xlsm），不帶任何 dev 裝置 json / 範例 / 測試檔
    $toolSrc = Join-Path $engineProject "$siteDir\*.xlsm"
    if (Test-Path $toolSrc) {
        Copy-Item -Path $toolSrc -Destination $sitePath -Force
        Write-Host "  $siteDir : kept generator tool (*.xlsm), stripped dev data" -ForegroundColor Gray
    } else {
        Write-Host "  $siteDir : emptied (no generator tool found)" -ForegroundColor Gray
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
Write-Host "[2/6] Building Web (self-contained)..." -ForegroundColor Yellow
$webProject = Join-Path $RootPath "ScadaEngine.Web"
dotnet publish $webProject -c Release --self-contained true --runtime win-x64 -o "$ReleasePath\Web\App" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "Web build FAILED" -ForegroundColor Red; exit 1 }

# 移除 Web publish 遞移帶入的 Engine 設定資料夾（死檔，避免工程師誤改）
# Web ProjectReference 到 Engine，Engine .csproj 把 Modbus/DBPoint/OpcUaPoint 的 *.json 標了
# CopyToOutputDirectory，SDK 會沿引用把它們複製進 Web 輸出。但 Web 實際讀的是
# appsettings.json 的 WatchedFolder（= C:\SCADA\Engine\App\...），Web\App 下這幾份是死的，
# 留著只會讓現場工程師誤以為要改這裡。刪掉，保持 Web\App 乾淨。
foreach ($deadDir in @("Modbus", "DBPoint", "OpcUaPoint")) {
    $deadPath = "$ReleasePath\Web\App\$deadDir"
    if (Test-Path $deadPath) {
        Remove-Item $deadPath -Recurse -Force
        Write-Host "  Removed dead $deadDir folder from Web\App (Engine owns it)" -ForegroundColor Gray
    }
}

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
# 3. Build ModbusServer (optional gateway, NOT installed by main Install.bat)
# ──────────────────────────────────────
Write-Host "[3/6] Building ModbusServer gateway (self-contained)..." -ForegroundColor Yellow
$modbusProject = Join-Path $RootPath "ScadaEngine.ModbusServer"
dotnet publish $modbusProject -c Release --self-contained true --runtime win-x64 -o "$ReleasePath\ModbusServer\App" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "ModbusServer build FAILED" -ForegroundColor Red; exit 1 }

# Copy ModbusServer configs (publish already carries them, copy again to be explicit like Engine)
foreach ($dir in @("Setting", "MqttSetting", "Web")) {
    $src = Join-Path $modbusProject $dir
    if (Test-Path $src) {
        $dst = "$ReleasePath\ModbusServer\App\$dir"
        if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }
        Copy-Item -Path "$src\*" -Destination $dst -Recurse -Force
    }
}

# Copy ModbusServer scripts
Copy-Item -Path (Join-Path $modbusProject "Scripts\DeployModbusServer.ps1") -Destination "$ReleasePath\ModbusServer\" -Force
Copy-Item -Path (Join-Path $modbusProject "Scripts\QuickDeploy.bat") -Destination "$ReleasePath\ModbusServer\" -Force

Write-Host "  ModbusServer OK" -ForegroundColor Green

# ──────────────────────────────────────
# 4. Build LicenseBridge (net48 HASP bridge — installed by Install.bat)
# ──────────────────────────────────────
# net48 x86 單一目標，不支援 --self-contained / --runtime（靠目標機內建 .NET Framework 4.8）。
# HASP runtime dll 由 csproj 的 CopyToOutputDirectory 帶入輸出目錄，無需外網。
Write-Host "[4/6] Building LicenseBridge (net48 HASP bridge)..." -ForegroundColor Yellow
$bridgeProject = Join-Path $RootPath "ScadaEngine.LicenseBridge"
dotnet publish $bridgeProject -c Release -o "$ReleasePath\LicenseBridge\App" --nologo -v quiet
if ($LASTEXITCODE -ne 0) { Write-Host "LicenseBridge build FAILED" -ForegroundColor Red; exit 1 }

# 驗證 HASP runtime dll 確實落在輸出（少了狗就驗不了證）
$haspProbe = "$ReleasePath\LicenseBridge\App\hasp_net_windows.dll"
if (-not (Test-Path $haspProbe)) { Write-Host "LicenseBridge missing HASP runtime dll" -ForegroundColor Red; exit 1 }
Write-Host "  LicenseBridge OK (net48 + HASP runtime)" -ForegroundColor Green

# ──────────────────────────────────────
# 5. Create install script for on-site
# ──────────────────────────────────────
Write-Host "[5/6] Creating install scripts..." -ForegroundColor Yellow

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

:: ── Prerequisite: .NET Framework 4.8 (required by License Bridge) ──
:: Engine/Web/ModbusServer are self-contained .NET 8 and carry their own runtime,
:: but the HASP License Bridge is net48 and relies on the OS having 4.8 (Release>=528040).
:: Win11 / Server 2022 ship it; Server 2016/2019 / old Win10 do NOT. 4.8 is a ~120MB MS
:: redistributable that can't be xcopy'd, so we DETECT and STOP (not bundle it) — abort
:: before touching anything so the machine stays untouched until 4.8 is installed.
set "_REL="
for /f "tokens=3" %%r in ('reg query "HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" /v Release 2^>nul ^| find "Release"') do set /a _REL=%%r
if not defined _REL goto NET48_MISSING
if %_REL% GEQ 528040 goto NET48_OK
:NET48_MISSING
echo ========================================
echo   [ERROR] .NET Framework 4.8 is NOT installed
echo ========================================
echo   The HASP License Bridge (net48) needs .NET Framework 4.8, and without a
echo   working License Bridge the Engine PAUSES Modbus data collection.
echo   (Engine/Web are self-contained .NET 8 and do NOT need it - only License does.)
echo.
echo   FIX: install .NET Framework 4.8 (or newer), reboot, then re-run Install.bat.
echo   Offline installer (put on the USB for air-gapped sites):
echo     https://dotnet.microsoft.com/download/dotnet-framework/net48
echo.
echo   Nothing was installed. Aborting.
echo.
pause
exit /b 1
:NET48_OK
echo [OK] .NET Framework 4.8+ detected (Release=%_REL%).
echo.

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

echo [1/8] Stopping existing services (if any)...
echo.
net stop ScadaEngineService >nul 2>&1
net stop ScadaWebService >nul 2>&1
net stop ScadaEngineLicense >nul 2>&1

echo [2/8] Installing Engine...
echo.
xcopy /E /I /Y "%~dp0Engine\App" "C:\SCADA\Engine\App"
sc create ScadaEngineService binPath= "\"C:\SCADA\Engine\App\ScadaEngine.Engine.exe\"" DisplayName= "\"SCADA Engine Service\"" start= auto
sc description ScadaEngineService "Industrial SCADA data collection engine"
sc failure ScadaEngineService reset= 86400 actions= restart/5000/restart/10000/restart/30000
echo Engine installed.
echo.

echo [3/8] Installing Web...
echo.
xcopy /E /I /Y "%~dp0Web\App" "C:\SCADA\Web\App"
sc create ScadaWebService binPath= "\"C:\SCADA\Web\App\ScadaEngine.Web.exe\"" DisplayName= "\"SCADA Web Service\"" start= auto
sc description ScadaWebService "SCADA Web Dashboard (http://0.0.0.0:5038)"
sc failure ScadaWebService reset= 86400 actions= restart/5000/restart/10000/restart/30000
echo Web installed.
echo.

echo [4/8] Installing License Bridge (HASP)...
echo.
:: net48 x86 bridge; exe path is hard-coded in Engine as C:\SCADA\LicenseBridge\ (no \App subfolder)
xcopy /E /I /Y "%~dp0LicenseBridge\App" "C:\SCADA\LicenseBridge"
sc create ScadaEngineLicense binPath= "\"C:\SCADA\LicenseBridge\ScadaEngine.LicenseBridge.exe\"" DisplayName= "\"SCADA Engine License Bridge\"" start= auto
sc description ScadaEngineLicense "32-bit HASP verification bridge (Named Pipe)"
sc failure ScadaEngineLicense reset= 86400 actions= restart/5000/restart/10000/restart/30000
echo License Bridge installed.
echo   NOTE: still needs the HASP USB dongle plugged in + Sentinel runtime driver on this server.
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
echo [5/8] Database setup...
powershell -ExecutionPolicy Bypass -File "C:\SCADA\Engine\App\Setting\install-db.ps1"
if %errorLevel% NEQ 0 (
    echo [WARN] Database setup reported errors. Engine startup has a fallback,
    echo        but weekly backup needs the folders. Re-run manually if needed:
    echo        powershell -ExecutionPolicy Bypass -File C:\SCADA\Engine\App\Setting\install-db.ps1
)
echo.

echo [6/8] Generating internal HTTPS certificate (CA + server cert)...
echo.
if exist "C:\SCADA\Web\App\certs\scada-web.pfx" (
    echo [INFO] Server certificate already exists - reusing.
    echo        Re-run generate-https-cert.ps1 manually if the server IP changed.
) else (
    powershell -ExecutionPolicy Bypass -File "C:\SCADA\Web\App\certs\generate-https-cert.ps1"
    if errorlevel 1 (
        echo [WARN] Certificate generation failed - Web will fall back to HTTP-only on 5038.
        echo        Re-run manually: powershell -ExecutionPolicy Bypass -File C:\SCADA\Web\App\certs\generate-https-cert.ps1
    )
)
:: Assemble client CA installer bundle for easy distribution to each browsing PC
if exist "C:\SCADA\Web\App\certs\ScadaEngine-CA.crt" (
    if not exist "C:\SCADA\ClientCA_Installer" mkdir "C:\SCADA\ClientCA_Installer"
    copy /Y "C:\SCADA\Web\App\certs\ScadaEngine-CA.crt"       "C:\SCADA\ClientCA_Installer\" >nul
    copy /Y "C:\SCADA\Web\App\certs\install-ca-on-client.ps1" "C:\SCADA\ClientCA_Installer\" >nul
    echo [OK] Client CA installer ready: C:\SCADA\ClientCA_Installer
    echo      Copy that folder to each client PC, run install-ca-on-client.ps1 as Admin.
)
echo.

echo [7/8] Opening firewall ports 5038 (HTTP) and 7189 (HTTPS)...
netsh advfirewall firewall add rule name="ScadaEngine Web" dir=in action=allow protocol=TCP localport=5038
netsh advfirewall firewall add rule name="ScadaEngine Web HTTPS" dir=in action=allow protocol=TCP localport=7189
echo.

echo [8/8] Starting services...
net start ScadaEngineLicense
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
echo [HTTPS] HTTPS is now ENABLED. http://server-ip:5038 auto-redirects to https://server-ip:7189.
echo   - Until each client installs the CA, browsers show a certificate warning (click-through still works).
echo   - To remove the warning: copy the C:\SCADA\ClientCA_Installer folder to each client PC,
echo     run install-ca-on-client.ps1 as Administrator, then fully restart the browser.
echo   - Connect using the server IP printed in the certs output above (must match the certificate SAN).
echo   - To disable HTTPS (HTTP-only on 5038): delete C:\SCADA\Web\App\certs\scada-web.pfx and restart ScadaWebService.
echo.
echo ========================================
echo   Service Status  (RUNNING = OK)
echo ========================================
echo [Engine ] ScadaEngineService
sc query ScadaEngineService  | find "STATE"
echo [Web    ] ScadaWebService
sc query ScadaWebService     | find "STATE"
echo [License] ScadaEngineLicense
sc query ScadaEngineLicense  | find "STATE"
echo.
echo If a line is blank or shows STOPPED, that service did not start -
echo check its log and re-run this installer. (License also needs the HASP dongle.)
echo.
echo ----------------------------------------
echo This window stays open so you can read the result above.
echo Press any key to close it.
echo ----------------------------------------
pause
"@ | Set-Content -Path "$ReleasePath\Install.bat" -Encoding ASCII

Write-Host "  Install.bat created" -ForegroundColor Green

# ModbusServer standalone installer — deliberately NOT part of Install.bat（安裝解耦，見 docs/plans）
# 單引號 herestring：內容含 PowerShell 變數（$c/$p/$j），不可被 build 腳本插值；bat 內容須全 ASCII（Set-Content -Encoding ASCII）
@'
@echo off
setlocal
echo ========================================
echo   SCADA Modbus Gateway Installer
echo   (standalone - NOT installed by Install.bat)
echo ========================================
echo.

net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] Please run as Administrator
    pause
    exit /b 1
)

set "_APP=C:\SCADA\ModbusServer\App"
set "_BACKUP=C:\SCADA\_ModbusGatewayBackup"
set "_SVC=ScadaModbusGatewayService"
set "_IS_UPGRADE=0"

:: -- Detect upgrade: backup site config + AddressMap.json (address assignments must survive) --
if exist "%_APP%\Setting" (
    set "_IS_UPGRADE=1"
    echo [INFO] Detected existing installation - preserving site config + AddressMap.json...
    if not exist "%_BACKUP%" mkdir "%_BACKUP%"
    xcopy /E /I /Y "%_APP%\Setting"     "%_BACKUP%\Setting"     >nul
    xcopy /E /I /Y "%_APP%\MqttSetting" "%_BACKUP%\MqttSetting" >nul
    if exist "%_APP%\AddressMap.json" copy /Y "%_APP%\AddressMap.json" "%_BACKUP%\AddressMap.json" >nul
)

echo [1/6] Stopping existing service (if any)...
net stop %_SVC% >nul 2>&1

echo [2/6] Copying files...
xcopy /E /I /Y "%~dp0ModbusServer\App" "%_APP%" >nul

:: -- Restore site config --
if "%_IS_UPGRADE%"=="1" (
    echo [INFO] Restoring site config...
    xcopy /E /I /Y "%_BACKUP%\Setting"     "%_APP%\Setting"     >nul
    xcopy /E /I /Y "%_BACKUP%\MqttSetting" "%_APP%\MqttSetting" >nul
    if exist "%_BACKUP%\AddressMap.json" copy /Y "%_BACKUP%\AddressMap.json" "%_APP%\AddressMap.json" >nul
    rmdir /S /Q "%_BACKUP%"
)

:: -- Read configured ports from the (site) setting file --
for /f "usebackq" %%p in (`powershell -NoProfile -Command "(Get-Content '%_APP%\Setting\ModbusServerSetting.json' -Raw | ConvertFrom-Json).ModbusListenPort"`) do set MODBUS_PORT=%%p
for /f "usebackq" %%p in (`powershell -NoProfile -Command "(Get-Content '%_APP%\Setting\ModbusServerSetting.json' -Raw | ConvertFrom-Json).WebPort"`) do set WEB_PORT=%%p
if "%MODBUS_PORT%"=="" set MODBUS_PORT=502
if "%WEB_PORT%"=="" set WEB_PORT=5041

echo [3/6] Checking port availability (Modbus %MODBUS_PORT% / Web %WEB_PORT%)...

:CHECK_MODBUS
call :PORTCHECK %MODBUS_PORT%
if "%PORT_FREE%"=="1" goto MODBUS_OK
echo.
echo [WARN] Modbus TCP port %MODBUS_PORT% is already in use by: %PORT_OWNER%
set "NEWPORT="
set /p NEWPORT="Enter alternative Modbus port (suggest 1502, Enter = re-test %MODBUS_PORT%): "
if not "%NEWPORT%"=="" set MODBUS_PORT=%NEWPORT%
goto CHECK_MODBUS
:MODBUS_OK
echo   Modbus port %MODBUS_PORT% OK

:CHECK_WEB
call :PORTCHECK %WEB_PORT%
if "%PORT_FREE%"=="1" goto WEB_OK
echo.
echo [WARN] Web port %WEB_PORT% is already in use by: %PORT_OWNER%
set "NEWPORT="
set /p NEWPORT="Enter alternative Web port (suggest 5042, Enter = re-test %WEB_PORT%): "
if not "%NEWPORT%"=="" set WEB_PORT=%NEWPORT%
goto CHECK_WEB
:WEB_OK
echo   Web port %WEB_PORT% OK

:: -- Persist selected ports into ModbusServerSetting.json --
powershell -NoProfile -Command "$f='%_APP%\Setting\ModbusServerSetting.json'; $j=Get-Content $f -Raw | ConvertFrom-Json; $j.ModbusListenPort=[int]%MODBUS_PORT%; $j.WebPort=[int]%WEB_PORT%; $j | ConvertTo-Json -Depth 10 | Set-Content $f -Encoding UTF8"

echo [4/6] Registering Windows service...
sc query %_SVC% >nul 2>&1
if %errorLevel% NEQ 0 (
    sc create %_SVC% binPath= "\"%_APP%\ScadaEngine.ModbusServer.exe\"" DisplayName= "\"SCADA Modbus Gateway Service\"" start= auto
    sc description %_SVC% "SCADA realtime data Modbus TCP gateway (FC4 input registers, float32)"
    sc failure %_SVC% reset= 86400 actions= restart/5000/restart/10000/restart/30000
)

echo [5/6] Opening firewall for ports %MODBUS_PORT% (Modbus) and %WEB_PORT% (Web)...
netsh advfirewall firewall delete rule name="SCADA Modbus Gateway TCP" >nul 2>&1
netsh advfirewall firewall delete rule name="SCADA Modbus Gateway Web" >nul 2>&1
netsh advfirewall firewall add rule name="SCADA Modbus Gateway TCP" dir=in action=allow protocol=TCP localport=%MODBUS_PORT%
netsh advfirewall firewall add rule name="SCADA Modbus Gateway Web" dir=in action=allow protocol=TCP localport=%WEB_PORT%

echo [6/6] Starting service...
net start %_SVC%
echo.
echo ========================================
echo   Modbus Gateway installed!
echo   Modbus TCP : port %MODBUS_PORT% (FC4, float32)
echo   Address map: http://localhost:%WEB_PORT%
echo ========================================
echo.
if "%_IS_UPGRADE%"=="1" (
    echo [OK] Site config + AddressMap.json were PRESERVED.
) else (
    echo [NOTE] First install - edit config as needed:
    echo   %_APP%\Setting\ModbusServerSetting.json  ^(ports / word order / segments^)
    echo   %_APP%\Setting\dbSetting.json            ^(SQL connection^)
    echo   %_APP%\MqttSetting\MqttSetting.json      ^(MQTT broker^)
)
echo.
pause
exit /b 0

:: -- Subroutine: check if TCP port %1 is free --
:: Occupation by our own service (ScadaEngine.ModbusServer) is treated as free
:: (upgrade scenario - the service was just stopped / will restart with the same port)
:PORTCHECK
set PORT_FREE=1
set "PORT_OWNER="
for /f "usebackq tokens=*" %%o in (`powershell -NoProfile -Command "$c=Get-NetTCPConnection -State Listen -LocalPort %1 -ErrorAction SilentlyContinue | Select-Object -First 1; if($c){$p=Get-Process -Id $c.OwningProcess -ErrorAction SilentlyContinue; if($p -and $p.ProcessName -ne 'ScadaEngine.ModbusServer'){Write-Output ($p.ProcessName + ' (PID ' + $c.OwningProcess + ')')} elseif(-not $p){Write-Output ('PID ' + $c.OwningProcess)}}"`) do (
    set PORT_FREE=0
    set "PORT_OWNER=%%o"
)
exit /b
'@ | Set-Content -Path "$ReleasePath\InstallModbusServer.bat" -Encoding ASCII

Write-Host "  InstallModbusServer.bat created (standalone gateway installer)" -ForegroundColor Green

# ──────────────────────────────────────
# 6. Summary
# ──────────────────────────────────────
Write-Host "[6/6] Calculating size..." -ForegroundColor Yellow
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
Write-Host "  +-- Install.bat              <- On-site: right-click Run as Admin (Engine + Web only)"
Write-Host "  +-- InstallModbusServer.bat  <- Optional Modbus gateway (standalone, run separately)"
Write-Host "  +-- Engine\"
Write-Host "  |   +-- App\                 <- Engine executable + configs"
Write-Host "  |   +-- QuickDeploy.bat      <- Engine service manager"
Write-Host "  |   +-- DeployService.ps1"
Write-Host "  +-- Web\"
Write-Host "  |   +-- App\                 <- Web executable + wwwroot + configs"
Write-Host "  |   +-- QuickDeploy.bat      <- Web service manager"
Write-Host "  |   +-- DeployWebService.ps1"
Write-Host "  +-- LicenseBridge\"
Write-Host "  |   +-- App\                 <- net48 HASP bridge (installed by Install.bat; needs USB dongle)"
Write-Host "  +-- ModbusServer\"
Write-Host "      +-- App\                 <- Modbus TCP gateway (FC4 float32) + browse page"
Write-Host "      +-- QuickDeploy.bat      <- Gateway service manager"
Write-Host "      +-- DeployModbusServer.ps1"
Write-Host ""
Write-Host "On-site deployment:"
Write-Host "  1. Copy entire folder to USB"
Write-Host "  2. On target server: right-click Install.bat -> Run as Administrator"
Write-Host "  3. Done! Open http://server-ip:5038"
Write-Host ""
