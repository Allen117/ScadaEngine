@echo off
echo ========================================
echo        SCADA Engine Service Manager
echo ========================================
echo.

REM Check administrator privileges
net session >nul 2>&1
if %errorLevel% NEQ 0 (
    echo [ERROR] This script requires administrator privileges
    echo Please run as administrator
    echo.
    pause
    exit /b 1
)

echo [INFO] Administrator check passed
echo.

:main
echo Choose an option:
echo 1. Install Service
echo 2. Uninstall Service
echo 3. Start Service
echo 4. Stop Service
echo 5. Restart Service
echo 6. Check Status
echo 7. Exit
echo.
set /p choice="Enter your choice (1-7): "

if "%choice%"=="1" goto INSTALL
if "%choice%"=="2" goto UNINSTALL
if "%choice%"=="3" goto START
if "%choice%"=="4" goto STOP
if "%choice%"=="5" goto RESTART
if "%choice%"=="6" goto STATUS
if "%choice%"=="7" goto EXIT

echo Invalid choice. Please try again.
echo.
goto main

:INSTALL
echo.
echo Installing SCADA Engine Service...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployService.ps1" -Action install
goto CONTINUE

:UNINSTALL
echo.
echo Uninstalling SCADA Engine Service...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployService.ps1" -Action uninstall
goto CONTINUE

:START
echo.
echo Starting service...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployService.ps1" -Action start
goto CONTINUE

:STOP
echo.
echo Stopping service...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployService.ps1" -Action stop
goto CONTINUE

:RESTART
echo.
echo Restarting service...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployService.ps1" -Action restart
goto CONTINUE

:STATUS
echo.
echo Checking service status...
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployService.ps1" -Action status
goto CONTINUE

:CONTINUE
echo.
echo Press any key to return to main menu...
pause > nul
cls
goto main

:EXIT
echo Exiting...
exit /b 0