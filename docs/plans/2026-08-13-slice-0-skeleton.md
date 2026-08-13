# Skive 0 — skelet og rør

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** En kørbar desktop-app der viser "Todo" og statussen fra sit eget API, med kontraktgenerering, drift-test og én E2E-test på plads fra første commit.

**Architecture:** Én proces. `TodoHost.Build(args)` bygger en `WebApplication` der lytter på loopback med tilfældig port og serverer Angular fra `wwwroot`. `Program.Main` åbner et Photino-vindue mod den adresse, medmindre `--headless` er givet. Tests kalder `TodoHost.Build` direkte i egen proces og læser den tildelte adresse — ingen proces-spawning, ingen `WebApplicationFactory`.

**Tech Stack:** .NET 10 (SDK 10.0.300, runtime 10.0.8) · Photino.NET 4.0.16 · NSwag.ConsoleCore 14.7.1 · Angular 22.1.3 · xunit.v3 3.2.2 · Microsoft.Playwright 1.62.0 · YamlDotNet 18.1.0

**Verificeret på maskinen 2026-08-13:** dotnet 10.0.300, node v22.23.1, npm 10.8.2, @angular/cli 22.1.3, nuget.org tilgængelig. **`pwsh` findes ikke** — kun Windows PowerShell 5.1. Derfor installeres Playwright-browsere via `Microsoft.Playwright.Program.Main`, ikke via `playwright.ps1`.

**Inden udførelse:** Thomas' CLAUDE.md kræver godkendelse før `dotnet build` / `dotnet test` uden for Planners integrationstests. Bed om ét samlet ja til build- og testkommandoer i dette repo, før task 1 startes.

**Bevidst uden for skive 0:** database, EF Core, Jira, ADO, tray, notifikationer, Angular unit tests (runner-valget udskydes til skive 1, hvor der er noget at teste).

---

## Task 1: Repo-skelet

**Files:**
- Create: `.gitignore`
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `Todo.sln`
- Create: `src/Todo.Host/wwwroot/.gitkeep`

**Step 1: Opret `global.json`**

```json
{
  "sdk": {
    "version": "10.0.300",
    "rollForward": "latestFeature"
  }
}
```

**Step 2: Opret `Directory.Build.props`**

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

`TreatWarningsAsErrors` er bevidst udeladt — genereret NSwag-kode ville vælte buildet. Det strammes i skive 8.

**Step 3: Opret `.gitignore`**

```gitignore
bin/
obj/
.vs/
*.user
node_modules/
dist/
.angular/
src/Todo.Host/wwwroot/*
!src/Todo.Host/wwwroot/.gitkeep
tests/**/bin/
tests/**/obj/
*.db
*.db-shm
*.db-wal
```

**Step 4: Opret tom solution og placeholder-mappe**

```bash
git -C C:/privat-git/todo init >/dev/null 2>&1; true
```

Kør i repo-roden:

```bash
dotnet new sln --name Todo
```

Opret `src/Todo.Host/wwwroot/.gitkeep` som tom fil. Den sikrer at `wwwroot` findes, også før Angular er bygget.

**Step 5: Verificér**

Run: `dotnet build Todo.sln`
Expected: `Build succeeded` med 0 projekter.

**Step 6: Commit**

```bash
git add -A && git commit -m "🏗️ Add repository skeleton for todo app"
```

---

## Task 2: API-kontrakten

Kontrakten er sandheden. Alt andet i denne skive udledes af den.

**Files:**
- Create: `contracts/openapi.yaml`

**Step 1: Skriv kontrakten**

```yaml
openapi: 3.0.4
info:
  title: Todo API
  version: 1.0.0
paths:
  /api/health:
    get:
      operationId: getHealth
      tags:
        - Health
      summary: Reports that the API is running.
      responses:
        '200':
          description: The service is running.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/HealthResponse'
components:
  schemas:
    HealthResponse:
      type: object
      additionalProperties: false
      required:
        - status
        - version
      properties:
        status:
          type: string
          example: ok
        version:
          type: string
          example: 1.0.0.0
```

