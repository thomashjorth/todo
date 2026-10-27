@echo off
rem Double-click to run the NAS's container on this PC. "TryNas.cmd stop" takes it down again.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\try-nas.ps1" %*
if errorlevel 1 (
    echo.
    echo The NAS setup did not start. Close the window when you have read the message.
)
pause >nul
