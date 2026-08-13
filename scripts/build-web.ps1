#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$web = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Todo.Web'
if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    & npm.cmd ci --prefix $web
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed ($LASTEXITCODE)" }
}
& npm.cmd run build --prefix $web
if ($LASTEXITCODE -ne 0) { throw "ng build failed ($LASTEXITCODE)" }