`additionalProperties: false` lukker DTO'en. Uden den genererer NSwag en `[JsonExtensionData]`-pose i C# og en `[key: string]: any` index-signatur i TypeScript, og så er typekontrollen — hele pointen med en genereret klient — sat ud af kraft.

**Step 2: Commit**

```bash
git add contracts/openapi.yaml && git commit -m "📝 Add OpenAPI contract with health endpoint"
```

---

## Task 3: Kodegenerering fra kontrakten

**Files:**
- Create: `.config/dotnet-tools.json` (via kommando)
- Create: `src/Todo.Contracts/Todo.Contracts.csproj`
- Create: `scripts/generate-api.ps1`
- Generated: `src/Todo.Contracts/Generated/Contracts.g.cs`
- Generated: `src/Todo.Contracts/Generated/.source-hash`
- Generated: `src/Todo.Web/src/app/api/todo-client.ts`

**Step 1: Installér NSwag som lokalt værktøj**

```bash
dotnet new tool-manifest --output .config
```

```bash
dotnet tool install NSwag.ConsoleCore --version 14.7.1
```

`--output .config` er nødvendig: på SDK 10.0.300 skriver `dotnet new tool-manifest` uden flag manifestet i repo-roden, ikke i `.config/`.

Et lokalt værktøj frem for et globalt, så versionen er pinnet i repoet og følger med en fremtidig CI.

**Step 2: Opret `src/Todo.Contracts/Todo.Contracts.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Todo.Contracts</RootNamespace>
    <NoWarn>$(NoWarn);CS8618;CS1591</NoWarn>
  </PropertyGroup>
</Project>
```

**Step 3: Opret `scripts/generate-api.ps1`**

```powershell
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

dotnet tool restore
if ($LASTEXITCODE -ne 0) { throw "dotnet tool restore failed ($LASTEXITCODE)" }

Write-Host 'Generating C# DTOs...'
dotnet nswag openapi2csclient `
    /input:$contract `
    /output:$csOut `
    /namespace:Todo.Contracts `
    /generateClientClasses:false `
    /generateDtoTypes:true `
    /jsonLibrary:SystemTextJson
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
```

**`dotnet tool restore` skal med.** Uden den virker `dotnet nswag` kun på en maskine der tilfældigvis har værktøjet i sin resolver-cache; på en frisk klon eller i CI fejler den med "Cannot find command 'nswag'". Det ville gøre pinningen i `.config/dotnet-tools.json` meningsløs.

Hash-filen er kontrakten mellem kontrakten og den genererede kode: en test i task 6 fejler hvis `openapi.yaml` er ændret uden at generatoren er kørt.

**Linjeskift normaliseres før hashing.** `core.autocrlf` giver arbejdskopien CRLF, så en rå byte-hash ville være maskinafhængig, og testen i task 6 ville fejle grundløst efter en frisk klon. Task 6 skal normalisere på præcis samme måde.

**Step 4: Kør generatoren**

Run: `powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1`
Expected: to filer skrevet plus `Done. Contract hash: <64 hex-tegn>`.

Hvis et flag afvises, kør `dotnet nswag help openapi2tsclient` og ret flaget — versionen kan have omdøbt det.

**Undtagelsen er `/operationGenerationMode`.** NSwag 14.7.1 accepterer `MultipleClientsFromFirstTagAndOperationId`, selv om `dotnet nswag help openapi2tsclient` ikke nævner værdien i sin liste. Hjælpeteksten er ufuldstændig, ikke autoritativ — lad være med at "rette" flaget efter den. Kommandoen kører grønt, og resultatet er en `HealthClient` med metoden `getHealth()`, hvilket er præcis det ønskede.

**Step 5: Læs den genererede C#-fil igennem**

