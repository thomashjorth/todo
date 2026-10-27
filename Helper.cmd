@echo off
rem Double-click to start the helper, which holds the tokens and opens the tunnel to the server.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-helper.ps1" %*
if errorlevel 1 (
    echo.
    echo The helper stopped with an error. Close the window when you have read the message.
    pause >nul
)
