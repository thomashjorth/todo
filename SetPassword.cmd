@echo off
rem Double-click to set the app's password. Stores a hash, never the password itself.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\set-password.ps1" %*
if errorlevel 1 (
    echo.
    echo The password was not set. Close the window when you have read the message.
)
pause >nul