Bekræft at `Todo.Contracts.HealthResponse` findes med `Status` og `Version`. Er der i stedet genereret en klient-klasse, mangler `/generateClientClasses:false`. Er der en `[JsonExtensionData] AdditionalProperties`-pose på DTO'en, mangler `additionalProperties: false` i kontrakten.

**Step 6: Tilføj projektet til solution og byg**

```bash
dotnet sln add src/Todo.Contracts/Todo.Contracts.csproj
```

Run: `dotnet build Todo.sln`
Expected: `Build succeeded`.

**Step 7: Commit**

```bash
git add -A && git commit -m "✨ Generate C# DTOs and Angular client from the OpenAPI contract"
```

---

## Task 4: Hosten med `/api/health`

Test først. Testen ejer HTTP-kontrakten; implementeringen følger efter.

**Files:**
- Create: `tests/Todo.TestSupport/Todo.TestSupport.csproj`
- Create: `tests/Todo.TestSupport/RepoPaths.cs`
- Create: `tests/Todo.TestSupport/RunningHost.cs`
- Create: `tests/Todo.Api.Tests/Todo.Api.Tests.csproj`
- Create: `tests/Todo.Api.Tests/HealthEndpointTests.cs`
- Create: `src/Todo.Host/Todo.Host.csproj`
- Create: `src/Todo.Host/TodoHost.cs`

**Step 1: Opret host-projektet (tomt endnu)**

`src/Todo.Host/Todo.Host.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <RootNamespace>Todo.Host</RootNamespace>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.8" />
    <PackageReference Include="Photino.NET" Version="4.0.16" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Todo.Contracts\Todo.Contracts.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Opret testsupport-projektet**

`tests/Todo.TestSupport/Todo.TestSupport.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Todo.TestSupport</RootNamespace>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Todo.Host\Todo.Host.csproj" />
  </ItemGroup>
</Project>
```

`tests/Todo.TestSupport/RepoPaths.cs`:

```csharp
namespace Todo.TestSupport;

public static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string HostContentRoot => Path.Combine(Root, "src", "Todo.Host");

    public static string ContractFile => Path.Combine(Root, "contracts", "openapi.yaml");

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Todo.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate Todo.sln above the test output directory.");
    }
}
```

`tests/Todo.TestSupport/RunningHost.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Todo.Host;

namespace Todo.TestSupport;

/// <summary>
/// Starts the real host in-process on a free loopback port. Tests talk to it over real HTTP,
/// so they exercise the same startup path as the shipped app.
/// </summary>
public sealed class RunningHost : IAsyncDisposable
{
    private readonly WebApplication _app;

    private RunningHost(WebApplication app, string baseUrl)
    {
        _app = app;
        BaseUrl = baseUrl;
        Client = new HttpClient { BaseAddress = new Uri(baseUrl) };
    }

    public string BaseUrl { get; }

    public HttpClient Client { get; }

