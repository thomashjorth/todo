@echo off
rem Double-click to start the app. Builds Angular first if it is out of date.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-app.ps1" %*
if errorlevel 1 (
    echo.
    echo Todo stopped with an error. Close the window when you have read the message.
    pause >nul
)
