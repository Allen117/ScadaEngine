@echo off
setlocal
echo ========================================
echo   SCADA Quick Deploy ALL
echo   (BuildRelease + Install in one step)
echo ========================================
echo.

net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] Please run as Administrator
    pause
    exit /b 1
)

echo [1/2] Building release package (BuildRelease.ps1)...
echo.
powershell -ExecutionPolicy Bypass -File "%~dp0BuildRelease.ps1"
if %errorLevel% NEQ 0 (
    echo.
    echo [ERROR] Build failed - nothing was deployed.
    pause
    exit /b 1
)

:: Find newest release folder (names sort chronologically: SCADA_Release_yyyyMMdd_HHmm)
set "_REL="
for /f "delims=" %%d in ('dir /b /ad /o-n "%~dp0Release\SCADA_Release_*" 2^>nul') do (
    if not defined _REL set "_REL=%%d"
)
if not defined _REL (
    echo [ERROR] No release folder found under Release\
    pause
    exit /b 1
)

echo.
echo [2/2] Installing Engine + Web from Release\%_REL%
echo        (upgrade preserves site configs: Modbus, Setting, MqttSetting, DBPoint)
echo.
call "%~dp0Release\%_REL%\Install.bat"

echo.
echo [NOTE] Modbus gateway is NOT touched by this script.
echo        Install/update it separately: Release\%_REL%\InstallModbusServer.bat
echo.
echo QuickDeployAll finished.
echo Old release folders under Release\ are kept for rollback - delete manually when unneeded.
pause
