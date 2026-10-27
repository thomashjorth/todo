@echo off
rem Double-click to export the database for the NAS, without its tokens.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\export-data.ps1" %*
if errorlevel 1 (
    echo.
    echo Nothing was exported. Close the window when you have read the message.
)
pause >nul