    public static async Task<RunningHost> StartAsync(params string[] extraArgs)
    {
        string[] args =
        [
            "--urls", "http://127.0.0.1:0",
            "--contentRoot", RepoPaths.HostContentRoot,
            .. extraArgs
        ];

        var app = TodoHost.Build(args);
        await app.StartAsync();

        var baseUrl = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        return new RunningHost(app, baseUrl);
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
```

**Step 3: Opret testprojektet**

`tests/Todo.Api.Tests/Todo.Api.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="YamlDotNet" Version="18.1.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Todo.TestSupport\Todo.TestSupport.csproj" />
  </ItemGroup>
</Project>
```

**Step 4: Skriv den fejlende test**

`tests/Todo.Api.Tests/HealthEndpointTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Todo.Contracts;
using Todo.TestSupport;

namespace Todo.Api.Tests;

public class HealthEndpointTests
{
    [Fact]
    public async Task Health_endpoint_reports_ok()
    {
        await using var host = await RunningHost.StartAsync();

        var response = await host.Client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
        Assert.False(string.IsNullOrWhiteSpace(body.Version));
    }
}
```

**Step 5: Tilføj projekterne til solution og kør testen**

```bash
dotnet sln add src/Todo.Host/Todo.Host.csproj tests/Todo.TestSupport/Todo.TestSupport.csproj tests/Todo.Api.Tests/Todo.Api.Tests.csproj
```

Run: `dotnet test tests/Todo.Api.Tests`
Expected: **compile error** — `TodoHost` findes ikke endnu. Det er den røde fase.

**Step 6: Skriv den minimale implementering**

`src/Todo.Host/TodoHost.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Todo.Contracts;

namespace Todo.Host;

public static class TodoHost
{
    public static WebApplication Build(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddOpenApi();

        var app = builder.Build();

        app.MapOpenApi();
        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapGet("/api/health", () => new HealthResponse
        {
            Status = "ok",
            Version = typeof(TodoHost).Assembly.GetName().Version?.ToString() ?? "0.0.0",
        })
        .WithName("getHealth")
        .WithTags("Health")
        .Produces<HealthResponse>();

        app.MapFallbackToFile("index.html");

        return app;
    }
}
```

**Step 7: Kør testen igen**

Run: `dotnet test tests/Todo.Api.Tests`
Expected: **1 passed**.

**Step 8: Commit**

```bash
git add -A && git commit -m "✨ Add host with health endpoint and in-process test harness"
```

---

## Task 5: Drift-test mod kontrakten

Denne test er hele grunden til at contract-first kan bære uden genererede controllere.

**Files:**
- Create: `tests/Todo.Api.Tests/ContractDriftTests.cs`

**Step 1: Skriv testen**

```csharp
using System.Text.Json;
using Todo.TestSupport;
using YamlDotNet.Serialization;

namespace Todo.Api.Tests;

/// <summary>
/// contracts/openapi.yaml owns the API surface. This fails the build when the
/// implementation drifts away from it in either direction.
/// </summary>
public class ContractDriftTests
{
    [Fact]
    public async Task Running_api_exposes_exactly_the_operations_in_the_contract()
    {
        var expected = OperationsFromContract();
        var actual = await OperationsFromRunningAppAsync();

        Assert.Equal(expected, actual);
    }

    private static SortedSet<string> OperationsFromContract()
    {
        var yaml = File.ReadAllText(RepoPaths.ContractFile);
        var document = new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object>>(yaml);

        var paths = (Dictionary<object, object>)document["paths"];
        var operations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var (path, verbs) in paths)
        {
            foreach (var verb in ((Dictionary<object, object>)verbs).Keys)
            {
                operations.Add($"{verb.ToString()!.ToUpperInvariant()} {path}");
            }
        }

        return operations;
    }

    private static async Task<SortedSet<string>> OperationsFromRunningAppAsync()
    {
        await using var host = await RunningHost.StartAsync();

        using var stream = await host.Client.GetStreamAsync("/openapi/v1.json");
        using var document = await JsonDocument.ParseAsync(stream);

        var operations = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var verb in path.Value.EnumerateObject())
            {
                operations.Add($"{verb.Name.ToUpperInvariant()} {path.Name}");
            }
        }

        return operations;
    }
}
```

**Step 2: Kør testen**

Run: `dotnet test tests/Todo.Api.Tests`
Expected: **2 passed**.

Fejler den, så læs hvilken side der har for meget. Ekstra i "actual" betyder et endpoint uden for kontrakten; ekstra i "expected" betyder et endpoint der er lovet, men ikke bygget.

**Step 3: Bevis at testen virker**

Tilføj midlertidigt `app.MapGet("/api/oops", () => "x");` i `TodoHost.Build`.

Run: `dotnet test tests/Todo.Api.Tests`
Expected: **FAIL** — `GET /api/oops` optræder kun i actual.

Fjern linjen igen og kør testen: **2 passed**. En drift-test man ikke har set fejle, er ikke en test.

**Step 4: Commit**

```bash
git add -A && git commit -m "✅ Add contract drift test between openapi.yaml and the running API"
```

**Note:** Testen sammenligner sti + metode. Skemasammenligning er bevidst udeladt — .NET 10 udsteder OpenAPI 3.1 mens kontrakten er 3.0.4, så en naiv skema-diff ville støje. Udvides i skive 3, hvor der er DTO'er der reelt kan afvige.

---

## Task 6: Test for forældet genereret kode

**Files:**
- Create: `tests/Todo.Api.Tests/GeneratedCodeFreshnessTests.cs`

**Step 1: Skriv testen**

```csharp
using System.Security.Cryptography;
using System.Text;
using Todo.TestSupport;

