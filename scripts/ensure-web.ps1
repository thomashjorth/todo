#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

# Builds Angular into the host's wwwroot when a source is newer than index.html. Shared by Todo.cmd
# and Helper.cmd, because the helper carries the same bundle: its Connections page is the app's own
# second root, embedded in the helper's exe when the helper is built.

$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'src\Todo.Web'
$index = Join-Path $root 'src\Todo.Host\wwwroot\index.html'

if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    Write-Host 'Installing npm packages...' -ForegroundColor Cyan
    & npm.cmd ci --prefix $web
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed ($LASTEXITCODE)" }
}

# ng build takes a couple of seconds, but skipping it after a frontend change leaves the window
# blank - so timestamps are compared rather than the user being asked.
$sources = @(Get-ChildItem -Recurse -File (Join-Path $web 'src'))
foreach ($name in 'angular.json', 'package.json', '.postcssrc.json') {
    $path = Join-Path $web $name
    if (Test-Path $path) { $sources += Get-Item $path }
}
$newestSource = ($sources | Measure-Object -Property LastWriteTimeUtc -Maximum).Maximum

$needsBuild = -not (Test-Path $index)
if (-not $needsBuild) {
    $needsBuild = (Get-Item $index).LastWriteTimeUtc -lt $newestSource
}

if ($needsBuild) {
    Write-Host 'Building Angular...' -ForegroundColor Cyan
    & npm.cmd run build --prefix $web
    if ($LASTEXITCODE -ne 0) { throw "ng build failed ($LASTEXITCODE)" }
} else {
    Write-Host 'Angular is up to date.' -ForegroundColor DarkGray
}
