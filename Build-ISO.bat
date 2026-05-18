@echo off
:: Check for administrative privileges
net session >nul 2>&1
if %errorLevel% == 0 (
    goto :runScript
) else (
    echo [Apex Recovery System] Requesting Administrator privileges to mount WinPE...
    powershell -Command "Start-Process '%~dpnx0' -Verb RunAs"
    exit /b
)

:runScript
cd /d "%~dp0"
title Apex WinPE ISO Builder
cls
echo ===================================================
echo   APEX RECOVERY SYSTEM - ISO BUILD AUTOMATION
echo ===================================================
echo.
echo Launching Build-WinPE-Desktop.ps1 with elevated privileges...
echo.
powershell -NoProfile -ExecutionPolicy Bypass -File "Build-WinPE-Desktop.ps1"
echo.
echo ===================================================
echo Done! Press any key to exit.
pause >nul
