@echo off
rem Double-click to start everything for a local check: Ollama, the helper and the NAS's container, paired.
rem "VerifyLocal.cmd stop" takes it down again.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\verify-local.ps1" %*
if errorlevel 1 (
    echo.
    echo The local setup did not start. Close the window when you have read the message.
)
pause >nul
