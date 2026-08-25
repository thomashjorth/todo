#Requires -Version 5.1

# Publishes the app as a self-contained exe and then proves the artifact actually runs.
#
# The proof is the point. A publish that succeeds says nothing about whether the exe works: slice 16
# measured a publish that built cleanly and then answered 404 on every page, because the content root
# was the process working directory and wwwroot was not in it. Nothing in dotnet test could see that,
# and nothing in the publish output looked wrong.
#
# English throughout, like every script here. It also settles an encoding problem: PowerShell 5.1
# decodes a .ps1 without a BOM as the ANSI codepage, so a Danish letter reaches the reader as
# mojibake. English is plain ASCII, so the question does not come up.
param(
    # Where the two files land: publish\ in the repository root, which .gitignore holds out for the
    # same reason it holds out bin and dist.
    #
    # This was %TEMP% first, to keep 110 MB out of the working tree - but a temp folder is the wrong
    # home for something you launch: Windows may clear it, so anything pointing at the exe by path -
    # a shortcut, a pinned taskbar entry - could end up pointing at a file that is gone. A known
    # folder beats a disposable one, and the ignore rule does the job the temp folder was doing.
    #
    # Left as a parameter, because installing somewhere permanent is a different act from building:
    # pass -OutputPath C:\Apps\MandalorianToDo when that is what you mean.
    [string]$OutputPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'publish')
)

# Below the param block, not above it: param has to be the first executable statement in a script,
# and an assignment before it is a parse error rather than a warning.
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$hostProject = Join-Path $root 'src\Todo.Host\Todo.Host.csproj'

# dotnet tool restore reads its manifest from the current directory, so everything runs from the
# repo root rather than from wherever the caller happened to stand.
Set-Location $root

function Fail($message) {
    Write-Host ''
    Write-Host $message -ForegroundColor Red
    exit 1
}

# Angular first, and it is not optional any more. Since slice 16 the frontend is an EmbeddedResource
# of Todo.Host, so a stale wwwroot is baked into the exe rather than sitting beside it where the next
# build would pick it up. Skipping this step ships the previous frontend inside a fresh exe.
Write-Host ''
Write-Host '[1/3] Angular build' -ForegroundColor Cyan
& powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'build-web.ps1')
if ($LASTEXITCODE -ne 0) { Fail ("The Angular build failed (exit code {0})." -f $LASTEXITCODE) }

Write-Host ''
Write-Host '[2/3] Publish' -ForegroundColor Cyan

# Said plainly, because the raw failure is not: a running exe cannot be overwritten, and without
# this the script dies on an UnauthorizedAccessException from Remove-Item that names the file but not
# the reason. Measured by publishing while the app was open.
#
# The process is only reported, never stopped. It is the user's own window, and it may be holding
# unsaved work - the same rule the probe below follows when it kills only the pid it started.
$running = @(Get-Process -Name 'Todo.Host' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and $_.Path.StartsWith($OutputPath, [StringComparison]::OrdinalIgnoreCase) })

if ($running.Count -gt 0) {
    Write-Host ''
    Write-Host 'The app is running from the folder this would overwrite:' -ForegroundColor Red
    foreach ($p in $running) { Write-Host ("  pid {0}  {1}" -f $p.Id, $p.Path) -ForegroundColor Red }
    Fail 'Close the app and run this again.'
}

if (Test-Path $OutputPath) { Remove-Item $OutputPath -Recurse -Force }

# IncludeNativeLibrariesForSelfExtract is what keeps Photino.Native, WebView2Loader and e_sqlite3
# inside the exe instead of in a runtimes\ folder beside it. Measured: without it the output is a
# folder, with it there is nothing loose but the icon.
& dotnet publish $hostProject -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $OutputPath
if ($LASTEXITCODE -ne 0) { Fail ("Publish failed (exit code {0})." -f $LASTEXITCODE) }

$exe = Join-Path $OutputPath 'Todo.Host.exe'
if (-not (Test-Path $exe)) { Fail ("No exe in {0}." -f $OutputPath) }

