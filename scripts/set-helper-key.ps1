#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

# Stores the hash of the helper's key for Todo.cmd, in the user-level variable Helper__KeyHash the host
# reads on every start. The hash comes from "Make a new key" on the Connections page; the key itself
# never leaves the helper. The NAS gets the same hash in its .env as TODO_HELPER_KEY_HASH.

$hash = if ($args.Count -gt 0) { $args[0] } else { Read-Host "Paste the key's hash from the Connections page" }
$hash = "$hash".Trim()

if ($hash -notmatch '^pbkdf2-sha256\$\d+\$[^$]+\$[^$]+$') {
    throw 'That is not a key hash. Copy the whole line from the Connections page; it starts with pbkdf2-sha256$.'
}

[Environment]::SetEnvironmentVariable('Helper__KeyHash', $hash, 'User')
$env:Helper__KeyHash = $hash

Write-Host 'The helper key is set. Start Todo again for it to take effect.' -ForegroundColor Green
