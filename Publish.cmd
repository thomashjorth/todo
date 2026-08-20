@echo off
rem Dobbeltklik for at bygge appen som en exe og proeve den bagefter.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\publish.ps1" %*
if errorlevel 1 (
    echo.
    echo Publish fejlede. Luk vinduet naar du har laest beskeden.
    pause >nul
) else (
    echo.
    echo Luk vinduet naar du har laest stien.
    pause >nul
)
