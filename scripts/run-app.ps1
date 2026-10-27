#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot

# Angular, built first if the sources are newer than wwwroot. The helper serves the same bundle.
& (Join-Path $PSScriptRoot 'ensure-web.ps1')

try {
    & (Join-Path $PSScriptRoot 'install-ai-model.ps1')
} catch {
    Write-Host "AI model: the check failed and was skipped - $($_.Exception.Message)" -ForegroundColor Yellow
}

# A fixed port, because the app window and the browser's storage belong to an origin, and a random
# port would make a new one every time. 5180 is clear of the usual development servers; unmeasured
# against whatever else runs on this machine.
$port = 5180
$url = "http://127.0.0.1:$port/"
$window = @("--app=$url", '--window-size=480,1000')

# Already running: the user double-clicked again. The window opens on the host that is there,
# rather than a second host starting and failing on the port.
if (Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue) {
    Write-Host "Todo is already running on $url - opening a window on it." -ForegroundColor Cyan
    Start-Process 'msedge.exe' -ArgumentList $window
    exit 0
}

$hostProject = Join-Path $root 'src\Todo.Host'

# The password's hash, from the user-level variable SetPassword.cmd writes. Read from there as well
# as from the process, because a console opened before SetPassword.cmd ran does not have it yet.
if (-not $env:Auth__PasswordHash) {
    $env:Auth__PasswordHash = [Environment]::GetEnvironmentVariable('Auth__PasswordHash', 'User')
}
if (-not $env:Auth__PasswordHash) {
    throw 'No password is set for Todo. Run SetPassword.cmd once, then start Todo again.'
}

# The hash of the helper's key, from the user-level variable SetHelperKey.cmd writes. Without it the
# app still starts, but turns the helper away - so Jira, Azure DevOps and the AI model are out of reach,
# and the settings page says the helper is not connected. Said here too, with the cure.
if (-not $env:Helper__KeyHash) {
    $env:Helper__KeyHash = [Environment]::GetEnvironmentVariable('Helper__KeyHash', 'User')
}
if (-not $env:Helper__KeyHash) {
    Write-Host 'No helper key is set, so the helper cannot connect. Pair it on the settings page, then run SetHelperKey.cmd.' -ForegroundColor Yellow
}

# A host running from the build folder on another port - the Photino app from before 2.0.0, which took
# a random one - locks the files the build below has to write, and the build then fails with an
# MSB3027 that names neither the cause nor the cure. Measured 2026-10-26. Named here instead.
$buildFolder = Join-Path $hostProject 'bin\Release'
$older = @(Get-Process -Name 'Todo.Host' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($buildFolder, [StringComparison]::OrdinalIgnoreCase) })
if ($older.Count -gt 0) {
    throw ("An older Todo is running from {0} (pid {1}). Close it and start Todo again." -f
        $buildFolder, (($older | ForEach-Object { $_.Id }) -join ', '))
}

Write-Host 'Building Todo...' -ForegroundColor Cyan
& dotnet build $hostProject -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

Write-Host 'Starting Todo...' -ForegroundColor Cyan

# The content root is named here because the default is the binary's own folder since slice 16, and
# under "dotnet run" that folder is bin\Release\net10.0. It no longer finds wwwroot - the frontend is
# embedded in the assembly - but it is where the host looks for configuration files, and pointing
# it at the project is the honest answer during development.
$server = Start-Process -FilePath 'dotnet' -NoNewWindow -PassThru -ArgumentList (@(
    'run', '--project', $hostProject, '-c', 'Release', '--no-build', '--no-launch-profile', '--',
    '--contentRoot', $hostProject, '--urls', $url.TrimEnd('/')) + $args)

# curl.exe rather than Invoke-WebRequest: AMSI blocks Start-Process combined with Invoke-WebRequest
# on this machine (CLAUDE.md, "Maskinen").
$ready = $false
foreach ($attempt in 1..120) {
    Start-Sleep -Milliseconds 500
    if ($server.HasExited) { exit $server.ExitCode }
    $code = & curl.exe -s -o NUL -w '%{http_code}' "${url}api/health"
    if ($code -eq '200') { $ready = $true; break }
}
if (-not $ready) { throw "Todo did not answer on ${url}api/health within a minute." }

Start-Process 'msedge.exe' -ArgumentList $window
Write-Host "Todo runs on $url - close this window to stop it." -ForegroundColor Cyan

$server.WaitForExit()
exit $server.ExitCode