namespace Todo.Api.Tests;

public class GeneratedCodeFreshnessTests
{
    [Fact]
    public void Generated_code_matches_the_current_contract()
    {
        var hashFile = Path.Combine(
            RepoPaths.Root, "src", "Todo.Contracts", "Generated", ".source-hash");

        Assert.True(File.Exists(hashFile),
            "Generated code is missing. Run scripts/generate-api.ps1.");

        var recorded = File.ReadAllText(hashFile).Trim();

        // Must match the normalisation in scripts/generate-api.ps1, or the hash
        // becomes dependent on the checkout's line endings.
        var normalized = File.ReadAllText(RepoPaths.ContractFile).Replace("\r\n", "\n");
        var current = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));

        Assert.True(
            string.Equals(recorded, current, StringComparison.OrdinalIgnoreCase),
            "contracts/openapi.yaml changed without regenerating. Run scripts/generate-api.ps1 and commit the result.");
    }
}
```

**Step 2: Kør**

Run: `dotnet test tests/Todo.Api.Tests`
Expected: **3 passed**.

**Step 3: Bevis at testen virker**

Tilføj en blank linje sidst i `contracts/openapi.yaml`, kør testen igen.
Expected: **FAIL** med "changed without regenerating".

Fjern linjen igen; **3 passed**.

**Step 4: Commit**

```bash
git add -A && git commit -m "✅ Fail the build when generated code lags the contract"
```

---

## Task 7: Photino-vindue og `--headless`

**Files:**
- Create: `src/Todo.Host/Program.cs`

**Step 1: Skriv `Program.cs`**

```csharp
using System.Drawing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using PhotinoNET;

namespace Todo.Host;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // A bare "--headless" is not valid command-line configuration syntax, so it is
        // stripped before the arguments reach the host builder.
        var headless = args.Contains("--headless", StringComparer.OrdinalIgnoreCase);
        var hostArgs = args.Where(a =>
            !string.Equals(a, "--headless", StringComparison.OrdinalIgnoreCase)).ToArray();

        var app = TodoHost.Build(hostArgs);

        if (headless)
        {
            app.Run();
            return;
        }

        app.Start();

        var url = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        new PhotinoWindow()
            .SetTitle("Todo")
            .SetUseOsDefaultSize(false)
            .SetSize(new Size(1200, 900))
            .Center()
            .Load(new Uri(url))
            .WaitForClose();

        app.StopAsync().GetAwaiter().GetResult();
    }
}
```

**Step 2: Sæt en standardadresse i `TodoHost.Build`**

Indsæt lige efter `var builder = WebApplication.CreateBuilder(args);`:

```csharp
        if (builder.Configuration["urls"] is null)
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
        }
```

Vinduet skal have en tilfældig ledig port, ikke en fast en der kan være optaget.

**Step 3: Byg**

Run: `dotnet build Todo.sln`
Expected: `Build succeeded`.

**Step 4: Røgtest — headless**

Run: `dotnet run --project src/Todo.Host -- --headless --urls http://127.0.0.1:5199`

Åbn `http://127.0.0.1:5199/api/health` i en browser.
Expected: `{"status":"ok","version":"1.0.0.0"}`. Stop med Ctrl+C.

