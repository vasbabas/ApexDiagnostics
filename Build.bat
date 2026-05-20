@echo off
title Apex Diagnostics Builder
color 0b
cls

echo ==========================================================
echo    APEX HARDWARE SUITE - AUTOMATED PRODUCTION COMPILER
echo ==========================================================
echo.
echo [INFO] Preparing workspace...
echo.

:: Recreate clean Build directory in the root
if exist "%~dp0Build" (
    echo [INFO] Cleaning old build files...
    rmdir /s /q "%~dp0Build"
)
mkdir "%~dp0Build"

echo.
echo ==========================================================
echo  1. COMPILING MAIN DIAGNOSTIC SYSTEM (ApexDiagnostics)
echo ==========================================================
echo.
dotnet publish "%~dp0ApexDiagnostics\ApexDiagnostics.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0Build"
if %errorlevel% neq 0 (
    color 0c
    echo.
    echo [ERROR] Failed to compile ApexDiagnostics!
    goto :errorExit
)

echo.
echo ==========================================================
echo  2. COMPILING BOOT SYSTEM ENVIRONMENT (ApexShell)
echo ==========================================================
echo.
dotnet publish "%~dp0ApexShell\ApexShell.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0Build"
if %errorlevel% neq 0 (
    color 0c
    echo.
    echo [ERROR] Failed to compile ApexShell!
    goto :errorExit
)

:: Copy Explorer++.exe to Build directory if it exists in the root
if exist "%~dp0Explorer++.exe" (
    echo.
    echo [INFO] Copying Explorer++.exe to Build folder...
    copy /y "%~dp0Explorer++.exe" "%~dp0Build\Explorer++.exe" >nul
) else (
    echo.
    echo [WARNING] Explorer++.exe not found in root folder! WinPE will lack a file manager.
)

:: Final Verification Check
if not exist "%~dp0Build\ApexDiagnostics.exe" (
    color 0c
    echo [ERROR] Output file ApexDiagnostics.exe not found in Build folder!
    goto :errorExit
)
if not exist "%~dp0Build\ApexShell.exe" (
    color 0c
    echo [ERROR] Output file ApexShell.exe not found in Build folder!
    goto :errorExit
)

:: Clean up unwanted extra .pdb debug symbol files from Build folder to keep it 100% pristine
del /f /q "%~dp0Build\*.pdb" >nul 2>&1

color 0a
cls
echo ==========================================================
echo    🎉 COMPILATION COMPLETED SUCCESSFULLY!
echo ==========================================================
echo.
echo [SUCCESS] Your production-grade, highly optimized single-file
echo           executables have been generated in the root folder:
echo.
echo           📁 %~dp0Build
echo.
echo           - ApexDiagnostics.exe  (WinPE-Compatible, Single File)
echo           - ApexShell.exe        (Shell Host Bootloader, Single File)
echo.
echo [NEXT STEP] Copy the compiled files from the 'Build' folder
echo             into your Win10XPE project/plugin directory
echo             to package and run them in your custom ISO.
echo.
echo ==========================================================
pause
exit /b

:errorExit
echo.
echo [FATAL] Build process failed. Check the errors above.
echo.
pause
exit /b
