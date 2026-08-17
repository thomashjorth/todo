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

## Noter i markdown

Noten på en opgave skrives i markdown og vises renderet — fed skrift, punktopstillinger,
links, kodeblokke og tabeller. Klik på noten for at redigere den; så bytter den renderede
tekst plads med et tekstfelt med den rå markdown. **Esc** gemmer og lukker igen, og det
samme gør et klik ud af feltet. Der er ingen gem-knap. Er noten tom, står der en linje
i stedet, og den kan klikkes på præcis som en note med indhold.

Et klik på et link i noten åbner det i systemets browser i stedet for inde i vinduet.
Appvinduet har ingen adresselinje, så et link der blev fulgt indeni, ville ikke kunne
findes tilbage fra. Kun `http` og `https` åbnes — et link til noget andet ville lade en
note starte et program på maskinen — og afvises det, står begrundelsen under noten.

## Venter på og Måske

To statusser tager en opgave ud af deadline-sektionerne, uden at den bliver væk.

**Venter på** er til det, du har afleveret, og som en anden skal svare på. Vælger
du den status, kommer der et felt til hvem du venter på, og opgaven flytter ned i
sin egen sektion **Venter på**. Den bliver ved med at vise sig — det er hele
pointen — og linjen under titlen tæller hvor længe: `0 dage` den dag du sætter
statussen, `12 dage` tolv dage senere. Tælleren starter altså når du sætter
statussen, ikke da opgaven blev oprettet, og den flytter sig ikke af at du retter
noget andet på opgaven imens. Sætter du opgaven tilbage til Åben, glemmes både
navnet og datoen; begynder den at vente igen, tælles der forfra fra nul.

**Måske** er det modsatte: den gemmer sig. En parkeret opgave forsvinder fra
listen og kommer først frem igen under **Måske**, når du slår **Vis måske** til.
Sådan kan listen holdes kort uden at noget skal slettes.

## Sprog og indstillinger

Skærmen hedder **Indstillinger** i menuen øverst og ligger på ruten `/settings`. Her
vælger du sprog, og her retter du dine navne på retro-boardet.

Sproget har tre valg: **Følg systemet**, **Dansk** og **Engelsk**. "Følg systemet" er
standarden — så læses sproget af browserens `navigator.language`, og alt der ikke er
dansk bliver engelsk. Et valg slår igennem med det samme, uden at siden genindlæses.

Valget gemmes i databasen, ikke i browseren, så det overlever en genstart af appen.
Vælger du "Følg systemet" igen, slettes indstillingen frem for at blive gemt som en værdi.

Deadlines skrives på det aktive sprog — `14. aug. 2026` mod `Aug 14, 2026`. Datoen bygges
af de tre tal i `yyyy-MM-dd` og aldrig ved at lade en `Date` fortolke strengen:
`new Date("2026-08-14")` er midnat UTC og ville stå som den 13. vest for Greenwich.

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

Databasen ligger i `%APPDATA%\TodoApp\todo.db`. Migrationer køres ved opstart;
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
