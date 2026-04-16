@echo off
:: 用 bat 包裝 PowerShell 腳本，繞過執行原則限制
:: 雙擊即可執行，不需手動設定 ExecutionPolicy
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0BuildRelease.ps1"
pause
