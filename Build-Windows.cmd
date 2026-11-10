@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Tools\Build-Windows.ps1" %*
set "build_exit=%errorlevel%"
if not "%build_exit%"=="0" echo Build failed. Read the error and build log above.
pause
exit /b %build_exit%
