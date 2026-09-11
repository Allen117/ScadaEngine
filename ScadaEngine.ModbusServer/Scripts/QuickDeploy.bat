@echo off
echo ========================================
echo    SCADA Modbus Gateway Service Manager
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
echo 6. Update Service (Stop + Build + Deploy + Start)
echo 7. Check Status
echo 8. Show Logs
echo 9. Exit
echo.
set /p choice="Enter your choice (1-9): "

if "%choice%"=="1" goto INSTALL
if "%choice%"=="2" goto UNINSTALL
if "%choice%"=="3" goto START
if "%choice%"=="4" goto STOP
if "%choice%"=="5" goto RESTART
if "%choice%"=="6" goto UPDATE
if "%choice%"=="7" goto STATUS
if "%choice%"=="8" goto LOGS
if "%choice%"=="9" goto EXIT

echo Invalid choice. Please try again.
echo.
goto main

:INSTALL
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action install
goto CONTINUE

:UNINSTALL
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action uninstall
goto CONTINUE

:START
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action start
goto CONTINUE

:STOP
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action stop
goto CONTINUE

:RESTART
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action restart
goto CONTINUE

:UPDATE
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action update
goto CONTINUE

:STATUS
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action status
goto CONTINUE

:LOGS
powershell.exe -ExecutionPolicy Bypass -File "%~dp0DeployModbusServer.ps1" -Action logs
goto CONTINUE

:CONTINUE
echo.
echo Press any key to return to main menu...
pause > nul
cls
goto main

:EXIT
exit /b 0
