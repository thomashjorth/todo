# Web-udgaven 2.0.0, leverance 2: login — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit.

**Mål:** Alt under `/api/` undtagen health og login kræver en cookie, værten nægter at starte uden en
password-hash, og hver test kører login-vejen.

**Designet er `docs/plans/2026-10-26-web-hosting-design.md`, afsnit 5.** Køreplanen og leverance 1 er
`docs/plans/2026-10-26-web-hosting.md`; dens afsnit om leverance 2 har de fire fund `prep-refactor`
gjorde, og de står alle fire i opgaverne nedenfor.

**Arkitektur:** ASP.NET Core's cookie-godkendelse uden Identity — én bruger, én hash i
konfigurationen (`Auth:PasswordHash`), ingen brugertabel. Hashen er PBKDF2-SHA256 i et format der
bærer sine egne iterationer, så testene kan bruge én iteration og produktionen 600.000. Login har en
rate-limit. Data protection-nøglerne, som krypterer cookien, ligger **ved siden af databasen**, så en
ny container ikke logger brugeren ud ved hvert deploy. Frontenden får en login-side og en
interceptor, der sender et 401 dertil; efter login genindlæses hele siden.

**Teknologi:** .NET 10 (`Microsoft.AspNetCore.Authentication.Cookies`, `Microsoft.AspNetCore.RateLimiting`,
`Microsoft.AspNetCore.DataProtection` — alle i shared framework, ingen nye pakker), Angular 22,
xUnit, Playwright, Vitest.

**Planen kan være forkert.** Mål frem for at tro, og ret planfilen når en måling modsiger den.

## Beslutninger truffet i planen

- **Hashen ligger i en miljøvariabel, også lokalt** — `Auth__PasswordHash` på brugerniveau, sat én
  gang af `SetPassword.cmd`. Samme mekanisme som containeren, så der kun er én vej at teste.
- **Passwordet når hash-værktøjet gennem en miljøvariabel, ikke stdin.** PowerShell 5.1 piper til en
  native kommando med `$OutputEncoding` = ASCII, og .NET læser en omdirigeret stdin med konsollens
  OEM-tegntabel. Et password med æøå ville blive hashet som noget andet end det browseren sender —
  og login ville fejle uden at nogen kunne se hvorfor. Miljøvariabler er UTF-16 hele vejen. Umålt,
  men begge halvdele er dokumenteret adfærd; vejen udenom koster intet.
- **Ingen log ud.** Én bruger, cookie på 400 dage. YAGNI.
- **Ingen retur-URL efter login.** Login lander altid på `/`.
- **Den statiske frontend, `/scalar/` og `/openapi/*` er åbne.** Login-siden skal kunne indlæses, og
  dokumentationen viser API'ets form, ikke data.
- **Login-siden står på systemets sprog**, fordi sproget er en indstilling bag login.
  `SettingsStore.start()` tåler allerede en fejl og falder tilbage på netop det.

## Fælder der rammer netop denne plan

Alle fælderne fra leverance 1 gælder. Dertil:

- **`HostContentRootTests` bygger værten uden om `RunningHost`** og skal have test-hashen med.
- **`page.APIRequest` deler cookies med browserkonteksten** — det er dét, der lader E2E logge ind
  med ét kald frem for gennem siden. Antaget fra Playwrights dokumentation; mål det i opgave 5.
- **Rate-limiteren er pr. vært**, og hver test har sin egen vært. En test der vil se 429, skal selv
  lave elleve forsøg.

---

### Opgave 1: Password-hashen (Core)

**Filer:** opret `src/Todo.Core/Auth/PasswordHash.cs`, test `tests/Todo.Core.Tests/Auth/PasswordHashTests.cs`.

Format: `pbkdf2-sha256$<iterationer>$<salt base64>$<hash base64>`, 16 bytes salt, 32 bytes hash.
`Create(password, iterations = 600_000)`, `Verify(password, stored)` med
`CryptographicOperations.FixedTimeEquals`, `IsWellFormed(stored)`. Tests først: rundtur, forkert
password, to hashes af samme password er forskellige (saltet), æøå rundtur, iterationerne læses af
strengen (en hash med 1 iteration verificerer), og hver form for ødelagt streng er ikke velformet og
verificerer ikke (forkert præfiks, ikke-tal, negativ, base64-fejl, forkert længde, tom).

### Opgave 2: Værten kræver hashen, og hash-værktøjet

**Filer:** `src/Todo.Host/TodoHost.cs`, `Program.cs`, `tests/Todo.TestSupport/RunningHost.cs`,
`tests/Todo.Api.Tests/HostContentRootTests.cs`, test `tests/Todo.Api.Tests/AuthConfigurationTests.cs`.

- `TodoHost.Build` kaster med en sætning der nævner `SetPassword.cmd` og `Auth__PasswordHash`, hvis
  `Auth:PasswordHash` mangler eller ikke er velformet. Tests: mangler, ødelagt.
- `RunningHost.TestPassword` og `TestPasswordHash` (én iteration), givet til hver vært før
  `extraArgs`, så en test kan overskrive den.
- `Program.Main`: verbet `hash-password` læser `TODO_PASSWORD` fra miljøet, nægter et tomt, og
  skriver hashen alene på stdout.

### Opgave 3: Cookie-login, rate-limit og beskyttede ruter

