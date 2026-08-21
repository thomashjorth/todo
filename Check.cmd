@echo off
rem Double-click to run the whole suite in the order that matters.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check.ps1" %*
if errorlevel 1 (
    echo.
    echo A step failed. Close the window when you have read the message.
    pause >nul
)
