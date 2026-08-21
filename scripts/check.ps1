#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

# CLAUDE.md documented four commands you had to remember in the right order, and the order is
# load-bearing: the E2E suite has no build step, so Playwright silently tests the previous build
# of the frontend unless Angular is built first. Nothing looks wrong when that happens -the
# suite does exactly what it should, against the wrong input. CI says the same thing about the
# same step. This script is that order, once.
$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'src\Todo.Web'

# dotnet tool restore reads its manifest from the current directory, and npm resolves the
# workspace from it too -so everything below runs from the repo root rather than from wherever
# the caller happened to stand.
Set-Location $root

$steps = @(
    @{ Name = 'Angular build'; Hint = 'scripts\build-web.ps1' }
    @{ Name = '.NET tests';    Hint = 'dotnet test Todo.sln' }
    @{ Name = 'Vitest';          Hint = 'npm.cmd run test --prefix src\Todo.Web -- --watch=false' }
)

function Start-Step($index) {
    Write-Host ''
    Write-Host ("[{0}/{1}] {2}" -f ($index + 1), $steps.Count, $steps[$index].Name) -ForegroundColor Cyan
}

function Stop-OnFailure($index, $code) {
    if ($code -eq 0) { return }

    Write-Host ''
    Write-Host ("{0} failed (exit code {1})." -f $steps[$index].Name, $code) -ForegroundColor Red
    # Every script here is written in English, which also settles an encoding problem it was written
    # around before: PowerShell 5.1 decodes a .ps1 without a BOM as the ANSI codepage, so a Danish
    # letter reaches the reader as mojibake. That is not theoretical - run-app.ps1 held Danish
    # comments and printed "gAyr" for "gor" when read back. English is plain ASCII, so the question
    # stops being one.
    Write-Host ("Run the step on its own for the full output: {0}" -f $steps[$index].Hint) -ForegroundColor Red
    exit $code
}

# Angular first. The formatting guard and the line ending guard both run inside dotnet test
# below, so neither needs a step of its own -verified rather than assumed: prettier is called
# by FrontendFormattingTests and git ls-files by LineEndingTests, both in Todo.Api.Tests.
Start-Step 0
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-web.ps1')
Stop-OnFailure 0 $LASTEXITCODE

Start-Step 1
& dotnet test (Join-Path $root 'Todo.sln')
Stop-OnFailure 1 $LASTEXITCODE

Start-Step 2
& npm.cmd run test --prefix $web -- --watch=false
Stop-OnFailure 2 $LASTEXITCODE

Write-Host ''
Write-Host 'All green.' -ForegroundColor Green
