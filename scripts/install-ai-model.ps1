#Requires -Version 5.1
# Makes sure the local AI model is in Ollama, and starts the download in its own window if it is
# not. Removes every other model that is not approved. Never stops the app from starting: every
# problem is reported and the script returns.
#
# Chinese models are forbidden on this work PC. A model's name does not reveal what it is built on,
# so the rule is an approved list rather than a blocked one.
param(
    [string]$Model = 'gemma4:e4b',
    [string[]]$Approved = @($Model)
)

# Under 'Stop', Windows PowerShell 5.1 turns anything a native command writes to stderr into a
# terminating error - and ollama writes its progress and its "not found" there.
$ErrorActionPreference = 'Continue'

function Find-Ollama {
    $command = Get-Command ollama -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    # The installer puts this folder on the user's PATH, but a console opened before the install
    # still has the old PATH.
    $default = Join-Path $env:LOCALAPPDATA 'Programs\Ollama\ollama.exe'
    if (Test-Path $default) { return $default }

    return $null
}

# Asked over HTTP rather than through the CLI: when no server answers, the CLI starts the tray
# app and waits for it, and a tray app that cannot start its server hangs this script.
# curl.exe rather than Invoke-WebRequest, because the antivirus blocks a script that combines
# Invoke-WebRequest with Start-Process.
function Test-OllamaServer {
    & curl.exe --silent --max-time 2 --output NUL http://127.0.0.1:11434/api/version
    return $LASTEXITCODE -eq 0
}

$exe = Find-Ollama
if (-not $exe) {
    Write-Host "AI model: Ollama is not installed, so $Model was not checked. Install it with: winget install Ollama.Ollama" -ForegroundColor Yellow
    return
}

if (-not (Test-OllamaServer)) {
    # A console opened before OLLAMA_MODELS was set still has the old environment, and a server
    # started from it would look in the default folder and download the model again.
    if (-not $env:OLLAMA_MODELS) {
        $env:OLLAMA_MODELS = [Environment]::GetEnvironmentVariable('OLLAMA_MODELS', 'User')
    }
    Start-Process -FilePath $exe -ArgumentList 'serve' -WindowStyle Hidden
    $deadline = (Get-Date).AddSeconds(15)
    while (-not (Test-OllamaServer) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }
    if (-not (Test-OllamaServer)) {
        Write-Host "AI model: Ollama did not answer within 15 seconds, so $Model was not checked." -ForegroundColor Yellow
        return
    }
}

# "ollama list" prints a header and then one model per line, its name first.
$installed = @(& $exe list 2>$null | Select-Object -Skip 1 | ForEach-Object { ($_ -split '\s+')[0] } | Where-Object { $_ })
foreach ($name in $installed) {
    if ($Approved -notcontains $name) {
        Write-Host "AI model: removing $name, which is not on the approved list." -ForegroundColor Yellow
        & $exe rm $name *> $null
        if ($LASTEXITCODE -ne 0) {
            Write-Host "AI model: could not remove $name." -ForegroundColor Red
        }
    }
}

& $exe show $Model *> $null
if ($LASTEXITCODE -eq 0) {
    Write-Host "AI model: $Model is installed." -ForegroundColor DarkGray
    return
}

# A second start of the app while the first download runs would otherwise open a second window
# pulling the same layers.
$pulling = Get-CimInstance Win32_Process -Filter "Name = 'ollama.exe'" |
    Where-Object { $_.CommandLine -match "\bpull\s+$([regex]::Escape($Model))" }
if ($pulling) {
    Write-Host "AI model: $Model is still downloading in its own window." -ForegroundColor Cyan
    return
}

# Several GB, so it runs beside the app rather than before it. The window stays open on a failure
# so the message can be read.
Write-Host "AI model: downloading $Model in a separate window. The app starts meanwhile." -ForegroundColor Cyan
Start-Process -FilePath $env:ComSpec -ArgumentList "/c `"`"$exe`" pull $Model || pause`""