**Filer:** kontrakten (`/api/auth/login`, `LoginRequest`), `scripts\generate-api.ps1`,
`src/Todo.Core/Errors/ErrorCodes.cs` (`auth.wrongPassword`, `auth.tooManyAttempts`), begge
sprogfiler, opret `src/Todo.Host/Endpoints/AuthEndpoints.cs`, `TodoHost.cs`, test
`tests/Todo.Api.Tests/AuthEndpointsTests.cs`.

- Cookie: `HttpOnly`, `SameSite=Strict`, `SecurePolicy=SameAsRequest` (LAN'et er http),
  glidende 400 dage, og `OnRedirectToLogin`/`OnRedirectToAccessDenied` svarer 401/403 frem for 302.
- Data protection: `PersistKeysToFileSystem` i `Data:KeysPath`, som standard mappen `keys` ved siden
  af databasen.
- Rate-limit: fast vindue, 10 forsøg pr. minut pr. IP på login; afvisningen skriver
  `auth.tooManyAttempts` som `ApiError` med 429.
- Alle `Map*` undtagen health og login ligger i `app.MapGroup("").RequireAuthorization()`.
- Tests: rigtigt password giver 204 og en cookie med de tre flag, og cookien åbner `/api/tasks`;
  forkert giver 400 `auth.wrongPassword` og ingen cookie; anonymt `/api/tasks` er 401 og ikke 302;
  health er åben; elleve forkerte giver 429 `auth.tooManyAttempts`; **og en vagt der går hver rute i
  `EndpointDataSource` igennem** og kræver autorisation på alt under `/api/` undtagen de to — så en
  ny `Map` uden for gruppen fælder den. Set fejle ved at flytte `MapTags()` ud af gruppen.

### Opgave 4: `RunningHost` og `ApiTest` logger ind

**Filer:** `RunningHost.cs`.

`Client` får en `CookieContainer` og logger ind i `StartAsync`; `AnonymousClient()` giver en frisk
uden cookie. `Data:KeysPath` sættes til `<database>.keys` og slettes med databasen, når værten ejer
den. Hele `Todo.Api.Tests` skal være grøn uden andre ændringer — det er beviset for at login-vejen
ikke har rykket noget.

### Opgave 5: E2E logger ind

**Filer:** `tests/Todo.E2E/TodoApp.cs`.

`OpenAsync` logger ind med `page.APIRequest` før `GotoAsync`. `OpenLoggedOutAsync` åbner uden.
Hele E2E-suiten skal være grøn — **men først efter opgave 6**, fordi frontenden indtil da ikke ved
hvad et 401 er. Mål i denne opgave kun at `page.APIRequest`s cookie når siden: et anonymt `GotoAsync`
efterfulgt af en `fetch('/api/tasks')` fra siden skal give 200.

### Opgave 6: Login-siden og interceptoren

**Filer:** opret `src/Todo.Web/src/app/auth/` (`auth-store.ts`, `login.ts`, `login.html`,
`unauthorized.interceptor.ts`, `reload.ts` + specs), `app.config.ts`, `app.routes.ts`, `app.html`,
`app.ts`, begge sprogfiler.

- `AuthStore.login(password)` kalder den genererede klient; lykkes det, `RELOAD('/')` — et
  `InjectionToken` med `window.location.assign` som standard, så en spec kan se det.
- `login.html`: en `<form>` med et `type="password"`-felt (`autocomplete="current-password"`) og en
  knap; ingen Angular forms. Fejllinjen bruger `apiErrorMessage`.
- Interceptoren sender et 401 til `/login`, medmindre siden allerede står der.
- `app.html` skjuler navigationen på `/login`. Health-linjen bliver; den er offentlig.
- Specs: storen (lykkes → genindlæsning; afvist → fejltekst), siden (sender passwordet, viser fejlen),
  interceptoren (401 → `/login`, 400 → intet, allerede på `/login` → intet).

### Opgave 7: Rejsen og kontrasten

**Filer:** opret `tests/Todo.E2E/LoginJourneyTests.cs`, `tests/Todo.E2E/LoginScreen.cs`,
`ContrastTests.cs`.

- Rejsen: anonymt åbnes `/`, login-siden kommer, et forkert password giver sætningen, det rigtige
  lander på opgavelisten, og en genindlæsning bliver der.
- Kontrasten: login-siden i begge temaer med fejllinjen på skærmen. Påstå at linjen *er* der før
  målingen.

### Opgave 8: Scripts og dokumentation

- `SetPassword.cmd` + `scripts\set-password.ps1`: `Read-Host -AsSecureString` to gange, ens, ikke
  tomme; `TODO_PASSWORD` i processens miljø; `dotnet build` og `dotnet run --no-build -- hash-password`;
  den sidste linje der ligner en hash; `[Environment]::SetEnvironmentVariable('Auth__PasswordHash',
  …, 'User')`; ryd `TODO_PASSWORD`.
- `run-app.ps1`: læs `Auth__PasswordHash` fra brugerniveau hvis processen ikke har den, og stop
  med en sætning om `SetPassword.cmd` hvis ingen af dem har.
- `publish.ps1`: proben får en fast engangs-hash med én iteration — den beskytter ingenting.
- README, CLAUDE.md (fire kommandoer, login i testdisciplinen, syv ruter) og HANDOFF (kør
  `SetPassword.cmd` én gang før `Todo.cmd`).

### Opgave 9: `Check.cmd` og testtallene med deres grunde
