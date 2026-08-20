@echo off
rem Dobbeltklik for at koere hele suiten i den rigtige raekkefoelge.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\check.ps1" %*
if errorlevel 1 (
    echo.
    echo Et trin fejlede. Luk vinduet naar du har laest beskeden.
    pause >nul
)
