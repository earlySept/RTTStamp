@echo off
rem 重新生成 image\icon\app.ico（仅在设计图标改动后需要运行）
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0make_ico.ps1"
pause
