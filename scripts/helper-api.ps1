#Requires -Version 5.1

# The helper's local API from a script, for verify-local.ps1: dot-source it, and every function takes the
# helper's address so a test can point them at a helper on another port. curl.exe rather than
# Invoke-WebRequest: AMSI blocks Start-Process combined with Invoke-WebRequest on this machine
# (CLAUDE.md, "Maskinen"), and the scripts that use these start processes.

function Invoke-HelperApi([string] $helper, [string] $method, [string] $path, [string] $body) {
    $arguments = @('-s', '-f', '-X', $method, '-H', 'X-Todo-Helper: 1', "$helper$path")

    # A file rather than an argument: PowerShell 5.1 strips the quotes out of JSON passed to a native
    # command, and the helper would read the body as nothing.
    $file = $null
    if ($body) {
        $file = [IO.Path]::GetTempFileName()
        [IO.File]::WriteAllText($file, $body)
        $arguments += @('-H', 'Content-Type: application/json', '--data-binary', "@$file")
    }

    try {
        $answer = & curl.exe @arguments
        if ($LASTEXITCODE -ne 0) { throw "The helper refused $method $path (curl exit $LASTEXITCODE)." }
        return ($answer -join "`n") | ConvertFrom-Json
    } finally {
        if ($file) { Remove-Item $file }
    }
}

function Get-HelperStatus([string] $helper) {
    Invoke-HelperApi $helper 'GET' '/api/connections'
}

# Setting the address also wakes a tunnel that is waiting out its back-off, which can be thirty seconds.
function Set-HelperServer([string] $helper, [string] $server) {
    Invoke-HelperApi $helper 'PUT' '/api/server' (@{ url = $server } | ConvertTo-Json -Compress)
}

# A new key, kept in the helper; the answer is its hash, for the host's Helper:KeyHash.
function New-HelperKey([string] $helper) {
    (Invoke-HelperApi $helper 'POST' '/api/server/pair').keyHash
}

# The tunnel's state once it is Connected or Refused, or whatever it is when the time runs out.
function Wait-HelperTunnel([string] $helper, [int] $seconds) {
    $status = $null
    foreach ($attempt in 1..($seconds * 2)) {
        $status = Get-HelperStatus $helper
        if ($status.tunnel -in 'Connected', 'Refused') { break }
        Start-Sleep -Milliseconds 500
    }
    return $status
}
