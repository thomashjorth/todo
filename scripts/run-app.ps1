#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

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

Write-Host 'Starting Todo...' -ForegroundColor Cyan

# The content root is named here because the default is the binary's own folder since slice 16, and
# under "dotnet run" that folder is bin\Release\net10.0.
#
# What it does NOT do any more is find wwwroot: slice 16 embedded the frontend in the assembly, so
# static files come from there whatever the content root says. This line stays because the content
# root is also where the host would look for configuration files, and pointing it at the project is
# the honest answer during development. A published exe needs no argument at all.
$hostProject = Join-Path $root 'src\Todo.Host'
& dotnet run --project $hostProject -c Release --no-launch-profile -- --contentRoot $hostProject @args
exit $LASTEXITCODE
