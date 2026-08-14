@echo off
rem Dobbeltklik for at starte appen. Bygger Angular hvis den er foraeldet.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\run-app.ps1" %*
if errorlevel 1 (
    echo.
    echo Todo stoppede med en fejl. Luk vinduet naar du har laest beskeden.
    pause >nul
)
