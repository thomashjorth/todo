# Todo

Personlig todo-app. Design: `docs/plans/2026-08-13-todo-app-design.md`.

## Kom i gang

```
powershell -ExecutionPolicy Bypass -File scripts\generate-api.ps1
powershell -ExecutionPolicy Bypass -File scripts\build-web.ps1
dotnet run --project src\Todo.Host
```

## Udvikling med hot reload

Terminal 1:

```
dotnet run --project src\Todo.Host -- --headless --urls http://127.0.0.1:5199
```

Terminal 2:

```
npm.cmd start --prefix src\Todo.Web
```

Åbn http://localhost:4200. Brug `npm.cmd`, ikke `npm` — PowerShell-shimmen er i stykker.

## Retro-import

Skærmen hedder **Retro-import** i menuen øverst og ligger på ruten `/import`. Indsæt
CSV-eksporten fra retro-boardet, tryk **Analysér**, vælg de rækker der skal med, og tryk
**Importér**. Afstemningskort — rækker hvor indholdet bare er et tal som `8` eller `9/10`
— bliver aldrig til opgaver, og skærmen fortæller hvor mange den sprang over.

Dine aliaser afgør hvad der er dit: en række er din, når `Action Owner` matcher et af
navnene under "Hvem er du på boardet?". Dem forudvælger appen, og et indledende
`"NAVN - "` fjernes fra titlen. Tilføjer du et alias, genanalyseres listen med det samme
— du skal ikke indsætte eksporten igen. Har du ikke deltaget i retroen, er ingen af
rækkerne dine, og skærmen siger det frem for bare at stå tom.

Det er sikkert at importere det samme board igen. Hver række genkendes på `Content` +
`Zone` + `Author` + `Created`, så en række du allerede har importeret står som
"importeret tidligere" og kan ikke vælges igen.

## Tests

Backend og end-to-end (xUnit):

```
dotnet test Todo.sln
```

Angular-unittests (Vitest):

```
npm.cmd run test --prefix src\Todo.Web -- --watch=false
```

E2E kræver at `scripts\build-web.ps1` er kørt først, så den byggede app ligger i
`src\Todo.Host\wwwroot`. Første E2E-kørsel henter Chromium ned og tager derfor
et par minutter.

Testdata arrangeres med builderne i `tests\Todo.TestSupport\Builders`:

```csharp
await host.AddAndSaveChangesAsync(
    new TaskItemBuilder().Titled("Køb kaffe").DueToday().RequestedBy("Anna").Build(),
    UserAliases.Named("Thomas Hjorth Hansen"));
```

De skriver direkte i databasen og går dermed uden om API'ets validering. Brug dem til at
arrangere en tilstand — aldrig til at udføre den handling en test skal verificere.

E2E-tests navigerer gennem `TodoApp` i `tests\Todo.E2E`, som venter på at skærmen er
tegnet, før den giver testen et skærmobjekt. Hvert skærmobjekt ejer sine egne
`data-testid`-selektorer, så en omdøbning i markup rammer én fil.

## Databasen

Databasen ligger i `%APPDATA%\EdoraTodo\todo.db`. Migrationer køres ved opstart;
findes der ventende migrationer, tages der først en kopi som
`todo.db.bak-<tidsstempel>`. Vil du nulstille alt, så slet filen.

Ny migration:

```
dotnet tool run dotnet-ef migrations add <Navn> --project src\Todo.Core --startup-project src\Todo.Host
```

Brug `dotnet tool run dotnet-ef`, ikke `dotnet ef` — den globalt installerede
`dotnet-ef` 7.0.16 kan ikke læse en EF Core 10-model.

## Kontrakten

`contracts/openapi.yaml` ejer API'et. Ændrer du den, så kør `scripts\generate-api.ps1`
og commit den genererede kode — ellers fejler `GeneratedCodeFreshnessTests`.

## Styling

Al styling er standard Tailwind utility-klasser. Der skrives ingen CSS eller SCSS
i dette projekt. Appen bruges i en spalte på cirka 480 px, hvilket er under
Tailwinds `sm`-brydepunkt — de uprefixede klasser er den smalle udgave, og
`sm:`/`md:` bruges kun til at udvide.
