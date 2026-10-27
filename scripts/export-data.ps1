#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

# Writes the app's database as one file for the NAS, without the Jira and Azure DevOps tokens 1.16.0
# stored in it. The database itself is only read, so this is safe while Todo is open.
# docs/NAS.md says where the file goes.

$root = Split-Path -Parent $PSScriptRoot
$hostProject = Join-Path $root 'src\Todo.Host'

$target = if ($args.Count -gt 0) { $args[0] } else {
    Join-Path ([Environment]::GetFolderPath('Desktop')) 'todo.db'
}

# The Debug build, for the same reason as SetPassword.cmd: Todo.cmd runs the app from bin\Release, and
# a running app locks the files a Release build would write.
Write-Host 'Building Todo...' -ForegroundColor Cyan
& dotnet build $hostProject --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

# A second argument names another database than the app's own - for trying the script without it.
$verb = @('export-db', $target) + @($args | Select-Object -Skip 1)
& dotnet run --project $hostProject --no-build --no-launch-profile -- @verb
if ($LASTEXITCODE -ne 0) { throw "export-db failed ($LASTEXITCODE)" }

Write-Host "Exported to $target, without tokens. Copy it to the NAS as data\todo.db - docs\NAS.md." -ForegroundColor Green
