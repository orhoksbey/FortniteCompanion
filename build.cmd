@echo off
chcp 65001 >nul
title Fortnite Companion derleniyor
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build.ps1" %*
if errorlevel 1 (
  echo.
  echo Derleme basarisiz. Yukaridaki mesaja bakin.
  pause
  exit /b 1
)
explorer "%~dp0dist"
pause
