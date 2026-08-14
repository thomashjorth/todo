#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$web = Join-Path $root 'src\Todo.Web'
$index = Join-Path $root 'src\Todo.Host\wwwroot\index.html'

if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    Write-Host 'Henter npm-pakker...' -ForegroundColor Cyan
    & npm.cmd ci --prefix $web
    if ($LASTEXITCODE -ne 0) { throw "npm ci fejlede ($LASTEXITCODE)" }
}

# ng build tager et par sekunder, men gør vinduet blankt hvis det springes over
# efter en frontend-ændring. Derfor sammenlignes tidsstempler frem for at spørge.
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
    Write-Host 'Bygger Angular...' -ForegroundColor Cyan
    & npm.cmd run build --prefix $web
    if ($LASTEXITCODE -ne 0) { throw "ng build fejlede ($LASTEXITCODE)" }
} else {
    Write-Host 'Angular er opdateret.' -ForegroundColor DarkGray
}

Write-Host 'Starter Todo...' -ForegroundColor Cyan
& dotnet run --project (Join-Path $root 'src\Todo.Host') -c Release --no-launch-profile -- @args
exit $LASTEXITCODE
