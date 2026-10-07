@echo off
rem 더블클릭으로 release.ps1 실행 (PowerShell 실행 정책 때문에 .ps1을 바로 못 여는 경우용)
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0release.ps1" %*
echo.
pause
