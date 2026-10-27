#Requires -Version 5.1
param(
    # 'start' (the default) runs the NAS setup here; 'stop' takes it down and puts Docker back.
    [ValidateSet('start', 'stop')]
    [string] $Action = 'start',

    # The image built from this working tree instead of the one CI pushed - for a change not yet on main.
    [switch] $Build,

    # Your own tasks, exported without tokens the way ExportData.cmd does, on the first start only.
    [switch] $WithMyData
)
$ErrorActionPreference = 'Stop'

# The NAS's setup on this PC: the same image, the same docker-compose.yml from deploy\, the same
# environment variables - so what works here is what the NAS will run. Docker on this machine runs
# Windows containers and the image is Linux, so the script switches the engine to Linux, and 'stop'
# switches it back if it was Windows before. Nothing here touches the app's own database or Todo.cmd.

$root = Split-Path -Parent $PSScriptRoot
$folder = Join-Path $env:LOCALAPPDATA 'TodoNasTry'
$project = 'todo-try'

# 8088 rather than the NAS's 8080: clear of Todo.cmd's 5180 and of the usual development servers.
$port = 8088
$url = "http://127.0.0.1:$port/"

$dockerDesktop = Join-Path $env:ProgramFiles 'Docker\Docker\Docker Desktop.exe'
$dockerCli = Join-Path $env:ProgramFiles 'Docker\Docker\DockerCli.exe'
$previousEngine = Join-Path $folder 'previous-engine.txt'

function Get-EngineOs {
    $os = & docker info --format '{{.OSType}}' 2>$null
    if ($LASTEXITCODE -ne 0) { return $null }
    return "$os".Trim()
}

function Wait-Engine([string] $os, [int] $seconds) {
    foreach ($attempt in 1..$seconds) {
        if ((Get-EngineOs) -eq $os) { return }
        Start-Sleep -Seconds 1
    }
    throw "Docker did not come up with $os containers within $seconds seconds. Look at Docker Desktop's window."
}

function Install-Docker {
    # WSL 2 first: Docker Desktop's Linux engine runs in it. Installing it needs a restart of Windows.
    & wsl.exe --status *> $null
    if ($LASTEXITCODE -ne 0) {
        Write-Host 'Installing WSL 2 - Windows asks for administrator rights...' -ForegroundColor Cyan
        Start-Process 'wsl.exe' -ArgumentList '--install', '--no-distribution' -Verb RunAs -Wait
        throw 'WSL 2 is installed. Restart Windows, then run TryNas.cmd again.'
    }

    Write-Host 'Installing Docker Desktop with winget - Windows asks for administrator rights...' -ForegroundColor Cyan
    & winget.exe install --exact --id Docker.DockerDesktop --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { throw "winget could not install Docker Desktop ($LASTEXITCODE)." }

    throw 'Docker Desktop is installed. Sign out of Windows and in again, start Docker Desktop once and accept its terms, then run TryNas.cmd again.'
}

function Start-Docker {
    if (-not (Test-Path $dockerDesktop)) { Install-Docker }

    if ($null -eq (Get-EngineOs)) {
        Write-Host 'Starting Docker Desktop...' -ForegroundColor Cyan
        Start-Process $dockerDesktop
        foreach ($attempt in 1..180) {
            if ($null -ne (Get-EngineOs)) { break }
            Start-Sleep -Seconds 1
        }
        if ($null -eq (Get-EngineOs)) { throw 'Docker Desktop did not start within three minutes.' }
    }
}

function Switch-Engine([string] $to) {
    $switch = if ($to -eq 'linux') { '-SwitchLinuxEngine' } else { '-SwitchWindowsEngine' }
    Write-Host "Switching Docker to $to containers..." -ForegroundColor Cyan
    & $dockerCli $switch
    Wait-Engine $to 180
}