**Step 5: Røgtest — vindue**

Run: `dotnet run --project src/Todo.Host`
Expected: Et vindue med titlen "Todo" åbner. Det viser 404, fordi Angular ikke er bygget endnu — det er forventet og løses i task 9. Luk vinduet; processen skal afslutte af sig selv.

Åbner intet vindue, mangler WebView2-runtime — hent "Evergreen Bootstrapper" fra Microsoft.

**Step 6: Kør alle tests igen**

Run: `dotnet test Todo.sln`
Expected: **3 passed**. Testene rører ikke `Program.Main`, så de skal være upåvirkede.

**Step 7: Commit**

```bash
git add -A && git commit -m "✨ Open a Photino window over the host, with a headless switch"
```

---

## Task 8: Angular-appen

**Files:**
- Create: `src/Todo.Web/**` (via `ng new`)
- Modify: `src/Todo.Web/src/app/app.config.ts`
- Modify: `src/Todo.Web/src/app/app.ts`
- Modify: `src/Todo.Web/src/app/app.html`

**Step 1: Opret Angular-appen**

Kør i repo-roden:

```bash
ng new todo-web --directory src/Todo.Web --style=scss --ssr=false --skip-git --package-manager=npm
```

Afvises et flag af CLI'en, så drop netop det flag og svar på prompten i stedet.

**`ng new` kan nægte at scaffolde ind i `src/Todo.Web`,** fordi mappen ikke længere er tom — task 3 har allerede lagt den genererede `src/app/api/todo-client.ts` der. Sker det: flyt `src/Todo.Web/src/app/api` midlertidigt væk (fx til `src/Todo.Web.api-backup`), kør `ng new`, flyt mappen tilbage og kør generatoren igen. **Slet ikke den genererede klient** — den er committet med vilje, og et sletningsspor ville se ud som om task 3 aldrig blev kørt.

**Step 2: Genskab den genererede klient**

`ng new` overskrev måske ikke, men kan have ryddet mappen. Kør igen:

Run: `powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1`
Expected: `src/Todo.Web/src/app/api/todo-client.ts` findes.

Åbn filen og notér navnet på den genererede service — den bør hedde `HealthClient` med metoden `getHealth()`. Hedder den `Client`, så mangler `/operationGenerationMode:MultipleClientsFromFirstTagAndOperationId`.

**Step 3: Registrér HttpClient og base-URL**

I `src/Todo.Web/src/app/app.config.ts`, tilføj til `providers`:

```ts
import { provideHttpClient } from '@angular/common/http';
import { API_BASE_URL } from './api/todo-client';

// inde i appConfig.providers:
provideHttpClient(),
{ provide: API_BASE_URL, useValue: '' },
```

Tom base-URL, fordi appen serveres fra samme origin som API'et.

**Step 4: Kald API'et fra rodkomponenten**

`src/Todo.Web/src/app/app.ts` (i Angular 20+ hedder filen `app.ts`; hedder den `app.component.ts` hos dig, så brug det navn):

```ts
import { Component, inject, signal } from '@angular/core';
import { HealthClient, HealthResponse } from './api/todo-client';

@Component({
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  private readonly health = inject(HealthClient);

  protected readonly status = signal<HealthResponse | undefined>(undefined);
  protected readonly failed = signal(false);

  constructor() {
    this.health.getHealth().subscribe({
      next: (r) => this.status.set(r),
      error: () => this.failed.set(true),
    });
  }
}
```

`src/Todo.Web/src/app/app.html`:

```html
<main>
  <h1>Todo</h1>
  @if (status(); as s) {
    <p data-testid="health">API: {{ s.status }} (v{{ s.version }})</p>
  } @else if (failed()) {
    <p data-testid="health">API: unavailable</p>
  }
</main>
```

`data-testid` er E2E-testens greb om elementet. Det skal ikke ændres uden at testen ændres med.

