@echo off
rem Double-click to build the app as an exe and probe it afterwards.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" %*
if errorlevel 1 (
    echo.
    echo Publish failed. Close the window when you have read the message.
    pause >nul
) else (
    echo.
    echo Close the window when you have read the path.
    pause >nul
)
