#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

# Starts the helper on this PC: it holds the Jira and Azure DevOps tokens, opens the tunnel out to the
# server, and serves the Connections page the app's settings show in a frame. It carries the app's
# bundle in its exe, so Angular is built first when it is out of date.

$root = Split-Path -Parent $PSScriptRoot
$helperProject = Join-Path $root 'src\Todo.Helper'

# Fixed, because the app's settings page frames the helper's page from this address.
$port = 47600
$page = "http://127.0.0.1:$port/connections"

# Already running: open its page rather than start a second one that fails on the port - and rather
# than build, because the running helper locks the files a build would write.
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
    Write-Host "The helper is already running - opening $page." -ForegroundColor Cyan
    Start-Process $page
    exit 0
}

& (Join-Path $PSScriptRoot 'ensure-web.ps1')

Write-Host 'Building the helper...' -ForegroundColor Cyan
& dotnet build $helperProject -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host 'Starting the helper...' -ForegroundColor Cyan
$helper = Start-Process -FilePath 'dotnet' -NoNewWindow -PassThru -ArgumentList @(
    'run', '--project', $helperProject, '-c', 'Release', '--no-build', '--no-launch-profile')

# curl.exe rather than Invoke-WebRequest: AMSI blocks Start-Process combined with Invoke-WebRequest
# on this machine (CLAUDE.md, "Maskinen").
$ready = $false
foreach ($attempt in 1..60) {
    Start-Sleep -Milliseconds 500
    if ($helper.HasExited) { exit $helper.ExitCode }
    $code = & curl.exe -s -o NUL -w '%{http_code}' $page
    if ($code -eq '200') { $ready = $true; break }
}
if (-not $ready) { throw "The helper did not answer on $page within thirty seconds." }

Write-Host "The helper runs on $page - set it up there or on the app's settings page." -ForegroundColor Cyan
Write-Host 'Close this window to stop it.' -ForegroundColor Cyan

$helper.WaitForExit()
exit $helper.ExitCode