**Step 5: Verificér i browseren**

Start hosten i én terminal:

Run: `dotnet run --project src/Todo.Host -- --headless --urls http://127.0.0.1:5199`

Og Angular i en anden — bemærk `npm.cmd`, ikke `npm` (PowerShell-shimmen er i stykker på denne maskine):

Run: `npm.cmd start --prefix src/Todo.Web`

Åbn `http://localhost:4200`.
Expected: Overskriften "Todo". Health-linjen viser sandsynligvis "unavailable" endnu — proxyen kommer i næste task.

**Step 6: Commit**

```bash
git add -A && git commit -m "✨ Add Angular app that reads the API health endpoint"
```

---

## Task 9: Servér Angular fra hosten

**Files:**
- Modify: `src/Todo.Web/angular.json`
- Create: `src/Todo.Web/proxy.conf.json`
- Create: `scripts/build-web.ps1`

**Step 1: Byg Angular direkte ind i `wwwroot`**

I `src/Todo.Web/angular.json`, under `projects.todo-web.architect.build.options`, erstat `outputPath`:

```json
"outputPath": {
  "base": "../Todo.Host/wwwroot",
  "browser": ""
}
```

`"browser": ""` lægger filerne direkte i `wwwroot` i stedet for i en `browser`-undermappe.

**Step 2: Opret `src/Todo.Web/proxy.conf.json`**

```json
{
  "/api": { "target": "http://127.0.0.1:5199", "secure": false },
  "/openapi": { "target": "http://127.0.0.1:5199", "secure": false }
}
```

**Step 3: Slå proxyen til**

I `angular.json` under `projects.todo-web.architect.serve.options`, tilføj:

```json
"proxyConfig": "proxy.conf.json"
```

Findes `options` ikke under `serve`, så opret objektet.

**Step 4: Opret `scripts/build-web.ps1`**

```powershell
#Requires -Version 5.1
$ErrorActionPreference = 'Stop'

$web = Join-Path (Split-Path -Parent $PSScriptRoot) 'src\Todo.Web'
if (-not (Test-Path (Join-Path $web 'node_modules'))) {
    & npm.cmd ci --prefix $web
    if ($LASTEXITCODE -ne 0) { throw "npm ci failed ($LASTEXITCODE)" }
}
& npm.cmd run build --prefix $web
if ($LASTEXITCODE -ne 0) { throw "ng build failed ($LASTEXITCODE)" }
```

**Step 5: Byg og verificér**

Run: `powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1`
Expected: `src/Todo.Host/wwwroot/index.html` findes.

Run: `dotnet run --project src/Todo.Host`
Expected: Photino-vinduet viser "Todo" **og** linjen "API: ok (v1.0.0.0)".

Det er første gang hele kæden kører: Angular → genereret klient → minimal API → kontrakt.

**Step 6: Commit**

```bash
git add -A && git commit -m "✨ Serve the Angular app from the host and proxy the API in dev"
```

---

## Task 10: E2E-testen

**Files:**
- Create: `tests/Todo.E2E/Todo.E2E.csproj`
- Create: `tests/Todo.E2E/BrowserFixture.cs`
- Create: `tests/Todo.E2E/AppSmokeTests.cs`

**Step 1: Opret projektet**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="xunit.v3" Version="3.2.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Microsoft.Playwright" Version="1.62.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Todo.TestSupport\Todo.TestSupport.csproj" />
  </ItemGroup>
</Project>
```

**Step 2: Skriv browser-fixturen**

Maskinen har ikke `pwsh`, så `playwright.ps1` kan ikke bruges. Browseren installeres i stedet gennem Playwrights egen entrypoint.

`tests/Todo.E2E/BrowserFixture.cs`:

```csharp
using Microsoft.Playwright;

namespace Todo.E2E;

