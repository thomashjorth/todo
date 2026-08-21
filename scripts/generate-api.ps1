#Requires -Version 5.1
$ErrorActionPreference = 'Stop'
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

$root = Split-Path -Parent $PSScriptRoot
$contract = Join-Path $root 'contracts\openapi.yaml'
$csOut = Join-Path $root 'src\Todo.Contracts\Generated\Contracts.g.cs'
$tsOut = Join-Path $root 'src\Todo.Web\src\app\api\todo-client.ts'
$hashOut = Join-Path $root 'src\Todo.Contracts\Generated\.source-hash'

New-Item -ItemType Directory -Force -Path (Split-Path $csOut) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $tsOut) | Out-Null

# dotnet tool restore reads the manifest from the current directory, so run from the repo root -
# otherwise it silently restores whichever repo the shell sat in.
Set-Location $root

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)" }

Write-Host 'Generating C# DTOs...'
dotnet nswag openapi2csclient `
    /input:$contract `
    /output:$csOut `
    /namespace:Todo.Contracts `
    /generateClientClasses:false `
    /generateDtoTypes:true `
    /dateType:System.DateOnly `
    /jsonLibrary:SystemTextJson `
    /jsonLibraryVersion:10.0
if ($LASTEXITCODE -ne 0) { throw "NSwag C# generation failed ($LASTEXITCODE)" }

Write-Host 'Generating Angular client...'
dotnet nswag openapi2tsclient `
    /input:$contract `
    /output:$tsOut `
    /template:Angular `
    /httpClass:HttpClient `
    /rxJsVersion:7.0 `
    /injectionTokenType:InjectionToken `
    /useSingletonProvider:true `
    /operationGenerationMode:MultipleClientsFromFirstTagAndOperationId `
    /dateTimeType:string `
    /typeStyle:Class
if ($LASTEXITCODE -ne 0) { throw "NSwag TypeScript generation failed ($LASTEXITCODE)" }

# core.autocrlf gives the working copy CRLF, so a raw byte hash would be machine-dependent.
$normalized = ([System.IO.File]::ReadAllText($contract)) -replace "`r`n", "`n"
$sha = [System.Security.Cryptography.SHA256]::Create()
try {
    $bytes = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($normalized))
} finally {
    $sha.Dispose()
}
$hash = [System.BitConverter]::ToString($bytes).Replace('-', '')
[System.IO.File]::WriteAllText($hashOut, $hash, [System.Text.UTF8Encoding]::new($false))
Write-Host "Done. Contract hash: $hash"