function Invoke-Compose {
    & docker compose --project-name $project --project-directory $folder `
        -f (Join-Path $folder 'docker-compose.yml') -f (Join-Path $folder 'docker-compose.override.yml') @args
    if ($LASTEXITCODE -ne 0) { throw "docker compose $($args -join ' ') failed ($LASTEXITCODE)" }
}

if ($Action -eq 'stop') {
    if ((Test-Path $folder) -and $null -ne (Get-EngineOs)) {
        if ((Get-EngineOs) -eq 'linux') {
            Invoke-Compose down
            Write-Host 'The NAS setup is stopped. Its data stays in the volume todo-try_data.' -ForegroundColor Green
        }

        if ((Test-Path $previousEngine) -and (Get-Content $previousEngine) -eq 'windows') {
            Switch-Engine 'windows'
            Remove-Item $previousEngine
            Write-Host 'Docker runs Windows containers again, as before.' -ForegroundColor Green
        }
    }
    exit 0
}

# The same two hashes Todo.cmd uses, from the user-level variables SetPassword.cmd and SetHelperKey.cmd
# write - so the password is yours, and a helper paired for Todo.cmd is let in here too.
$passwordHash = [Environment]::GetEnvironmentVariable('Auth__PasswordHash', 'User')
if (-not $passwordHash) { throw 'No password is set. Run SetPassword.cmd once, then TryNas.cmd again.' }
$helperKeyHash = [Environment]::GetEnvironmentVariable('Helper__KeyHash', 'User')

Start-Docker

New-Item -ItemType Directory -Force $folder | Out-Null
$before = Get-EngineOs
if ($before -ne 'linux') {
    # Remembered once, so a second start does not forget that it was Windows before the first.
    if (-not (Test-Path $previousEngine)) { Set-Content -Path $previousEngine -Value $before -Encoding ascii }
    Switch-Engine 'linux'
}

# The NAS's own compose file, copied as the checklist copies it, so a change to it is tried here too.
Copy-Item (Join-Path $root 'deploy\docker-compose.yml') (Join-Path $folder 'docker-compose.yml') -Force

# What differs from the NAS, and only that: a named volume rather than .\data, because SQLite's WAL
# needs a real filesystem and a Windows folder shared into the Linux VM is not one (docs/NAS.md says
# the same of a network drive); and, with -Build, the image from this working tree.
$image = if ($Build) { "    image: todo-local:try`n" } else { '' }
$override = "services:`n  todo:`n$image    container_name: todo-try`n    volumes:`n      - data:/data`nvolumes:`n  data:`n"
[IO.File]::WriteAllText((Join-Path $folder 'docker-compose.override.yml'), $override)

# The .env the NAS has, with this PC's answers. 1654 is the image's own user, which owns /data in the
# volume. Single quotes, because the hashes are full of $ and Compose reads $ as a variable.
$envFile = @(
    'TODO_UID=1654'
    'TODO_GID=1654'
    "TODO_PORT=$port"
    "TODO_PASSWORD_HASH='$passwordHash'"
    "TODO_HELPER_KEY_HASH='$helperKeyHash'"
) -join "`n"
[IO.File]::WriteAllText((Join-Path $folder '.env'), "$envFile`n")

if ($Build) {
    Write-Host 'Building the image from this working tree...' -ForegroundColor Cyan
    & docker build --tag todo-local:try $root
    if ($LASTEXITCODE -ne 0) { throw "docker build failed ($LASTEXITCODE)" }
} else {
    Write-Host 'Pulling the image CI pushed...' -ForegroundColor Cyan
    Invoke-Compose pull --quiet
}

# Created before the copy below, so the volume exists with /data owned by the image's user.
Invoke-Compose create

if ($WithMyData) {
    $export = Join-Path $folder 'todo.db'
    if (Test-Path $export) { Remove-Item $export }

    & (Join-Path $PSScriptRoot 'export-data.ps1') $export

    # In through the image itself, as root, and handed to its user: a file copied in by docker cp would
    # be root's, and the app could not write to it.
    $imageName = if ($Build) { 'todo-local:try' } else { 'ghcr.io/thomashjorth/todo:latest' }
    & docker run --rm --user 0 -v "${project}_data:/data" -v "${folder}:/import:ro" --entrypoint sh $imageName `
        -c 'if [ -f /data/todo.db ]; then echo "The volume has a database already - left as it is."; else cp /import/todo.db /data/todo.db && chown 1654:1654 /data/todo.db; fi'
    if ($LASTEXITCODE -ne 0) { throw "Copying the export into the volume failed ($LASTEXITCODE)" }
    Remove-Item $export
}

Invoke-Compose up -d

# curl.exe rather than Invoke-WebRequest: AMSI blocks Start-Process combined with Invoke-WebRequest
# on this machine (CLAUDE.md, "Maskinen").
$ready = $false
foreach ($attempt in 1..120) {
    Start-Sleep -Milliseconds 500
    $code = & curl.exe -s -o NUL -w '%{http_code}' "${url}api/health"
    if ($code -eq '200') { $ready = $true; break }
}
if (-not $ready) {
    & docker logs --tail 40 todo-try
    throw "The container did not answer on ${url}api/health within a minute. Its log is above."
}

Start-Process 'msedge.exe' -ArgumentList @("--app=$url", '--window-size=480,1000')

Write-Host ''
Write-Host "The NAS setup runs on $url, with your own password." -ForegroundColor Green
Write-Host 'For Jira, Azure DevOps and the AI model: start Helper.cmd, and under Indstillinger -> Forbindelser' -ForegroundColor Cyan
Write-Host "set the server's address to http://127.0.0.1:$port. The helper serves one server at a time," -ForegroundColor Cyan
Write-Host 'so set it back to http://127.0.0.1:5180 for Todo.cmd afterwards.' -ForegroundColor Cyan
if (-not $helperKeyHash) {
    Write-Host 'No helper key is set yet: make one on that page, run SetHelperKey.cmd, then TryNas.cmd again.' -ForegroundColor Yellow
}
Write-Host 'TryNas.cmd stop takes it down and puts Docker back to Windows containers.' -ForegroundColor Cyan
