@echo off
rem Double-click to store the hash of the helper's key for Todo.cmd.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\set-helper-key.ps1" %*
if errorlevel 1 (
    echo.
    echo The key was not set. Close the window when you have read the message.
)
pause >nul