public sealed class BrowserFixture : IAsyncLifetime
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        // No pwsh on this machine, so playwright.ps1 is unavailable; this is the
        // supported alternative and is a no-op once the browser is present.
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Playwright browser install failed ({exitCode}).");
        }

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new() { Headless = true });
    }

    public async ValueTask DisposeAsync()
    {
        await Browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
```

**Step 3: Skriv testen**

`tests/Todo.E2E/AppSmokeTests.cs`:

```csharp
using Microsoft.Playwright;
using Todo.TestSupport;

namespace Todo.E2E;

public class AppSmokeTests(BrowserFixture fixture) : IClassFixture<BrowserFixture>
{
    [Fact]
    public async Task App_loads_and_shows_api_health()
    {
        var index = Path.Combine(RepoPaths.HostContentRoot, "wwwroot", "index.html");
        Assert.True(File.Exists(index),
            "The Angular app has not been built. Run scripts/build-web.ps1 first.");

        await using var host = await RunningHost.StartAsync();
        var page = await fixture.Browser.NewPageAsync();

        await page.GotoAsync(host.BaseUrl);

        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Todo" }))
            .ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("health"))
            .ToContainTextAsync("API: ok");
    }
}
```

**Step 4: Tilføj til solution og kør**

```bash
dotnet sln add tests/Todo.E2E/Todo.E2E.csproj
```

Run: `dotnet test tests/Todo.E2E`
Expected: Første kørsel henter Chromium (~1-2 min), derefter **1 passed**.

**Step 5: Bevis at testen virker**

Omdøb midlertidigt overskriften i `app.html` fra `Todo` til `Todoo`, byg web igen og kør E2E.
Expected: **FAIL** på heading-assertionen.

Ret tilbage, byg, kør: **1 passed**.

**Step 6: Commit**

```bash
git add -A && git commit -m "✅ Add Playwright end-to-end smoke test"
```

---

## Task 11: README og samlet grøn kørsel

**Files:**
- Create: `README.md`

**Step 1: Skriv `README.md`**

```markdown
# Todo

Personlig todo-app. Design: `docs/plans/2026-08-13-todo-app-design.md`.

## Kom i gang

```powershell
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet run --project src\Todo.Host
```

## Udvikling med hot reload

Terminal 1:

```powershell
dotnet run --project src\Todo.Host -- --headless --urls http://127.0.0.1:5199
```

Terminal 2:

```powershell
npm.cmd start --prefix src\Todo.Web
```

Åbn http://localhost:4200. Brug `npm.cmd`, ikke `npm` — PowerShell-shimmen er i stykker.

## Tests

```powershell
dotnet test Todo.sln
```

E2E kræver at `scripts\build-web.ps1` er kørt først.

## Kontrakten

`contracts/openapi.yaml` ejer API'et. Ændrer du den, så kør `scripts\generate-api.ps1`
og commit den genererede kode — ellers fejler `GeneratedCodeFreshnessTests`.
```

**Step 2: Kør alt fra bunden**

```bash
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
```

```bash
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
```

Run: `dotnet test Todo.sln`
Expected: **4 passed** (3 i Todo.Api.Tests, 1 i Todo.E2E), 0 failed.

**Step 3: Commit**

```bash
git add -A && git commit -m "📝 Add README with setup, development and test instructions"
```

---

## Færdig når

- `dotnet test Todo.sln` er grøn med 4 tests.
- `dotnet run --project src/Todo.Host` åbner et vindue der viser "Todo" og "API: ok".
- Drift-testen og friskheds-testen er hver set fejle mindst én gang.
- Alt er committet, og arbejdstræet er rent.

## Til skive 1

- EF Core + SQLite. Bemærk: den globale `dotnet-ef` er 7.0.16 og skal opdateres til 10.x, eller installeres som lokalt værktøj i `.config/dotnet-tools.json`.
- Vælg unit-test-runner til Angular (Vitest eller Karma) — udskudt fordi der intet er at teste i skive 0.
- Udvid drift-testen med skemasammenligning, når der findes DTO'er der reelt kan afvige.
