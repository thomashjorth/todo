#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

# Sets the app's one password: asks for it twice, hashes it with the host's own hash-password verb,
# and stores the hash - never the password - in the user-level variable Auth__PasswordHash, which the
# host reads on every start. Run it once, and again to change the password.

$root = Split-Path -Parent $PSScriptRoot
$hostProject = Join-Path $root 'src\Todo.Host'

function Read-Plain([string] $prompt) {
    $secure = Read-Host $prompt -AsSecureString
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer) }
}

$first = Read-Plain 'New password for Todo'
if ([string]::IsNullOrEmpty($first)) { throw 'An empty password is refused.' }

$second = Read-Plain 'The same password again'
if ($first -ne $second) { throw 'The two passwords differ. Nothing was changed.' }

# The Debug build rather than Release: Todo.cmd runs the app from bin\Release, and a running app locks
# the files a Release build would write - so changing the password while Todo is open would fail on
# a locked file. Measured 2026-10-26 for Todo.cmd itself.
Write-Host 'Building Todo...' -ForegroundColor Cyan
& dotnet build $hostProject --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed ($LASTEXITCODE)" }

# The password reaches the host in its environment, not on stdin or the command line. PowerShell 5.1
# pipes to a native command as ASCII and .NET reads a redirected stdin in the console's OEM code
# page, so a password with Danish letters would be hashed as something else than the browser sends; an argument
# would land in the process list. Environment variables are UTF-16 all the way.
$env:TODO_PASSWORD = $first
try {
    $output = & dotnet run --project $hostProject --no-build --no-launch-profile -- hash-password
    if ($LASTEXITCODE -ne 0) { throw "hash-password failed ($LASTEXITCODE)" }
}
finally {
    Remove-Item Env:\TODO_PASSWORD -ErrorAction SilentlyContinue
}

$hash = @($output | Where-Object { $_ -match '^pbkdf2-sha256\$' }) | Select-Object -Last 1
if (-not $hash) { throw 'hash-password printed no hash.' }

[Environment]::SetEnvironmentVariable('Auth__PasswordHash', $hash, 'User')
$env:Auth__PasswordHash = $hash

Write-Host 'The password is set. Start Todo again for it to take effect.' -ForegroundColor Green
