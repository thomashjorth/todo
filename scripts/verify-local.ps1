#Requires -Version 5.1
param(
    # 'start' (the default) brings everything up; 'stop' takes the container down and gives the helper
    # back the server it had.
    [ValidateSet('start', 'stop')]
    [string] $Action = 'start',

    # Passed on to try-nas.ps1: the image from this working tree, and your own tasks on the first start.
    [switch] $Build,
    [switch] $WithMyData
)
$ErrorActionPreference = 'Stop'

# Everything the NAS setup needs on this PC, in one go: Ollama, the helper, the container from
# try-nas.ps1 - and the pairing between the helper and the container, which is the step that is easy to
# miss. Without it the app runs, but the AI, Jira and Azure DevOps are simply not there.

. (Join-Path $PSScriptRoot 'helper-api.ps1')

$root = Split-Path -Parent $PSScriptRoot
$helper = 'http://127.0.0.1:47600'
$server = 'http://127.0.0.1:8088'
$folder = Join-Path $env:LOCALAPPDATA 'TodoNasTry'
$previousServer = Join-Path $folder 'previous-server.txt'

function Test-Listening([int] $port) {
    $null -ne (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue)
}

function Wait-Port([int] $port, [int] $seconds) {
    foreach ($attempt in 1..$seconds) {
        if (Test-Listening $port) { return $true }
        Start-Sleep -Seconds 1
    }
    return $false
}

# The hash goes where Todo.cmd and try-nas.ps1 both read it, so one pairing serves both.
function Register-NewKey {
    Write-Host 'Pairing the helper with a new key...' -ForegroundColor Cyan
    $hash = New-HelperKey $helper
    [Environment]::SetEnvironmentVariable('Helper__KeyHash', $hash, 'User')
    $env:Helper__KeyHash = $hash
}

function Start-Container {
    & (Join-Path $PSScriptRoot 'try-nas.ps1') -NoOpen -Build:$Build -WithMyData:$WithMyData
}

if ($Action -eq 'stop') {
    & (Join-Path $PSScriptRoot 'try-nas.ps1') stop

    if ((Test-Path $previousServer) -and (Test-Listening 47600)) {
        $previous = "$(Get-Content $previousServer)".Trim()
        Set-HelperServer $helper $previous | Out-Null
        Remove-Item $previousServer
        $shown = if ($previous) { $previous } else { 'no server' }
        Write-Host "The helper is back on $shown." -ForegroundColor Green
    }
    exit 0
}

# Ollama: its tray app starts the server, as it does when Windows starts it.
if (-not (Test-Listening 11434)) {
    $ollama = Get-Command 'ollama' -ErrorAction SilentlyContinue
    if ($ollama) {
        Write-Host 'Starting Ollama...' -ForegroundColor Cyan
        Start-Process (Join-Path (Split-Path $ollama.Source) 'ollama app.exe')
        if (-not (Wait-Port 11434 30)) { Write-Host 'Ollama did not start - the AI will be missing.' -ForegroundColor Yellow }
    } else {
        Write-Host 'Ollama is not installed - the AI will be missing.' -ForegroundColor Yellow
    }
}

# The helper in a window of its own, which is how you stop it: Helper.cmd builds what is out of date first,
# so the first start can take a few minutes.
if (-not (Test-Listening 47600)) {
    Write-Host 'Starting the helper in its own window...' -ForegroundColor Cyan
    Start-Process (Join-Path $root 'Helper.cmd')
    if (-not (Wait-Port 47600 300)) { throw "The helper did not start within five minutes. Its window says why." }
}
$status = Get-HelperStatus $helper

# Remembered once, so 'stop' gives the helper back what it had before - Todo.cmd's server, typically.
New-Item -ItemType Directory -Force $folder | Out-Null
if ($status.serverUrl -ne $server -and -not (Test-Path $previousServer)) {
    Set-Content -Path $previousServer -Value "$($status.serverUrl)" -Encoding ascii
}

$paired = $false
if (-not $status.hasKey -or -not [Environment]::GetEnvironmentVariable('Helper__KeyHash', 'User')) {
    Register-NewKey
    $paired = $true
}
Set-HelperServer $helper $server | Out-Null

Start-Container

# A key the container does not know looks like a tunnel that keeps dropping, not like a refusal. So when
# it does not connect on a key from an earlier run, the key is replaced and the container started again
# with the new hash - once.
Set-HelperServer $helper $server | Out-Null
$status = Wait-HelperTunnel $helper 20
if ($status.tunnel -ne 'Connected' -and $status.tunnel -ne 'Refused' -and -not $paired) {
    Register-NewKey
    Start-Container
    Set-HelperServer $helper $server | Out-Null
    $status = Wait-HelperTunnel $helper 30
}
if ($status.tunnel -eq 'Refused') { throw "The server refused the helper: $($status.refusal)" }
if ($status.tunnel -ne 'Connected') { throw "The helper did not get through to $server (the tunnel is $($status.tunnel))." }

Start-Process 'msedge.exe' -ArgumentList @("--app=$server/", '--window-size=480,1000')

function Write-Source([string] $name, $source) {
    if ($source.hasToken) {
        Write-Host "  $name`: token held, $($source.baseUrl)" -ForegroundColor Green
    } else {
        Write-Host "  $name`: no token - add it under Indstillinger -> Forbindelser" -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host "Everything runs: the app on $server with your own password, the helper connected to it." -ForegroundColor Green
$ai = if (Test-Listening 11434) { 'running' } else { 'not running' }
Write-Host "  Ollama: $ai"
Write-Source 'Jira' $status.jira
Write-Source 'Azure DevOps' $status.ado
Write-Host 'VerifyLocal.cmd stop takes the container down, puts Docker back and gives the helper its old server.' -ForegroundColor Cyan
