# Web-udgaven 2.0.0, leverance 4: hjælperen og tunnelen — leveranceplan

> **For Claude:** KRÆVET UNDER-SKILL: brug `superpowers:executing-plans` til at køre den her plan
> opgave for opgave. Én opgave ad gangen, hver med sin egen commit.

**Mål:** AI-, Jira- og ADO-kaldene går fra værten gennem en WebSocket som `Todo.Helper` på PC'en selv
har åbnet; hjælperen sætter `Authorization` og viderestiller kun til sin egen hvidliste. Værten har
aldrig et token og aldrig en basis-URL i databasen.

**Designet er `docs/plans/2026-10-26-web-hosting-design.md`, afsnit 2 og 3.** Køreplanen er
`docs/plans/2026-10-26-web-hosting.md`.

**Planen kan være forkert.** Mål frem for at tro, og ret planfilen når en måling modsiger den.

## Grenen: leverance 4 og 5 lander sammen

Arbejdet sker på `web-hosting/helper` og flettes til `main` **først når leverance 5 er færdig**.
`Todo.cmd` kører `main` mod brugerens rigtige database; fjernede leverance 4 tokens og basis-URL'er
fra databasen før leverance 5 gav et sted at taste dem, mistede den daglige brug Jira og ADO i
mellemtiden. CI kører på grenen (test-jobbet), men skubber intet image — det gør kun `main`.

## Arkitekturen, som koden gør den

- **Sømmen er settings-readerne.** `JiraSettings` og `AdoSettings` bærer i dag `BaseUrl` og `Token`
  fra databasen, og alt nedstrøms spørger kun `IsConfigured`, `BaseUri` og `Token`. De to felter
  kommer fremover fra **`ISourceConnections`** (Core): `SourceConnection(BaseUrl, Token, Authorized)`
  pr. kilde. `Token` er tokenet *denne proces* sætter på kaldet — `null` når hjælperen gør det.
  `Authorized` siger at kaldene bliver autoriseret, af nogen. `IsConfigured` = basis-URL og
  `Authorized`. Kilderne sætter kun `Authorization`, når `Token` ikke er `null`.
- **To tilstande, valgt i `TodoHost.Build` af `Sources:Mode`:**
  - `Helper` (standard): `HelperConnections` svarer ud fra hjælperens hello, og de tre typed clients
    (`JiraTaskSource`, `AdoTaskSource`, `OllamaClient`) får `TunnelHandler` som primær handler.
  - `Direct` (testene): ingen tunnel, og `ISourceConnections` er `NoSourceConnections`, som en test
    erstatter. Valgt i konfigurationen og ikke af `configureServices`, fordi `configureServices`
    kører efter registreringerne og ikke kan fjerne en handler (`prep-refactor`, leverance 1).
- **Tunnel-endpointet** `GET /api/helper/tunnel` er en WebSocket uden cookie, autoriseret af
  hjælperens nøgle mod `Helper:KeyHash` (samme hash-format som passwordet). Rutevagten i
  `AuthEndpointsTests` får den som tredje åbne rute, med begrundelsen ved siden af.
- **Protokollen** er JSON-tekstbeskeder: `hello` (protokolversion, basis-URL'er, hvilke tokens
  hjælperen har), `request`/`response` med korrelations-id og base64-kroppe, og `refused` med en
  grund. Version uden for det værten kender → `refused`, og hjælperen siger "opdatér".
- **Ingen hjælper** → `HelperOfflineException`, som kilderne oversætter til `helper.offline` og
  Ollama-klienten til "ikke klar" — den tilstand AI allerede har.

## Hjælperen

- `src/Todo.Helper`: en .NET 10-konsolapp. Tunnel-klient (`ClientWebSocket` ud til værten,
  genforbindelse med backoff), et lokalt API på `127.0.0.1:47600` til leverance 5's microfrontend, og
  et lager i `%APPDATA%\TodoHelper\helper.json` hvor tokens og nøglen er krypteret med DPAPI.
- **Hvidlisten** bygges af hjælperens egne basis-URL'er plus Ollamas adresse: skema, vært, port og
  sti-præfiks skal passe. Alt andet afvises og logges.
- **`Authorization` sættes af hjælperen pr. mål** — Bearer for Jira, Basic `":" + PAT` for ADO,
  intet for Ollama — og en header fra værten fjernes altid først.
- **Det lokale API kræver headeren `X-Todo-Helper` og en `Host` på 127.0.0.1/localhost.** Headeren
  tvinger en CORS-preflight som intet svarer på, så en fremmed side i browseren ikke kan ændre en
  basis-URL og dermed sende tokenet et andet sted hen; `Host`-tjekket lukker DNS-rebinding.
- `Helper.cmd` starter den med synligt konsolvindue. Autostart er brugerens valg og hører til
  leverance 5.

## Opgaver

1. **Core:** `SourceConnection`, `ISourceConnections`, `NoSourceConnections`; `JiraSettings` og
   `AdoSettings` får `Authorized`; readerne læser forbindelsen; kilderne sætter kun en header de har.
   `FakeSourceConnections` i TestSupport, registreret af `RunningHost`, og testene der i dag sår
   `jira.baseUrl`/`jira.token`/`ado.*` som rækker, sætter forbindelsen i stedet.
2. **Migrering** der sletter `jira.baseUrl`, `jira.token`, `ado.baseUrl` og `ado.token`; token-ruterne
   og basis-URL-felterne forlader kontrakten, `SettingsResponse` får hjælperens status i stedet.
3. **Tunnelen på værten:** endpointet, `HelperHub`, `TunnelHandler`, `HelperConnections`,
   `helper.offline`, `Sources:Mode`, og tests mod en falsk hjælper i testprocessen.
4. **`Todo.Helper`** med lager, hvidliste, header pr. mål, tunnel-klient og lokalt API, og
   `Todo.Helper.Tests`.
5. **Integrationen:** en test der starter værten i `Helper`-tilstand, hjælperen i samme proces og en
   `FakeJira`, og fastslår at tokenet når Jira og **aldrig** værten.
6. **Frontenden:** Jira- og ADO-grupperne mister basis-URL og token og viser hjælperens status;
   `helper.offline` på begge sprog.
7. **Scripts, docs, `Check.cmd`, push af grenen og grøn CI på den.**