# The shape of the output, and this is the one assertion here that dotnet test cannot make at all.
# Slice 16 promised one exe; what it delivered is the exe plus an icon Photino needs as a path.
# Everything else that used to be here - web.config, three pdb files, a 55 kB static web assets
# manifest and nine wwwroot files - is held out by csproj properties, and a property that stops
# working looks exactly like one that works. So the list is written down.
#
# Note what this replaced. The probe below runs the exe from the repo root, which was written to
# catch finding 5: a published exe whose content root was the working directory answered 404 on
# every page. Measured after Task 2, that mutation no longer fails - embedding the frontend removed
# the mechanism, because the assembly does not care where the process was started. The probe still
# proves the exe runs and serves; it can no longer prove the content root, and pretending otherwise
# would be a guard that cannot fail.
$expected = @('Todo.Host.exe', 'icon.ico')
$actual = @(Get-ChildItem -Path $OutputPath -Recurse -File |
    ForEach-Object { $_.FullName.Substring($OutputPath.Length).TrimStart('\') } | Sort-Object)

$unexpected = @($actual | Where-Object { $expected -notcontains $_ })
$missing = @($expected | Where-Object { $actual -notcontains $_ })

if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
    Write-Host ''
    Write-Host 'The publish output is not the shape the slice promised:' -ForegroundColor Red
    foreach ($file in $unexpected) { Write-Host ("  unexpected: {0}" -f $file) -ForegroundColor Red }
    foreach ($file in $missing) { Write-Host ("  missing:    {0}" -f $file) -ForegroundColor Red }
    Fail ("Expected exactly: {0}" -f ($expected -join ', '))
}

Write-Host ''
Write-Host '[3/3] Probing the published exe' -ForegroundColor Cyan

# A free port asked of the OS rather than a number picked here: the user usually has the app open,
# and two probes could otherwise collide.
$listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
$listener.Start()
$port = $listener.LocalEndpoint.Port
$listener.Stop()

# Never the real database. %APPDATA%\TodoApp\todo.db is the user's own tasks, and it holds tokens in
# cleartext -so every probe gets its own file and takes it with it when it goes.
$probeDatabase = Join-Path ([IO.Path]::GetTempPath()) ("TodoApp.publish.{0}.db" -f [Guid]::NewGuid().ToString('N'))
$logFile = Join-Path ([IO.Path]::GetTempPath()) ("TodoApp.publish.{0}.log" -f $port)

# Started from the repo root, deliberately: this is the whole assertion. Run from its own folder the
# exe answered 200 even before the content root was fixed, so a probe from there could not fail.
$process = Start-Process -FilePath $exe -PassThru -WindowStyle Hidden `
    -WorkingDirectory $root `
    -RedirectStandardOutput $logFile `
    -ArgumentList @(
        '--headless',
        '--urls', ("http://127.0.0.1:{0}" -f $port),
        '--Data:Path', $probeDatabase
    )

$failures = @()

try {
    # Health first and on a loop, because it is the cheapest route and the one that says the host
    # finished starting. Migrations run before the first request is served.
    $ready = $false

    foreach ($attempt in 1..30) {
        Start-Sleep -Milliseconds 500

        try {
            $response = Invoke-WebRequest -Uri ("http://127.0.0.1:{0}/api/health" -f $port) -UseBasicParsing -TimeoutSec 5
            if ($response.StatusCode -eq 200) { $ready = $true; break }
        }
        catch {
            # PowerShell 5.1 throws on an HTTP error code and on a refused connection alike, so a
            # failed attempt here means "not up yet" rather than "broken".
        }
    }

    if (-not $ready) { $failures += '/api/health did not answer 200 within 15 seconds' }

    if ($ready) {
        # The frontend and the documentation page. These three are the ones that read something the
        # publish had to carry: index.html and the hashed bundle come out of the assembly, and
        # /scalar/ reads the embedded contract.
        $index = Invoke-WebRequest -Uri ("http://127.0.0.1:{0}/" -f $port) -UseBasicParsing -TimeoutSec 5

        if ($index.StatusCode -ne 200) { $failures += ("/ answered {0}" -f $index.StatusCode) }

        # Not merely a 200: MapFallbackToFile answers 200 with index.html for anything it does not
        # recognise, so a body check is what tells a served page from a served fallback.
        if ($index.Content -notmatch '<app-root') { $failures += '/ answered 200 without app-root in the body' }

        # The hashed bundle by the name index.html asks for, so a wrong or missing embed shows up
        # here rather than as a blank window later.
        $bundle = [regex]::Match($index.Content, 'src="(main-[^"]+\.js)"')

        if (-not $bundle.Success) {
            $failures += 'Found no main-*.js in index.html'
        }
        else {
            foreach ($path in @($bundle.Groups[1].Value, 'i18n/da.json', 'scalar/')) {
                try {
                    $asset = Invoke-WebRequest -Uri ("http://127.0.0.1:{0}/{1}" -f $port, $path) -UseBasicParsing -TimeoutSec 5
                    if ($asset.StatusCode -ne 200) { $failures += ("/{0} answered {1}" -f $path, $asset.StatusCode) }
                }
                catch {
                    $failures += ("/{0} failed: {1}" -f $path, $_.Exception.Message)
                }
            }
        }
    }
}
finally {
    # On the port, never on the name. The user often has the app open, and Stop-Process -Name
    # Todo.Host would close their window along with this one.
    $owners = @(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty OwningProcess -Unique)

    foreach ($owner in $owners) {
        if ($owner -eq $process.Id) { Stop-Process -Id $owner -Force -ErrorAction SilentlyContinue }
    }

    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue }

    # The database and its two WAL companions. todo.db alone is not the database.
    foreach ($file in @($probeDatabase, "$probeDatabase-wal", "$probeDatabase-shm", $logFile)) {
        if (Test-Path $file) { Remove-Item $file -Force -ErrorAction SilentlyContinue }
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'The published exe did not answer as it should:' -ForegroundColor Red
    foreach ($failure in $failures) { Write-Host ("  {0}" -f $failure) -ForegroundColor Red }
    Write-Host ''
    Write-Host ("The exe is still in {0}, so it can be inspected." -f $OutputPath) -ForegroundColor Red
    exit 1
}

$files = @(Get-ChildItem -Path $OutputPath -Recurse -File)
$total = ($files | Measure-Object -Property Length -Sum).Sum

Write-Host ''
Write-Host ("Published and probed: {0} file(s), {1:N1} MiB" -f $files.Count, ($total / 1MB)) -ForegroundColor Green
foreach ($file in $files | Sort-Object Name) {
    Write-Host ("  {0,12:N0}  {1}" -f $file.Length, $file.Name)
}
Write-Host ''
Write-Host $OutputPath -ForegroundColor Green
